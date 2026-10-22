using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace PaletteShellExtension.Classes;

/// <summary>Where a folder candidate came from — decides its icon, section, and ranking boost.</summary>
internal enum FolderSource
{
    Default,
    Favorite,
    Recent,
    Indexed,
    Typed,
}

/// <summary>A folder found by <see cref="FolderIndex"/>.</summary>
/// <param name="Path">Full path.</param>
/// <param name="SearchText">The part that search matches against: the path from its root's own
/// name down (e.g. <c>dev\PaletteShell</c>), so every folder under <c>C:\Users\andrew</c>
/// doesn't match "andrew".</param>
/// <param name="Depth">Levels below the root it was found under (the root itself is 0).</param>
/// <param name="IsRepo">Contains a <c>.git</c> entry — likely a project, so it ranks higher.</param>
internal sealed record FolderEntry(string Path, string SearchText, int Depth, bool IsRepo);

/// <summary>A folder offered by the picker, before it becomes a list item.</summary>
internal sealed record FolderCandidate(string Path, FolderSource Source, string SearchText, int Depth = 0, bool IsRepo = false);

/// <summary>Limits for one <see cref="FolderIndex.Build"/> pass.</summary>
internal sealed record FolderIndexOptions(int MaxDepth = 3, int MaxEntries = 10_000, int TimeBudgetMs = 4_000);

/// <summary>
/// Builds a breadth-first list of the folders under the session search roots (the default folder
/// and favorites), so "search for a project" is an in-memory filter instead of a disk walk per
/// keystroke. Bounded by depth, entry count, and time; skips hidden/system folders, dot-folders,
/// and build/dependency output; and treats a git repository as a leaf — its subfolders are rarely
/// where you'd start an agent, and skipping them keeps the budget for actual projects.
/// </summary>
internal static class FolderIndex
{
    private static readonly HashSet<string> SkippedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules", "bin", "obj", "target", "dist", "build", "out", "packages", "__pycache__",
        "venv", "AppData", "$RECYCLE.BIN", "System Volume Information", "Windows",
        "Program Files", "Program Files (x86)", "ProgramData",
    };

    private static readonly EnumerationOptions ChildOptions = new()
    {
        AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
        IgnoreInaccessible = true,
        RecurseSubdirectories = false,
    };

    public static List<FolderEntry> Build(IEnumerable<string> roots, FolderIndexOptions options, CancellationToken cancellationToken = default)
    {
        var entries = new List<FolderEntry>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<(string Path, string SearchText, int Depth, bool Descend)>();

        foreach (var root in roots)
        {
            if (FolderPaths.Exists(root) && visited.Add(root))
            {
                queue.Enqueue((root, FolderPaths.DisplayName(root), 0, true));
            }
        }

        var clock = Stopwatch.StartNew();
        while (queue.Count > 0 && entries.Count < options.MaxEntries && clock.ElapsedMilliseconds < options.TimeBudgetMs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (dir, searchText, depth, descend) = queue.Dequeue();
            var isRepo = IsRepository(dir);
            entries.Add(new FolderEntry(dir, searchText, depth, isRepo));

            if (!descend || isRepo || depth >= options.MaxDepth)
            {
                continue;
            }

            try
            {
                foreach (var child in new DirectoryInfo(dir).EnumerateDirectories("*", ChildOptions))
                {
                    if (child.Name.StartsWith('.') || SkippedNames.Contains(child.Name) || !visited.Add(child.FullName))
                    {
                        continue;
                    }

                    // Symlinks and junctions are listed but not walked: they can loop back up
                    // the tree or point at a huge unrelated volume. (Cloud placeholders — OneDrive
                    // folders — are reparse points too, but have no link target, so they're walked.)
                    var isLink = child.Attributes.HasFlag(FileAttributes.ReparsePoint) && child.LinkTarget is not null;
                    queue.Enqueue((child.FullName, searchText + Path.DirectorySeparatorChar + child.Name, depth + 1, !isLink));
                }
            }
            catch (Exception)
            {
                // Access denied, vanished mid-walk, offline share: skip this branch.
            }
        }

        return entries;
    }

    private static bool IsRepository(string dir)
    {
        try
        {
            // A file, not a folder, for worktrees and submodules.
            return Path.Exists(Path.Combine(dir, ".git"));
        }
        catch (Exception)
        {
            return false;
        }
    }
}

/// <summary>Ranking and path completion for the session folder picker.</summary>
internal static class FolderSearch
{
    /// <summary>
    /// Filters and ranks candidates for <paramref name="query"/>. Every whitespace-separated term
    /// must appear in the candidate's search text; folders whose own name matches rank above
    /// folders that only match a parent, then favorites, recents, and git repos get a boost, and
    /// shallower folders win ties. A path offered by several sources (say, a favorite that the
    /// index also found) appears once, as whichever of them ranks highest.
    /// </summary>
    public static List<FolderCandidate> Rank(IEnumerable<FolderCandidate> candidates, string? query, int max)
    {
        var terms = (query ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var best = new Dictionary<string, (FolderCandidate Candidate, int Score)>(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in candidates)
        {
            var score = Score(candidate, terms);
            if (score is null)
            {
                continue;
            }

            if (!best.TryGetValue(candidate.Path, out var existing)
                || score > existing.Score
                || (score == existing.Score && candidate.Source < existing.Candidate.Source))
            {
                best[candidate.Path] = (candidate, score.Value);
            }
        }

        return [.. best.Values
            .OrderByDescending(b => b.Score)
            .ThenBy(b => b.Candidate.Path.Length)
            .ThenBy(b => b.Candidate.Path, StringComparer.OrdinalIgnoreCase)
            .Take(max)
            .Select(b => b.Candidate)];
    }

    /// <summary>Null when the candidate doesn't match every term; otherwise its rank score.</summary>
    internal static int? Score(FolderCandidate candidate, IReadOnlyList<string> terms)
    {
        var name = FolderPaths.DisplayName(candidate.Path);
        var score = 0;

        foreach (var term in terms)
        {
            if (!candidate.SearchText.Contains(term, StringComparison.OrdinalIgnoreCase)
                && !name.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            score += name.Equals(term, StringComparison.OrdinalIgnoreCase) ? 1000
                : name.StartsWith(term, StringComparison.OrdinalIgnoreCase) ? 800
                : StartsWordIn(name, term) ? 700
                : name.Contains(term, StringComparison.OrdinalIgnoreCase) ? 600
                : 200; // Only a parent folder matched.
        }

        score += candidate.Source switch
        {
            FolderSource.Favorite => 150,
            FolderSource.Recent => 100,
            FolderSource.Default => 50,
            _ => 0,
        };

        if (candidate.IsRepo)
        {
            score += 75;
        }

        return score - (candidate.Depth * 10);
    }

    // "shell" starts a word in "palette-shell", "palette_shell", "Palette Shell", "PaletteShell".
    private static bool StartsWordIn(string name, string term)
    {
        for (var i = 1; i < name.Length; i++)
        {
            var boundary = name[i - 1] is '-' or '_' or '.' or ' '
                || (char.IsUpper(name[i]) && char.IsLower(name[i - 1]));
            if (boundary && string.Compare(name, i, term, 0, term.Length, StringComparison.OrdinalIgnoreCase) == 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Completes a typed path: the subfolders of its parent whose names contain the last segment
    /// (names that start with it first), or every subfolder when the path ends in a separator.
    /// Returns nothing when the parent doesn't exist.
    /// </summary>
    public static List<string> CompletePath(string typedPath, int max)
    {
        var expanded = FolderPaths.Expand(typedPath);
        string? parent;
        string prefix;

        if (expanded.EndsWith('\\') || expanded.EndsWith('/') || (expanded.Length == 2 && expanded[1] == ':'))
        {
            parent = FolderPaths.TryNormalize(expanded);
            prefix = "";
        }
        else
        {
            parent = FolderPaths.TryNormalize(Path.GetDirectoryName(expanded));
            prefix = Path.GetFileName(expanded);
        }

        if (parent is null || !FolderPaths.Exists(parent))
        {
            return [];
        }

        try
        {
            return [.. new DirectoryInfo(parent)
                .EnumerateDirectories("*", new EnumerationOptions
                {
                    AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
                    IgnoreInaccessible = true,
                })
                .Where(d => d.Name.Contains(prefix, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(d => d.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                .Take(max)
                .Select(d => d.FullName)];
        }
        catch (Exception)
        {
            return [];
        }
    }
}
