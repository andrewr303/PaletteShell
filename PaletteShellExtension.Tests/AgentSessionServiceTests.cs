using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using PaletteShellExtension.Classes;
using Xunit;

namespace PaletteShellExtension.Tests;

/// <summary>In-memory stand-in for the Settings-backed values Agent sessions uses.</summary>
internal sealed class FakeSessionSettings : IAgentSessionSettings
{
    public string AgentsText { get; set; } = AgentCliList.DefaultText;

    public string FavoriteFoldersText { get; set; } = "";

    public string? DefaultSessionFolder { get; set; }

    public AgentLaunchSettings LaunchSettings { get; set; } = new();

    public bool ShowAgentsInRoot { get; set; } = true;

    public event EventHandler? AgentSessionSettingsChanged;

    /// <summary>Simulates the user saving the Settings page.</summary>
    public void SaveSettingsPage() => AgentSessionSettingsChanged?.Invoke(this, EventArgs.Empty);
}

public sealed class AgentSessionServiceTests : IDisposable
{
    private readonly TestFolder _temp = new();
    private readonly FakeSessionSettings _settings = new();
    private readonly AgentSessionService _service;
    private int _changes;

    public AgentSessionServiceTests()
    {
        _service = new AgentSessionService(_settings, new RecentFolders(Path.Combine(_temp.Path, "recents.txt")));
        _service.Changed += (_, _) => _changes++;
    }

    public void Dispose() => _temp.Dispose();

    // ----- Favorites --------------------------------------------------------------------------

    [Fact]
    public void AddFavorite_AppendsOnce_AndAnnouncesTheChange()
    {
        var a = _temp.Create("a");
        var b = _temp.Create("b");

        Assert.True(_service.AddFavorite(a));
        Assert.True(_service.AddFavorite(b));
        Assert.False(_service.AddFavorite(a.ToUpperInvariant()));

        Assert.Equal([a, b], _service.Favorites);
        Assert.True(_service.IsFavorite(b));
        Assert.Equal(2, _changes);
        Assert.Equal([a, b], FavoriteFolderList.Parse(_settings.FavoriteFoldersText));
    }

    [Fact]
    public void RemoveFavorite_RemovesOnlyThatFolder()
    {
        var a = _temp.Create("a");
        var b = _temp.Create("b");
        _service.AddFavorite(a);
        _service.AddFavorite(b);

        Assert.True(_service.RemoveFavorite(a));
        Assert.False(_service.RemoveFavorite(a));
        Assert.Equal([b], _service.Favorites);
    }

    [Fact]
    public void MoveFavorite_ReordersWithinBounds()
    {
        var a = _temp.Create("a");
        var b = _temp.Create("b");
        var c = _temp.Create("c");
        _settings.FavoriteFoldersText = FavoriteFolderList.Format([a, b, c]);

        Assert.True(_service.MoveFavorite(c, -1));
        Assert.Equal([a, c, b], _service.Favorites);
        Assert.False(_service.MoveFavorite(a, -1));
        Assert.False(_service.MoveFavorite(b, +1));
        Assert.True(_service.MoveFavorite(a, +2));
        Assert.Equal([c, b, a], _service.Favorites);
    }

    // ----- Default folder ---------------------------------------------------------------------

    [Fact]
    public void DefaultFolder_IsTheUserFolderUntilSet()
    {
        Assert.Null(_service.ConfiguredDefaultFolder);
        Assert.Equal(FolderPaths.UserFolder, _service.DefaultFolder);

        var work = _temp.Create("work");
        _service.SetDefaultFolder(work);

        Assert.Equal(work, _service.DefaultFolder);
        Assert.Equal(1, _changes);
    }

    [Fact]
    public void DefaultFolder_ThatNoLongerExists_FallsBackToTheUserFolder()
    {
        _settings.DefaultSessionFolder = Path.Combine(_temp.Path, "unplugged");

        Assert.Equal(Path.Combine(_temp.Path, "unplugged"), _service.ConfiguredDefaultFolder);
        Assert.Equal(FolderPaths.UserFolder, _service.DefaultFolder);
    }

    [Fact]
    public void SetDefaultFolder_NullOrTheUserFolder_ClearsTheSetting()
    {
        _service.SetDefaultFolder(_temp.Create("work"));
        _service.SetDefaultFolder(FolderPaths.UserFolder);
        Assert.Equal("", _settings.DefaultSessionFolder);

        _service.SetDefaultFolder(_temp.Path);
        _service.SetDefaultFolder(null);
        Assert.Null(_service.ConfiguredDefaultFolder);
    }

    // ----- Agents -----------------------------------------------------------------------------

    [Fact]
    public void Agents_StartAsTheBuiltIns()
    {
        Assert.Equal(AgentCliList.BuiltIn, _service.Agents);
    }

    [Fact]
    public void SaveAgent_EditsInPlace_OrAppendsANewOne()
    {
        var claude = _service.Agents.Single(a => a.Command == "claude");
        var edited = claude with { Arguments = "--model opus" };

        _service.SaveAgent(claude, edited);
        _service.SaveAgent(null, new AgentCli("Aider", "aider", ""));

        Assert.Equal(["andrewcode", "claude --model opus", "agy", "muse", "codex", "aider"], _service.Agents.Select(a => a.CommandLine));
        Assert.Equal(2, _changes);
    }

    [Fact]
    public void RemoveAgent_ThenRestoreBuiltIns_BringsItBack()
    {
        var muse = _service.Agents.Single(a => a.Command == "muse");

        Assert.True(_service.RemoveAgent(muse));
        Assert.DoesNotContain(_service.Agents, a => a.Command == "muse");

        Assert.Equal(1, _service.RestoreBuiltInAgents());
        Assert.Contains(_service.Agents, a => a.Command == "muse");
        Assert.Equal(0, _service.RestoreBuiltInAgents());
    }

    [Fact]
    public void RestoreBuiltInAgents_KeepsCustomizedBuiltIns()
    {
        _settings.AgentsText = "My Claude | claude --resume";

        Assert.Equal(AgentCliList.BuiltIn.Count - 1, _service.RestoreBuiltInAgents());
        Assert.Equal("claude --resume", _service.Agents[0].CommandLine);
    }

    [Fact]
    public void SettingsPageSave_AnnouncesAChange()
    {
        _settings.SaveSettingsPage();

        Assert.Equal(1, _changes);
    }

    // ----- Search roots and index -------------------------------------------------------------

    [Fact]
    public void SearchRoots_AreTheDefaultFolderThenFavorites_WithoutDuplicates()
    {
        var work = _temp.Create("work");
        var other = _temp.Create("other");
        _service.SetDefaultFolder(work);
        _service.AddFavorite(other);
        _service.AddFavorite(work);

        Assert.Equal([work, other], _service.SearchRoots);
    }

    [Fact]
    public async Task GetFolderIndex_BuildsInTheBackground_AndAnnouncesIt()
    {
        var work = _temp.Create("work");
        var project = _temp.Create("work/project");
        _service.SetDefaultFolder(work);
        var updated = new TaskCompletionSource();
        _service.IndexUpdated += (_, _) => updated.TrySetResult();

        _service.GetFolderIndex(out var building);
        Assert.True(building);
        await updated.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await _service.WhenIndexIdle();

        var index = _service.GetFolderIndex(out building);
        Assert.False(building);
        Assert.Contains(index, e => e.Path == project);
    }
}
