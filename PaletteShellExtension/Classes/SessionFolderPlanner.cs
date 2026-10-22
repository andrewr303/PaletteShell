using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PaletteShellExtension.Classes;

/// <summary>What a folder-picker row does.</summary>
internal enum PickerRowKind
{
    /// <summary>Start the agent in <see cref="PickerRow.Path"/>.</summary>
    Folder,

    /// <summary>Create <see cref="PickerRow.Path"/>, then start the agent there.</summary>
    CreateFolder,

    /// <summary>Open the add-favorite form (shown when there are no favorites yet).</summary>
    AddFavorite,
}

/// <summary>One row of the session folder picker, before it becomes a list item.</summary>
/// <param name="SuggestText">Text the palette can offer to complete the search box with.</param>
/// <param name="LastUsedUtc">For recent folders, when a session last started there.</param>
internal sealed record PickerRow(
    PickerRowKind Kind,
    string Path,
    FolderSource Source = FolderSource.Typed,
    string? Section = null,
    bool IsRepo = false,
    string? SuggestText = null,
    DateTime? LastUsedUtc = null);

/// <summary>
/// Decides what the session folder picker shows for a search text — kept apart from the page
/// so the rules are testable without Command Palette's list-item types:
/// <list type="bullet">
/// <item>Empty: the default folder, favorites (or an add-favorite prompt), then recent folders.</item>
/// <item>A path (<c>C:\src\</c>, <c>~\code\app</c>): that folder, then completions of its last
/// segment, then — only if it doesn't exist — an offer to create it. Creating comes last so
/// Enter on a half-typed name picks the matching folder instead of making a new one.</item>
/// <item>Anything else: a ranked search of favorites, recents, and the folder index.</item>
/// </list>
/// </summary>
internal static class SessionFolderPlanner
{
    public const int MaxSearchResults = 50;
    public const int MaxPathCompletions = 40;

    public const string DefaultSection = "Default folder";
    public const string FavoritesSection = "Favorites";
    public const string RecentSection = "Recent";

    /// <summary>The rows for <paramref name="query"/>, and whether the folder index is still being
    /// built (so search results may grow).</summary>
    public static (List<PickerRow> Rows, bool Loading) Plan(AgentSessionService service, string defaultFolder, string? query)
    {
        var text = query?.Trim() ?? "";
        if (text.Length == 0)
        {
            return (Home(service, defaultFolder), false);
        }

        if (FolderPaths.LooksLikePath(text))
        {
            return (PathResults(text), false);
        }

        var index = service.GetFolderIndex(out var building);
        return (SearchResults(service, defaultFolder, text, index), building);
    }

    private static List<PickerRow> Home(AgentSessionService service, string defaultFolder)
    {
        var rows = new List<PickerRow> { new(PickerRowKind.Folder, defaultFolder, FolderSource.Default, DefaultSection) };

        var favorites = service.Favorites;
        rows.AddRange(favorites.Select(f => new PickerRow(PickerRowKind.Folder, f, FolderSource.Favorite, FavoritesSection)));
        if (favorites.Count == 0)
        {
            rows.Add(new PickerRow(PickerRowKind.AddFavorite, "", Section: FavoritesSection));
        }

        foreach (var recent in service.Recents.Items)
        {
            if (!FolderPaths.Same(recent.Path, defaultFolder) && !favorites.Any(f => FolderPaths.Same(f, recent.Path)))
            {
                rows.Add(new PickerRow(PickerRowKind.Folder, recent.Path, FolderSource.Recent, RecentSection, LastUsedUtc: recent.LastUsedUtc));
            }
        }

        return rows;
    }

    private static List<PickerRow> PathResults(string text)
    {
        var rows = new List<PickerRow>();
        var target = FolderPaths.TryNormalize(text);

        // Unknown (a share that's slow to answer) counts as existing: offer it, never "create" it.
        var targetExists = target is not null && FolderPaths.ProbeExists(target, 1000) != false;
        if (targetExists)
        {
            rows.Add(new PickerRow(PickerRowKind.Folder, target!, SuggestText: SuggestionFor(target!)));
        }

        foreach (var child in FolderSearch.CompletePath(text, MaxPathCompletions))
        {
            if (!FolderPaths.Same(child, target))
            {
                rows.Add(new PickerRow(PickerRowKind.Folder, child, SuggestText: SuggestionFor(child)));
            }
        }

        var endsWithSeparator = text.EndsWith('\\') || text.EndsWith('/');
        if (!targetExists
            && target is not null
            && !endsWithSeparator
            && Path.GetDirectoryName(target) is { } parent
            && FolderPaths.ProbeExists(parent, 1000) == true)
        {
            rows.Add(new PickerRow(PickerRowKind.CreateFolder, target));
        }

        return rows;
    }

    private static List<PickerRow> SearchResults(AgentSessionService service, string defaultFolder, string text, IReadOnlyList<FolderEntry> index)
    {
        var candidates = new List<FolderCandidate> { new(defaultFolder, FolderSource.Default, defaultFolder) };
        candidates.AddRange(service.Favorites.Select(f => new FolderCandidate(f, FolderSource.Favorite, f)));
        candidates.AddRange(service.Recents.Items.Select(r => new FolderCandidate(r.Path, FolderSource.Recent, r.Path)));
        candidates.AddRange(index.Select(e => new FolderCandidate(e.Path, FolderSource.Indexed, e.SearchText, e.Depth, e.IsRepo)));

        return [.. FolderSearch.Rank(candidates, text, MaxSearchResults)
            .Select(c => new PickerRow(PickerRowKind.Folder, c.Path, c.Source, IsRepo: c.IsRepo))];
    }

    // The folder plus a trailing separator, so accepting the suggestion lists its subfolders.
    // A root such as C:\ already ends in one.
    private static string SuggestionFor(string folder) =>
        Path.EndsInDirectorySeparator(folder) ? folder : folder + Path.DirectorySeparatorChar;
}
