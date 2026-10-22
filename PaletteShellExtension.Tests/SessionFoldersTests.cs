using System;
using System.IO;
using System.Linq;
using PaletteShellExtension.Classes;
using Xunit;

namespace PaletteShellExtension.Tests;

public class SessionFoldersTests
{
    // ----- FolderPaths ------------------------------------------------------------------------

    [Theory]
    [InlineData(@"C:\src", true)]
    [InlineData("d:", true)]
    [InlineData("~", true)]
    [InlineData(@"~\code", true)]
    [InlineData(@"\\server\share", true)]
    [InlineData("%USERPROFILE%", true)]
    [InlineData("claude", false)]
    [InlineData("my project", false)]
    [InlineData("", false)]
    public void LooksLikePath_DistinguishesPathsFromSearchTerms(string text, bool expected)
    {
        Assert.Equal(expected, FolderPaths.LooksLikePath(text));
    }

    [Fact]
    public void TryNormalize_ExpandsHomeAndTrimsTrailingSeparator()
    {
        var home = FolderPaths.UserFolder;

        Assert.Equal(Path.TrimEndingDirectorySeparator(home), FolderPaths.TryNormalize("~"));
        Assert.Equal(Path.Combine(home, "code"), FolderPaths.TryNormalize("~/code/"));
    }

    [Fact]
    public void TryNormalize_ExpandsEnvironmentVariables()
    {
        using var temp = new TestFolder();
        var name = $"PS_SESSION_TEST_{Guid.NewGuid():N}";
        Environment.SetEnvironmentVariable(name, temp.Path);
        try
        {
            Assert.Equal(Path.Combine(temp.Path, "app"), FolderPaths.TryNormalize($"%{name}%{Path.DirectorySeparatorChar}app"));
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, null);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    [InlineData("relative/folder")]
    public void TryNormalize_BlankOrRelative_IsNull(string? text)
    {
        Assert.Null(FolderPaths.TryNormalize(text));
    }

    [Fact]
    public void DisplayName_IsTheFolderNameOrTheRootItself()
    {
        using var temp = new TestFolder();
        var root = Path.GetPathRoot(temp.Path)!;

        Assert.Equal("proj", FolderPaths.DisplayName(Path.Combine(temp.Path, "proj")));
        Assert.Equal(root, FolderPaths.DisplayName(root));
    }

    // ----- FavoriteFolderList -----------------------------------------------------------------

    [Fact]
    public void FavoriteFolderList_Parse_NormalizesDedupesAndSkipsJunk()
    {
        using var temp = new TestFolder();
        var a = Path.Combine(temp.Path, "a");
        var b = Path.Combine(temp.Path, "b");
        var text = $"{a}{Path.DirectorySeparatorChar}\r\n# comment\r{b}\n\nnot/absolute\n{a.ToUpperInvariant()}";

        var folders = FavoriteFolderList.Parse(text);

        // On case-sensitive file systems the upper-cased copy is technically distinct, but
        // favorites compare case-insensitively like Windows paths do.
        Assert.Equal([a, b], folders);
    }

    [Fact]
    public void FavoriteFolderList_FormatRoundTrips()
    {
        using var temp = new TestFolder();
        string[] folders = [Path.Combine(temp.Path, "one"), Path.Combine(temp.Path, "two words")];

        Assert.Equal(folders, FavoriteFolderList.Parse(FavoriteFolderList.Format(folders)));
    }

    // ----- RecentFolders ----------------------------------------------------------------------

    [Fact]
    public void RecentFolders_Record_MovesToFrontAndPersists()
    {
        using var temp = new TestFolder();
        var store = Path.Combine(temp.Path, "recents.txt");
        var recents = new RecentFolders(store);
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        recents.Record(Path.Combine(temp.Path, "a"), start);
        recents.Record(Path.Combine(temp.Path, "b"), start.AddHours(1));
        recents.Record(Path.Combine(temp.Path, "A"), start.AddHours(2)); // same folder, different case

        var reloaded = new RecentFolders(store).Items;
        Assert.Equal(["A", "b"], reloaded.Select(r => Path.GetFileName(r.Path)));
        Assert.Equal(start.AddHours(2), reloaded[0].LastUsedUtc);
    }

    [Fact]
    public void RecentFolders_KeepsOnlyTheNewestCapacityEntries()
    {
        using var temp = new TestFolder();
        var recents = new RecentFolders(Path.Combine(temp.Path, "recents.txt"));

        for (var i = 0; i < RecentFolders.Capacity + 5; i++)
        {
            recents.Record(Path.Combine(temp.Path, $"f{i}"));
        }

        Assert.Equal(RecentFolders.Capacity, recents.Items.Count);
        Assert.Equal($"f{RecentFolders.Capacity + 4}", Path.GetFileName(recents.Items[0].Path));
    }

    [Fact]
    public void RecentFolders_Remove_ForgetsTheFolder()
    {
        using var temp = new TestFolder();
        var store = Path.Combine(temp.Path, "recents.txt");
        var recents = new RecentFolders(store);
        var folder = Path.Combine(temp.Path, "a");
        recents.Record(folder);

        Assert.True(recents.Remove(folder));
        Assert.False(recents.Remove(folder));
        Assert.Empty(new RecentFolders(store).Items);
    }

    [Fact]
    public void RecentFolders_CorruptLines_AreSkipped()
    {
        using var temp = new TestFolder();
        var store = Path.Combine(temp.Path, "recents.txt");
        var good = Path.Combine(temp.Path, "good");
        File.WriteAllLines(store, ["garbage", "not-a-date\t" + good, "2026-01-01T00:00:00.0000000Z\t" + good, "2026-01-01T00:00:00Z\trelative"]);

        var item = Assert.Single(new RecentFolders(store).Items);
        Assert.Equal(good, item.Path);
    }
}
