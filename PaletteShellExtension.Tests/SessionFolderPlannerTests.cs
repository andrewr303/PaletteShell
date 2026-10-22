using System;
using System.IO;
using System.Linq;
using PaletteShellExtension.Classes;
using Xunit;

namespace PaletteShellExtension.Tests;

public sealed class SessionFolderPlannerTests : IDisposable
{
    private readonly TestFolder _temp = new();
    private readonly AgentSessionService _service;

    public SessionFolderPlannerTests()
    {
        _service = new AgentSessionService(new FakeSessionSettings(), new RecentFolders(Path.Combine(_temp.Path, "recents.txt")));
    }

    public void Dispose() => _temp.Dispose();

    private PickerRow[] Plan(string query) => [.. SessionFolderPlanner.Plan(_service, _service.DefaultFolder, query).Rows];

    [Fact]
    public void PathQuery_ListsCompletionsBeforeTheCreateFolderOffer()
    {
        var palette = _temp.Create("palette");
        _temp.Create("other");
        var typed = Path.Combine(_temp.Path, "pal");

        var rows = Plan(typed);

        Assert.Equal([(PickerRowKind.Folder, palette), (PickerRowKind.CreateFolder, typed)], rows.Select(r => (r.Kind, r.Path)));
        Assert.Equal(palette + Path.DirectorySeparatorChar, rows[0].SuggestText);
    }

    [Fact]
    public void PathQuery_ExistingFolder_IsFirstAndNeverOfferedForCreation()
    {
        var palette = _temp.Create("palette");
        var two = _temp.Create("palette-two");

        var rows = Plan(palette);

        Assert.Equal([palette, two], rows.Select(r => r.Path));
        Assert.All(rows, r => Assert.Equal(PickerRowKind.Folder, r.Kind));
    }

    [Fact]
    public void PathQuery_TrailingSeparator_ListsChildrenWithoutACreateOffer()
    {
        var a = _temp.Create("a");

        var rows = Plan(_temp.Path + Path.DirectorySeparatorChar);

        Assert.Contains(rows, r => r.Path == a);
        Assert.DoesNotContain(rows, r => r.Kind == PickerRowKind.CreateFolder);
    }

    [Fact]
    public void PathQuery_MissingParent_OffersNothing()
    {
        Assert.Empty(Plan(Path.Combine(_temp.Path, "missing", "deeper")));
    }

    [Fact]
    public void PathQuery_RootFolder_SuggestsItWithoutADoubledSeparator()
    {
        var root = Path.GetPathRoot(_temp.Path)!;

        Assert.Equal(root, Plan(root)[0].SuggestText);
    }

    [Fact]
    public void EmptyQuery_DefaultFolderThenFavoritesThenOtherRecents()
    {
        var work = _temp.Create("work");
        var fav = _temp.Create("fav");
        var recent = _temp.Create("recent");
        _service.SetDefaultFolder(work);
        _service.AddFavorite(fav);
        _service.Recents.Record(fav);
        _service.Recents.Record(recent);

        var rows = Plan("");

        Assert.Equal(
            [(work, FolderSource.Default, SessionFolderPlanner.DefaultSection), (fav, FolderSource.Favorite, SessionFolderPlanner.FavoritesSection), (recent, FolderSource.Recent, SessionFolderPlanner.RecentSection)],
            rows.Select(r => (r.Path, r.Source, r.Section)));
        Assert.NotNull(rows[2].LastUsedUtc);
    }

    [Fact]
    public void EmptyQuery_NoFavorites_PromptsToAddOne()
    {
        var rows = Plan("");

        Assert.Equal(PickerRowKind.AddFavorite, rows[1].Kind);
        Assert.Equal(SessionFolderPlanner.FavoritesSection, rows[1].Section);
    }

    [Fact]
    public void NameQuery_RanksFavoritesAndDoesNotSuggest()
    {
        var shell = _temp.Create("PaletteShell");
        _service.AddFavorite(shell);

        var rows = Plan("shell");

        var row = Assert.Single(rows, r => r.Path == shell);
        Assert.Equal(FolderSource.Favorite, row.Source);
        Assert.Null(row.SuggestText);
    }
}
