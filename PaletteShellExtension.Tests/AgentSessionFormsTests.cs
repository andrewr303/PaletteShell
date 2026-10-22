using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using PaletteShellExtension.Classes;
using PaletteShellExtension.Forms;
using Xunit;

namespace PaletteShellExtension.Tests;

public sealed class AgentSessionFormsTests : IDisposable
{
    private readonly TestFolder _temp = new();
    private readonly FakeSessionSettings _settings = new();
    private readonly AgentSessionService _service;

    public AgentSessionFormsTests()
    {
        _service = new AgentSessionService(_settings, new RecentFolders(Path.Combine(_temp.Path, "recents.txt")));
    }

    public void Dispose() => _temp.Dispose();

    private static string Inputs(params (string Id, string Value)[] values) =>
        JsonSerializer.Serialize(values.ToDictionary(v => v.Id, v => v.Value));

    [Fact]
    public void Templates_AreValidJson_EvenWithQuotesAndBackslashesInValues()
    {
        var weird = _temp.Create("quote\"and\\slash");
        _service.SetDefaultFolder(weird);
        var agent = new AgentCli("Name \"quoted\"", "C:/tools/a\\b.exe", "--x \"y\"");

        JsonDocument.Parse(new AgentCliForm(_service, agent).TemplateJson).Dispose();
        JsonDocument.Parse(new AgentCliForm(_service, null).TemplateJson).Dispose();
        JsonDocument.Parse(new FavoriteFolderForm(_service).TemplateJson).Dispose();
        JsonDocument.Parse(new DefaultSessionFolderForm(_service).TemplateJson).Dispose();
    }

    [Fact]
    public void AgentCliForm_Add_AppendsTheAgent()
    {
        new AgentCliForm(_service, null).SubmitForm(Inputs(("command", "aider"), ("arguments", "--model x"), ("name", "")), "{}");

        Assert.Equal(new AgentCli("aider", "aider", "--model x"), _service.Agents[^1]);
    }

    [Fact]
    public void AgentCliForm_Edit_ReplacesTheAgentInPlace()
    {
        var codex = _service.Agents.Single(a => a.Command == "codex");

        new AgentCliForm(_service, codex).SubmitForm(Inputs(("command", "codex"), ("arguments", "--full-auto"), ("name", "Codex")), "{}");

        Assert.Equal("codex --full-auto", _service.Agents.Single(a => a.Name == "Codex").CommandLine);
        Assert.Equal(AgentCliList.BuiltIn.Count, _service.Agents.Count);
    }

    [Fact]
    public void AgentCliForm_BlankCommand_ChangesNothing()
    {
        var before = _settings.AgentsText;

        new AgentCliForm(_service, null).SubmitForm(Inputs(("command", "  "), ("name", "Nothing")), "{}");

        Assert.Equal(before, _settings.AgentsText);
    }

    [Fact]
    public void FavoriteFolderForm_ExistingFolder_IsAdded()
    {
        var folder = _temp.Create("repo");

        new FavoriteFolderForm(_service).SubmitForm(Inputs(("folder", folder + Path.DirectorySeparatorChar)), "{}");

        Assert.Equal([folder], _service.Favorites);
    }

    [Fact]
    public void FavoriteFolderForm_MissingOrRelativeFolder_IsRejected()
    {
        new FavoriteFolderForm(_service).SubmitForm(Inputs(("folder", Path.Combine(_temp.Path, "missing"))), "{}");
        new FavoriteFolderForm(_service).SubmitForm(Inputs(("folder", "relative")), "{}");

        Assert.Empty(_service.Favorites);
    }

    [Fact]
    public void DefaultSessionFolderForm_SetsAndClearsTheDefault()
    {
        var folder = _temp.Create("work");

        new DefaultSessionFolderForm(_service).SubmitForm(Inputs(("folder", folder)), "{}");
        Assert.Equal(folder, _service.DefaultFolder);

        new DefaultSessionFolderForm(_service).SubmitForm(Inputs(("folder", Path.Combine(_temp.Path, "missing"))), "{}");
        Assert.Equal(folder, _service.DefaultFolder);

        new DefaultSessionFolderForm(_service).SubmitForm(Inputs(("folder", "")), "{}");
        Assert.Null(_service.ConfiguredDefaultFolder);
    }

    [Fact]
    public void AgentCliForm_EditingAnAgentWithASpacedPath_RoundTripsUnchanged()
    {
        var agent = new AgentCli("Tool", "C:/Program Files/Tool/tool.exe", "--x");
        _service.SaveAgent(null, agent);
        var template = JsonDocument.Parse(new AgentCliForm(_service, agent).TemplateJson);
        var shownCommand = template.RootElement.GetProperty("body").EnumerateArray()
            .Single(e => e.TryGetProperty("id", out var id) && id.GetString() == "command")
            .GetProperty("value").GetString()!;

        new AgentCliForm(_service, agent).SubmitForm(Inputs(("command", shownCommand), ("arguments", "--x"), ("name", "Tool")), "{}");

        Assert.Equal(agent, _service.Agents[^1]);
    }
}
