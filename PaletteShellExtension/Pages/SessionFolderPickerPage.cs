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
            (_items, IsLoading) = BuildItems(SearchText);

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

                var (items, loading) = BuildItems(query);

                // Items and the spinner are applied together, and only for the newest text, so a
                // superseded search can't leave stale rows or a stuck spinner behind.
                if (!cts.IsCancellationRequested)
                {
                    _items = items;
                    IsLoading = loading;
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

    // Plans the rows (see SessionFolderPlanner for the rules) and renders them as list items.
    private (IListItem[] Items, bool Loading) BuildItems(string? query)
    {
        // Read once per build: it may probe a network share, and every row's menu needs it.
        var defaultFolder = _service.DefaultFolder;
        var (rows, loading) = SessionFolderPlanner.Plan(_service, defaultFolder, query);
        var now = DateTime.UtcNow;
        return ([.. rows.Select(row => ToListItem(row, defaultFolder, now))], loading);
    }

    private IListItem ToListItem(PickerRow row, string defaultFolder, DateTime now)
    {
        switch (row.Kind)
        {
            case PickerRowKind.AddFavorite:
                return new ListItem(FavoriteFoldersPage.AddFavoritePage(_service))
                {
                    Title = "Add a favorite folder",
                    Subtitle = "Favorites are listed here and searched for project folders",
                    Icon = SessionIcons.Add,
                    Section = row.Section ?? "",
                };

            case PickerRowKind.CreateFolder:
                return new ListItem(new LaunchAgentSessionCommand(_service, _agent, row.Path, createFolder: true))
                {
                    Title = $"New folder: {FolderPaths.DisplayName(row.Path)}",
                    Subtitle = $"Create {row.Path} and start {_agent.Name} there",
                    Icon = SessionIcons.NewFolder,
                };
        }

        // Indexed and typed folders were just seen on disk; only stored ones can have gone missing.
        var missing = row.Source is not (FolderSource.Indexed or FolderSource.Typed) && FolderPaths.LooksMissing(row.Path);
        var item = new ListItem(new LaunchAgentSessionCommand(_service, _agent, row.Path))
        {
            Title = SessionFolderItems.Title(row.Path),
            Subtitle = missing ? $"⚠ Not found — {row.Path}" : row.Path,
            Icon = missing ? SessionIcons.Warning : row.Source switch
            {
                FolderSource.Default => SessionIcons.Home,
                FolderSource.Favorite => SessionIcons.FavoriteFilled,
                FolderSource.Recent => SessionIcons.Recent,
                _ when row.IsRepo => SessionIcons.Repo,
                _ => SessionIcons.Folder,
            },
            MoreCommands = SessionFolderItems.FolderCommands(_service, row.Path, _agent, defaultFolder),
        };

        if (row.Section is not null)
        {
            item.Section = row.Section;
        }

        if (row.SuggestText is not null)
        {
            item.TextToSuggest = row.SuggestText;
        }

        if (row.LastUsedUtc is { } lastUsed)
        {
            item.Tags = [new Tag(SessionFolderItems.RelativeTime(lastUsed, now))];
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
