using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PaletteShellExtension.Classes;

/// <summary>
/// The Settings-backed values Agent sessions reads and edits. Implemented by
/// <see cref="PaletteShellSettingsManager"/>; tests substitute an in-memory copy.
/// </summary>
internal interface IAgentSessionSettings
{
    /// <summary>Agent CLIs, one <c>Name | command args</c> per line (see <see cref="AgentCliList"/>).</summary>
    string AgentsText { get; set; }

    /// <summary>Favorite folders, one path per line (see <see cref="FavoriteFolderList"/>).</summary>
    string FavoriteFoldersText { get; set; }

    /// <summary>The configured default session folder; blank means the user folder.</summary>
    string? DefaultSessionFolder { get; set; }

    AgentLaunchSettings LaunchSettings { get; }

    bool ShowAgentsInRoot { get; }

    /// <summary>Raised when the user saves the Settings page (not for programmatic sets).</summary>
    event EventHandler? AgentSessionSettingsChanged;
}

/// <summary>
/// Agent sessions' state and operations: the agent list, favorite and default folders (stored in
/// Settings so they're editable there too), recent folders, the background folder index used by
/// search, and launching. Pages subscribe to <see cref="Changed"/> and rebuild from here rather
/// than caching their own copies, so an edit made anywhere — a palette command or the Settings
/// page — shows up everywhere.
/// </summary>
internal sealed class AgentSessionService
{
    private static readonly Lazy<AgentSessionService> LazyInstance = new(() =>
        new AgentSessionService(PaletteShellSettingsManager.Instance, new RecentFolders(RecentFolders.DefaultStorePath())));

    /// <summary>A rebuilt index older than this is refreshed on next use, so folders created
    /// since are found without the user having to do anything.</summary>
    private static readonly TimeSpan IndexMaxAge = TimeSpan.FromMinutes(3);

    private readonly IAgentSessionSettings _settings;
    private readonly FolderIndexOptions _indexOptions;
    private readonly object _gate = new();

    private IReadOnlyList<FolderEntry> _index = [];
    private string? _indexRootsKey;
    private DateTime _indexBuiltUtc;
    private Task? _indexBuild;

    public AgentSessionService(IAgentSessionSettings settings, RecentFolders recents, FolderIndexOptions? indexOptions = null)
    {
        _settings = settings;
        _indexOptions = indexOptions ?? new FolderIndexOptions();
        Recents = recents;
        settings.AgentSessionSettingsChanged += (_, _) => RaiseChanged();
    }

    public static AgentSessionService Instance => LazyInstance.Value;

    /// <summary>Agents, favorites, the default folder, recents, or launch settings changed.</summary>
    public event EventHandler? Changed;

    /// <summary>A background folder-index build finished; open searches should re-run.</summary>
    public event EventHandler? IndexUpdated;

    public RecentFolders Recents { get; }

    public IReadOnlyList<AgentCli> Agents => AgentCliList.Parse(_settings.AgentsText);

    public IReadOnlyList<string> Favorites => FavoriteFolderList.Parse(_settings.FavoriteFoldersText);

    public AgentLaunchSettings LaunchSettings => _settings.LaunchSettings;

    public bool ShowAgentsInRoot => _settings.ShowAgentsInRoot;

    /// <summary>The default folder the user set, normalized, or null when unset.</summary>
    public string? ConfiguredDefaultFolder => FolderPaths.TryNormalize(_settings.DefaultSessionFolder);

    /// <summary>Where sessions start by default: the configured folder unless it's confirmed
    /// missing, else the user folder — so an unplugged drive degrades to home rather than a failed
    /// launch, and a share that's merely slow to answer doesn't stall the caller.</summary>
    public string DefaultFolder =>
        ConfiguredDefaultFolder is { } configured && !FolderPaths.LooksMissing(configured) ? configured : FolderPaths.UserFolder;

    // ----- Folders ----------------------------------------------------------------------------

    public bool IsFavorite(string folder) => Favorites.Any(f => FolderPaths.Same(f, folder));

    /// <summary>Adds a favorite at the end of the list. False when it's already a favorite.</summary>
    public bool AddFavorite(string folder) => EditFavorites(lines =>
    {
        if (lines.Any(l => FolderPaths.Same(l.Folder, folder)))
        {
            return false;
        }

        lines.Add(new FavoriteFolderLine(folder, folder));
        return true;
    });

    public bool RemoveFavorite(string folder) =>
        EditFavorites(lines => lines.RemoveAll(l => FolderPaths.Same(l.Folder, folder)) > 0);

    /// <summary>Moves a favorite up (negative) or down (positive) among the favorites. False at
    /// either end. Comment and other non-folder lines stay where they are.</summary>
    public bool MoveFavorite(string folder, int offset) => EditFavorites(lines =>
    {
        // The line slots that hold favorites (first occurrence of each folder), in order.
        var slots = new List<int>();
        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].Folder is { } f && !slots.Any(s => FolderPaths.Same(lines[s].Folder, f)))
            {
                slots.Add(i);
            }
        }

        var from = slots.FindIndex(s => FolderPaths.Same(lines[s].Folder, folder));
        var to = from + offset;
        if (from < 0 || to < 0 || to >= slots.Count)
        {
            return false;
        }

        // Reorder the favorites, then write them back into the same slots.
        var ordered = slots.Select(s => lines[s]).ToList();
        var moving = ordered[from];
        ordered.RemoveAt(from);
        ordered.Insert(to, moving);
        for (var i = 0; i < slots.Count; i++)
        {
            lines[slots[i]] = ordered[i];
        }

        return true;
    });

    /// <summary>Sets the default session folder; null or the user folder itself clears it.</summary>
    public void SetDefaultFolder(string? folder)
    {
        lock (_gate)
        {
            _settings.DefaultSessionFolder = folder is null || FolderPaths.Same(folder, FolderPaths.UserFolder) ? "" : folder;
        }

        RaiseChanged();
    }

    // Edits operate on the stored lines, not the parsed list, so comments and lines the user
    // wrote by hand on the Settings page survive a palette edit.
    private bool EditFavorites(Func<List<FavoriteFolderLine>, bool> edit)
    {
        lock (_gate)
        {
            var lines = FavoriteFolderList.ReadLines(_settings.FavoriteFoldersText);
            if (!edit(lines))
            {
                return false;
            }

            _settings.FavoriteFoldersText = FavoriteFolderList.WriteLines(lines);
        }

        RaiseChanged();
        return true;
    }

    // ----- Agents -----------------------------------------------------------------------------

    /// <summary>Replaces <paramref name="original"/> with <paramref name="agent"/> in place, or
    /// appends <paramref name="agent"/> when there's no original (or it's gone).</summary>
    public void SaveAgent(AgentCli? original, AgentCli agent) => EditAgents(lines =>
    {
        var index = original is null ? -1 : lines.FindIndex(l => l.Agent?.Key == original.Key);
        if (index >= 0)
        {
            lines[index] = AgentCliList.LineFor(agent);
        }
        else if (!lines.Any(l => l.Agent == agent))
        {
            lines.Add(AgentCliList.LineFor(agent));
        }

        return true;
    });

    public bool RemoveAgent(AgentCli agent) => EditAgents(lines => lines.RemoveAll(l => l.Agent?.Key == agent.Key) > 0);

    /// <summary>Re-adds any built-in agent whose command isn't in the list. Returns how many.</summary>
    public int RestoreBuiltInAgents()
    {
        var added = 0;
        EditAgents(lines =>
        {
            foreach (var builtIn in AgentCliList.BuiltIn)
            {
                if (!lines.Any(l => string.Equals(l.Agent?.Command, builtIn.Command, StringComparison.OrdinalIgnoreCase)))
                {
                    lines.Add(AgentCliList.LineFor(builtIn));
                    added++;
                }
            }

            return added > 0;
        });
        return added;
    }

    private bool EditAgents(Func<List<AgentCliLine>, bool> edit)
    {
        lock (_gate)
        {
            var lines = AgentCliList.ReadLines(_settings.AgentsText);
            if (!edit(lines))
            {
                return false;
            }

            _settings.AgentsText = AgentCliList.WriteLines(lines);
        }

        RaiseChanged();
        return true;
    }

    // ----- Launching --------------------------------------------------------------------------

    /// <summary>Starts <paramref name="agent"/> in <paramref name="folder"/> and records the folder
    /// as recent. Throws when the terminal or shell can't be resolved or the process won't start.</summary>
    public void Launch(AgentCli agent, string folder)
    {
        var spec = AgentSessionLauncher.Plan(agent, folder, LaunchSettings);
        Log.Info($"Starting agent session '{agent.Name}' in '{folder}': {spec.FileName} {spec.Arguments}");
        AgentSessionLauncher.Start(spec);
        Recents.Record(folder);
        RaiseChanged();
    }

    // ----- Folder index -----------------------------------------------------------------------

    /// <summary>The folders search looks under: the default folder, then the favorites.</summary>
    public IReadOnlyList<string> SearchRoots
    {
        get
        {
            var roots = new List<string> { DefaultFolder };
            roots.AddRange(Favorites.Where(f => !roots.Any(r => FolderPaths.Same(r, f))));
            return roots;
        }
    }

    /// <summary>
    /// Returns the current folder index, starting a background rebuild when there's none yet, it's
    /// stale, or the search roots changed. The caller gets the previous snapshot meanwhile (possibly
    /// empty) and hears about the new one via <see cref="IndexUpdated"/>.
    /// </summary>
    public IReadOnlyList<FolderEntry> GetFolderIndex(out bool building)
    {
        var roots = SearchRoots;
        var key = string.Join('\n', roots);
        lock (_gate)
        {
            var fresh = key == _indexRootsKey && DateTime.UtcNow - _indexBuiltUtc < IndexMaxAge;
            if (!fresh && _indexBuild is null)
            {
                _indexBuild = Task.Run(() => BuildIndex(roots, key));
            }

            building = _indexBuild is not null;
            return _index;
        }
    }

    /// <summary>Completes when no index build is running. For tests.</summary>
    internal Task WhenIndexIdle()
    {
        lock (_gate)
        {
            return _indexBuild ?? Task.CompletedTask;
        }
    }

    private void BuildIndex(IReadOnlyList<string> roots, string key)
    {
        IReadOnlyList<FolderEntry> entries;
        try
        {
            entries = FolderIndex.Build(roots, _indexOptions);
        }
        catch (Exception ex)
        {
            Log.Warn($"Couldn't index session folders: {ex.Message}");
            entries = [];
        }

        lock (_gate)
        {
            _index = entries;
            _indexRootsKey = key;
            _indexBuiltUtc = DateTime.UtcNow;
            _indexBuild = null;
        }

        try
        {
            IndexUpdated?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Log.Warn($"Folder index listener failed: {ex.Message}");
        }
    }

    private void RaiseChanged()
    {
        try
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            // A page failing to refresh must not fail the edit that triggered it.
            Log.Warn($"Agent sessions change listener failed: {ex.Message}");
        }
    }
}
