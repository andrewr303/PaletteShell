using System.IO;
using System.Linq;
using PaletteShellExtension.Classes;
using Xunit;

namespace PaletteShellExtension.Tests;

public class FolderSearchTests
{
    // ----- FolderIndex ------------------------------------------------------------------------

    [Fact]
    public void Build_WalksBreadthFirstWithinDepth_AndSkipsNoise()
    {
        using var temp = new TestFolder();
        temp.Create("alpha/.git");
        temp.Create("alpha/src");
        temp.Create("beta/node_modules/pkg");
        temp.Create("gamma/one/two/three/four");
        temp.Create(".hidden/inside");

        var entries = FolderIndex.Build([temp.Path], new FolderIndexOptions(MaxDepth: 3));
        var relative = entries.Select(e => Path.GetRelativePath(temp.Path, e.Path).Replace('\\', '/')).ToList();

        Assert.Equal(".", relative[0]);
        Assert.Contains("alpha", relative);
        Assert.Contains("gamma/one/two", relative);
        Assert.DoesNotContain("gamma/one/two/three", relative);   // deeper than MaxDepth
        Assert.DoesNotContain("alpha/src", relative);             // repos are leaves
        Assert.DoesNotContain("beta/node_modules", relative);     // dependency output
        Assert.DoesNotContain(relative, r => r.StartsWith(".hidden", System.StringComparison.Ordinal));
        Assert.True(entries.Single(e => e.Path.EndsWith("alpha", System.StringComparison.Ordinal)).IsRepo);

        // Breadth-first: every depth-1 folder comes before any depth-2 folder.
        var depths = entries.Select(e => e.Depth).ToList();
        Assert.Equal(depths.OrderBy(d => d), depths);
    }

    [Fact]
    public void Build_SearchTextStartsAtTheRootName()
    {
        using var temp = new TestFolder();
        temp.Create("dev/PaletteShell");

        var entry = FolderIndex.Build([temp.Path], new FolderIndexOptions())
            .Single(e => e.Path.EndsWith("PaletteShell", System.StringComparison.Ordinal));

        Assert.Equal(Path.Combine(Path.GetFileName(temp.Path), "dev", "PaletteShell"), entry.SearchText);
        Assert.Equal(2, entry.Depth);
    }

    [Fact]
    public void Build_StopsAtMaxEntries_AndIgnoresMissingRoots()
    {
        using var temp = new TestFolder();
        for (var i = 0; i < 20; i++)
        {
            temp.Create($"f{i}");
        }

        var entries = FolderIndex.Build([Path.Combine(temp.Path, "missing"), temp.Path], new FolderIndexOptions(MaxEntries: 5));

        Assert.Equal(5, entries.Count);
    }

    // ----- Rank -------------------------------------------------------------------------------

    private static FolderCandidate Indexed(string path, int depth = 1, bool repo = false) =>
        new(path, FolderSource.Indexed, path, depth, repo);

    [Fact]
    public void Rank_FolderNameMatchBeatsParentMatch()
    {
        var sep = Path.DirectorySeparatorChar;
        var ranked = FolderSearch.Rank(
        [
            Indexed($"{sep}src{sep}shell-tools{sep}docs"),
            Indexed($"{sep}src{sep}shell", depth: 2),
        ], "shell", 10);

        Assert.Equal($"{sep}src{sep}shell", ranked[0].Path);
        Assert.Equal(2, ranked.Count);
    }

    [Fact]
    public void Rank_EveryTermMustMatch()
    {
        var sep = Path.DirectorySeparatorChar;
        var ranked = FolderSearch.Rank(
        [
            Indexed($"{sep}dev{sep}PaletteShell"),
            Indexed($"{sep}dev{sep}Other"),
        ], "dev palette", 10);

        Assert.Equal($"{sep}dev{sep}PaletteShell", Assert.Single(ranked).Path);
    }

    [Fact]
    public void Rank_FavoritesRecentsAndReposAreBoosted()
    {
        var sep = Path.DirectorySeparatorChar;
        var ranked = FolderSearch.Rank(
        [
            Indexed($"{sep}a{sep}app"),
            Indexed($"{sep}b{sep}app", repo: true),
            new($"{sep}c{sep}app", FolderSource.Recent, $"{sep}c{sep}app"),
            new($"{sep}d{sep}app", FolderSource.Favorite, $"{sep}d{sep}app"),
        ], "app", 10);

        Assert.Equal(["d", "c", "b", "a"], ranked.Select(c => c.Path.Split(sep)[1]));
    }

    [Fact]
    public void Rank_SamePathFromSeveralSources_AppearsOnceAsTheBestRanked()
    {
        var sep = Path.DirectorySeparatorChar;
        var path = $"{sep}src{sep}app";
        var ranked = FolderSearch.Rank(
        [
            Indexed(path),
            new(path.ToUpperInvariant(), FolderSource.Favorite, path),
        ], "app", 10);

        Assert.Equal(FolderSource.Favorite, Assert.Single(ranked).Source);
    }

    [Theory]
    [InlineData("PaletteShell", "shell")]
    [InlineData("palette-shell", "shell")]
    [InlineData("palette_shell", "shell")]
    public void Score_WordStartsInsideTheNameBeatPlainSubstrings(string name, string term)
    {
        var sep = Path.DirectorySeparatorChar;
        var wordStart = FolderSearch.Score(Indexed($"{sep}{name}", depth: 0), [term]);
        var substring = FolderSearch.Score(Indexed($"{sep}seashells", depth: 0), [term]);

        Assert.True(wordStart > substring);
    }

    [Fact]
    public void Rank_RespectsMax()
    {
        var sep = Path.DirectorySeparatorChar;
        var candidates = Enumerable.Range(0, 30).Select(i => Indexed($"{sep}app{i}"));

        Assert.Equal(7, FolderSearch.Rank(candidates, "app", 7).Count);
    }

    // ----- CompletePath -----------------------------------------------------------------------

    [Fact]
    public void CompletePath_PartialSegment_ListsMatchingSiblingsPrefixFirst()
    {
        using var temp = new TestFolder();
        temp.Create("my-app");
        temp.Create("app-two");
        temp.Create("other");

        var completions = FolderSearch.CompletePath(Path.Combine(temp.Path, "app"), 10);

        Assert.Equal(["app-two", "my-app"], completions.Select(Path.GetFileName));
    }

    [Fact]
    public void CompletePath_TrailingSeparator_ListsEveryChild()
    {
        using var temp = new TestFolder();
        temp.Create("b");
        temp.Create("a");

        var completions = FolderSearch.CompletePath(temp.Path + Path.DirectorySeparatorChar, 10);

        Assert.Equal(["a", "b"], completions.Select(Path.GetFileName));
    }

    [Fact]
    public void CompletePath_MissingParent_IsEmpty()
    {
        using var temp = new TestFolder();

        Assert.Empty(FolderSearch.CompletePath(Path.Combine(temp.Path, "nope", "x"), 10));
    }
}
