using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using PaletteShellExtension.Classes;
using PaletteShellExtension.Commands;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PaletteShellExtension.Pages;

/// <summary>
/// Picks the folder to start one agent in. With an empty search box it lists the default folder,
/// favorites, and recent folders; typing searches those plus an index of the folders under them
/// (see <see cref="FolderIndex"/>); typing a path (<c>C:\src\</c>, <c>~\code\new-app</c>) lists
/// matching subfolders and, for a folder that doesn't exist yet, offers to create it. Enter
/// starts the agent; the context menu starts another agent there or manages the folder.
/// <para>One instance per agent (see <see cref="For"/>), shared by the Agent sessions page and
/// the agent's top-level command, so both show the same live state.</para>
/// </summary>
internal sealed partial class SessionFolderPickerPage : DynamicListPage
{
    private static readonly ConcurrentDictionary<string, SessionFolderPickerPage> Pages = new();

    private const int MaxSearchResults = 50;
    private const int MaxPathCompletions = 40;

    // Short: search is in-memory except for path completion, which lists one directory.
    private const int DebounceMs = 120;

    private readonly AgentSessionService _service;
    private readonly AgentCli _agent;
    private IListItem[] _items = [];
    private bool _started;

    // Cancelled and replaced on each keystroke so a slow path listing can't overwrite the
    // results for newer text (same pattern as ScriptListPage).
    private CancellationTokenSource? _refreshCts;

    private SessionFolderPickerPage(AgentSessionService service, AgentCli agent)
    {
        _service = service;
        _agent = agent;
        Title = $"Start {agent.Name} in…";
        Name = "Choose folder";
        Icon = SessionIcons.Agent;
        Id = $"AgentSession.{StableId(agent.Key)}";
        PlaceholderText = "Search favorites, recent and project folders, or type a path";
        ShowDetails = false;
        EmptyContent = new CommandItem(new NoOpCommand())
        {
            Title = "No matching folders",
            Subtitle = @"Type a full path such as C:\src\app, or add favorites to search inside them",
            Icon = SessionIcons.Folder,
        };

        service.Changed += OnStateChanged;
        service.IndexUpdated += OnIndexUpdated;
    }

    /// <summary>The picker for <paramref name="agent"/>, created on first use.</summary>
    public static SessionFolderPickerPage For(AgentSessionService service, AgentCli agent) =>
        Pages.GetOrAdd(agent.Key, _ => new SessionFolderPickerPage(service, agent));

    /// <summary>Drops pickers for agents that were removed or edited, unhooking their events.</summary>
    public static void Prune(IEnumerable<AgentCli> current)
    {
        var keep = current.Select(a => a.Key).ToHashSet();
        foreach (var key in Pages.Keys.Where(k => !keep.Contains(k)))
        {
            if (Pages.TryRemove(key, out var page))
            {
                page._service.Changed -= page.OnStateChanged;
                page._service.IndexUpdated -= page.OnIndexUpdated;
            }
        }
    }

    public override IListItem[] GetItems()
    {
        if (!_started)
        {
            _started = true;
            _items = BuildItems(SearchText);

            // Warm the index on first open so the first search doesn't start from nothing.
            _service.GetFolderIndex(out _);
        }

        return _items;
    }

    public override void UpdateSearchText(string oldSearch, string newSearch) => Refresh(newSearch, DebounceMs);

    private void OnStateChanged(object? sender, EventArgs e)
    {
        if (_started)
        {
            Refresh(SearchText, 0);
        }
    }

    private void OnIndexUpdated(object? sender, EventArgs e)
    {
        // Only a name search reads the index; the home list and path completion don't.
        if (_started && !string.IsNullOrWhiteSpace(SearchText) && !FolderPaths.LooksLikePath(SearchText))
        {
            Refresh(SearchText, 0);
        }
    }

    private void Refresh(string? query, int delayMs)
    {
        var cts = new CancellationTokenSource();
        Interlocked.Exchange(ref _refreshCts, cts)?.Cancel();

        _ = Task.Run(async () =>
        {
            try
            {
                if (delayMs > 0)
                {
                    await Task.Delay(delayMs, cts.Token);
                }

                var items = BuildItems(query);
                if (!cts.IsCancellationRequested)
                {
                    _items = items;
                    RaiseItemsChanged();
                }
            }
            catch (OperationCanceledException)
            {
                // Superseded by newer text.
            }
            catch (Exception ex)
            {
                // Never let a background failure crash the host.
                Log.Warn($"Folder picker refresh failed: {ex.Message}");
            }
        });
    }

    private IListItem[] BuildItems(string? query)
    {
        var text = query?.Trim() ?? "";
        if (text.Length == 0)
        {
            IsLoading = false;
            return BuildHome();
        }

        if (FolderPaths.LooksLikePath(text))
        {
            IsLoading = false;
            return BuildPathResults(text);
        }

        return BuildSearchResults(text);
    }

    // Default folder, then favorites, then recent folders not already listed.
    private IListItem[] BuildHome()
    {
        var items = new List<IListItem>();
        var defaultFolder = _service.DefaultFolder;
        items.Add(FolderItem(new FolderCandidate(defaultFolder, FolderSource.Default, defaultFolder), "Default folder"));

        var favorites = _service.Favorites;
        foreach (var favorite in favorites)
        {
            items.Add(FolderItem(new FolderCandidate(favorite, FolderSource.Favorite, favorite), "Favorites"));
        }

        if (favorites.Count == 0)
        {
            items.Add(new ListItem(FavoriteFoldersPage.AddFavoritePage(_service))
            {
                Title = "Add a favorite folder",
                Subtitle = "Favorites are listed here and searched for project folders",
                Icon = SessionIcons.Add,
                Section = "Favorites",
            });
        }

        var now = DateTime.UtcNow;
        foreach (var recent in _service.Recents.Items)
        {
            if (FolderPaths.Same(recent.Path, defaultFolder) || favorites.Any(f => FolderPaths.Same(f, recent.Path)))
            {
                continue;
            }

            var item = FolderItem(new FolderCandidate(recent.Path, FolderSource.Recent, recent.Path), "Recent");
            item.Tags = [new Tag(SessionFolderItems.RelativeTime(recent.LastUsedUtc, now))];
            items.Add(item);
        }

        return [.. items];
    }

    // A typed path: the folder itself (or an offer to create it), then completions of its last
    // segment. TextToSuggest lets the user accept a completion and keep typing into it.
    private IListItem[] BuildPathResults(string text)
    {
        var items = new List<IListItem>();
        var target = FolderPaths.TryNormalize(text);
        var endsWithSeparator = text.EndsWith('\\') || text.EndsWith('/');

        if (target is not null && FolderPaths.Exists(target))
        {
            items.Add(FolderItem(new FolderCandidate(target, FolderSource.Typed, target), suggest: true));
        }
        else if (target is not null && !endsWithSeparator && Path.GetDirectoryName(target) is { } parent && FolderPaths.Exists(parent))
        {
            items.Add(new ListItem(new LaunchAgentSessionCommand(_service, _agent, target, createFolder: true))
            {
                Title = $"New folder: {FolderPaths.DisplayName(target)}",
                Subtitle = $"Create {target} and start {_agent.Name} there",
                Icon = SessionIcons.NewFolder,
            });
        }

        foreach (var child in FolderSearch.CompletePath(text, MaxPathCompletions))
        {
            if (!FolderPaths.Same(child, target))
            {
                items.Add(FolderItem(new FolderCandidate(child, FolderSource.Typed, child), suggest: true));
            }
        }

        return [.. items];
    }

    private IListItem[] BuildSearchResults(string text)
    {
        var index = _service.GetFolderIndex(out var building);
        IsLoading = building;

        var defaultFolder = _service.DefaultFolder;
        var candidates = new List<FolderCandidate> { new(defaultFolder, FolderSource.Default, defaultFolder) };
        candidates.AddRange(_service.Favorites.Select(f => new FolderCandidate(f, FolderSource.Favorite, f)));
        candidates.AddRange(_service.Recents.Items.Select(r => new FolderCandidate(r.Path, FolderSource.Recent, r.Path)));
        candidates.AddRange(index.Select(e => new FolderCandidate(e.Path, FolderSource.Indexed, e.SearchText, e.Depth, e.IsRepo)));

        return [.. FolderSearch.Rank(candidates, text, MaxSearchResults).Select(c => FolderItem(c))];
    }

    private ListItem FolderItem(FolderCandidate candidate, string? section = null, bool suggest = false)
    {
        var exists = candidate.Source is FolderSource.Indexed or FolderSource.Typed || FolderPaths.Exists(candidate.Path);
        var item = new ListItem(new LaunchAgentSessionCommand(_service, _agent, candidate.Path))
        {
            Title = SessionFolderItems.Title(candidate.Path),
            Subtitle = exists ? candidate.Path : $"⚠ Not found — {candidate.Path}",
            Icon = !exists ? SessionIcons.Warning : candidate.Source switch
            {
                FolderSource.Default => SessionIcons.Home,
                FolderSource.Favorite => SessionIcons.FavoriteFilled,
                FolderSource.Recent => SessionIcons.Recent,
                _ when candidate.IsRepo => SessionIcons.Repo,
                _ => SessionIcons.Folder,
            },
            MoreCommands = SessionFolderItems.FolderCommands(_service, candidate.Path, _agent),
        };

        if (section is not null)
        {
            item.Section = section;
        }

        if (suggest)
        {
            item.TextToSuggest = candidate.Path + Path.DirectorySeparatorChar;
        }

        return item;
    }

    // Deterministic across runs (string.GetHashCode isn't), so the host's recent-command
    // history for a top-level agent survives restarts; keyed on the whole agent so two agents
    // that differ only in arguments still get distinct ids. FNV-1a, 32-bit.
    private static string StableId(string text)
    {
        var hash = 2166136261u;
        foreach (var c in text.ToUpperInvariant())
        {
            hash = (hash ^ c) * 16777619u;
        }

        return hash.ToString("x8", System.Globalization.CultureInfo.InvariantCulture);
    }
}
