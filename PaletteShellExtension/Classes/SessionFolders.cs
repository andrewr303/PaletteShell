using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace PaletteShellExtension.Classes;

/// <summary>Path helpers shared by the Agent sessions folder features.</summary>
internal static class FolderPaths
{
    /// <summary>The user's profile folder — the default session folder until one is set.</summary>
    public static string UserFolder => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>True when typed text reads as a path rather than a search term: drive-rooted
    /// (<c>C:\</c>, <c>D:</c>), UNC, rooted, home-relative (<c>~</c>), or starting with an
    /// environment variable (<c>%USERPROFILE%</c>).</summary>
    public static bool LooksLikePath(string? text)
    {
        var t = text?.Trim().TrimStart('"') ?? "";
        if (t.Length == 0)
        {
            return false;
        }

        return t[0] is '~' or '%' or '\\' or '/'
            || (t.Length >= 2 && char.IsAsciiLetter(t[0]) && t[1] == ':');
    }

    /// <summary>Expands a leading <c>~</c> to the user folder and any <c>%VAR%</c> references, and
    /// drops surrounding quotes (Explorer's "Copy as path" adds them).</summary>
    public static string Expand(string text)
    {
        var t = Environment.ExpandEnvironmentVariables(text.Trim().Trim('"').Trim());
        if (t == "~")
        {
            return UserFolder;
        }

        if (t.Length >= 2 && t[0] == '~' && (t[1] == '\\' || t[1] == '/'))
        {
            // Concatenated rather than Path.Join'd so a bare "~\" keeps its trailing separator,
            // which path completion reads as "list the user folder's children".
            return Path.TrimEndingDirectorySeparator(UserFolder) + Path.DirectorySeparatorChar + t[2..];
        }

        return t;
    }

    /// <summary>
    /// Expands and canonicalizes a folder path for storage and comparison: absolute, with no
    /// trailing separator (except on a root like <c>C:\</c>). A bare drive (<c>D:</c>) means its
    /// root, not the drive's current directory. Returns null for blank or invalid input.
    /// </summary>
    public static string? TryNormalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        try
        {
            var expanded = Expand(text);
            if (expanded.Length == 2 && char.IsAsciiLetter(expanded[0]) && expanded[1] == ':')
            {
                expanded += Path.DirectorySeparatorChar;
            }

            if (!Path.IsPathFullyQualified(expanded))
            {
                return null;
            }

            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(expanded));
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Case-insensitive comparison of two normalized paths (Windows paths are).</summary>
    public static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>The folder's own name, or the whole path for a root such as <c>C:\</c>.</summary>
    public static string DisplayName(string folder)
    {
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(folder));
        return string.IsNullOrEmpty(name) ? folder : name;
    }

    public static bool Exists(string folder)
    {
        try
        {
            return Directory.Exists(folder);
        }
        catch (Exception)
        {
            return false;
        }
    }

    // Network paths that recently didn't answer within a probe's timeout, and until when to
    // stop asking: page rebuilds happen often, and each would otherwise wait out the timeout again.
    private static readonly ConcurrentDictionary<string, DateTime> UnresponsivePaths = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan UnresponsiveBackoff = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Whether a folder exists, without risking a long hang on the caller's thread. Local paths
    /// are checked directly; a network path (UNC or a mapped drive) — which can block for tens of
    /// seconds when its share is offline — gets <paramref name="timeoutMs"/>, after which the
    /// answer is null (unknown) and stays unknown for a short back-off.
    /// </summary>
    public static bool? ProbeExists(string folder, int timeoutMs)
    {
        if (!IsNetworkPath(folder))
        {
            return Exists(folder);
        }

        if (UnresponsivePaths.TryGetValue(folder, out var until) && until > DateTime.UtcNow)
        {
            return null;
        }

        var check = Task.Run(() => Exists(folder));
        if (check.Wait(timeoutMs))
        {
            UnresponsivePaths.TryRemove(folder, out _);
            return check.Result;
        }

        UnresponsivePaths[folder] = DateTime.UtcNow + UnresponsiveBackoff;
        return null;
    }

    /// <summary>True only when the folder is confirmed missing — for "not found" hints on list
    /// rows, which mustn't stall the list for a share that's slow to answer.</summary>
    public static bool LooksMissing(string folder) => ProbeExists(folder, 250) == false;

    /// <summary>UNC paths and paths on mapped network drives.</summary>
    internal static bool IsNetworkPath(string path)
    {
        if (path.StartsWith(@"\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal))
        {
            return true;
        }

        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            var root = Path.GetPathRoot(path);
            return !string.IsNullOrEmpty(root) && new DriveInfo(root).DriveType == DriveType.Network;
        }
        catch (Exception)
        {
            return false;
        }
    }
}

/// <summary>One line of the stored favorites: its text, and the folder it names (null for a
/// blank, comment, or invalid line, which edits leave untouched).</summary>
internal sealed record FavoriteFolderLine(string Text, string? Folder);

/// <summary>
/// Reads and writes the favorite-folder list stored in Settings: one folder per line, so it's
/// editable from the Settings page as well as with the palette's add/remove commands. Entries are
/// normalized and de-duplicated on read, but folders that don't currently exist are kept — an
/// unplugged drive or offline share may come back. Palette edits go through
/// <see cref="ReadLines"/>/<see cref="WriteLines"/>, so comments and hand-written lines survive.
/// </summary>
internal static class FavoriteFolderList
{
    public static List<string> Parse(string? text)
    {
        var folders = new List<string>();
        foreach (var line in ReadLines(text))
        {
            if (line.Folder is { } folder && !folders.Any(f => FolderPaths.Same(f, folder)))
            {
                folders.Add(folder);
            }
        }

        return folders;
    }

    public static string Format(IEnumerable<string> folders) => string.Join(Environment.NewLine, folders);

    public static List<FavoriteFolderLine> ReadLines(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        return [.. text.Split(["\r\n", "\n", "\r"], StringSplitOptions.None).Select(line =>
        {
            var trimmed = line.Trim();
            var folder = trimmed.Length == 0 || trimmed.StartsWith('#') ? null : FolderPaths.TryNormalize(trimmed);
            return new FavoriteFolderLine(line, folder);
        })];
    }

    public static string WriteLines(IEnumerable<FavoriteFolderLine> lines) =>
        string.Join(Environment.NewLine, lines.Select(l => l.Text));
}

/// <summary>A folder a session was recently started in.</summary>
internal readonly record struct RecentFolder(string Path, DateTime LastUsedUtc);

/// <summary>
/// Most-recently-used session folders, newest first, capped at <see cref="Capacity"/>. Stored as
/// a small tab-separated text file (timestamp, path) next to settings.json — machine-local
/// history, not a preference worth syncing. Every operation is best-effort: a locked or corrupt
/// file only loses history, never breaks launching.
/// </summary>
internal sealed class RecentFolders
{
    public const int Capacity = 12;

    private readonly string _storePath;
    private readonly object _gate = new();
    private List<RecentFolder> _items;

    public RecentFolders(string storePath)
    {
        _storePath = storePath;
        _items = Load(storePath);
    }

    /// <summary>The default store location, beside the extension's settings.json.</summary>
    public static string DefaultStorePath() =>
        Path.Combine(PaletteShellSettingsManager.SettingsDirectory, "agent-session-recents.txt");

    /// <summary>Snapshot of the recent folders, newest first.</summary>
    public IReadOnlyList<RecentFolder> Items
    {
        get
        {
            lock (_gate)
            {
                return [.. _items];
            }
        }
    }

    /// <summary>Moves <paramref name="folder"/> to the front (adding it if new) and persists.</summary>
    public void Record(string folder, DateTime? whenUtc = null)
    {
        lock (_gate)
        {
            _items.RemoveAll(r => FolderPaths.Same(r.Path, folder));
            _items.Insert(0, new RecentFolder(folder, whenUtc ?? DateTime.UtcNow));
            if (_items.Count > Capacity)
            {
                _items.RemoveRange(Capacity, _items.Count - Capacity);
            }

            Save();
        }
    }

    /// <summary>Forgets <paramref name="folder"/>. Returns false when it wasn't in the list.</summary>
    public bool Remove(string folder)
    {
        lock (_gate)
        {
            if (_items.RemoveAll(r => FolderPaths.Same(r.Path, folder)) == 0)
            {
                return false;
            }

            Save();
            return true;
        }
    }

    private static List<RecentFolder> Load(string storePath)
    {
        var items = new List<RecentFolder>();
        try
        {
            if (!File.Exists(storePath))
            {
                return items;
            }

            foreach (var line in File.ReadAllLines(storePath))
            {
                var tab = line.IndexOf('\t');
                if (tab <= 0
                    || !DateTime.TryParse(line[..tab], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var when)
                    || FolderPaths.TryNormalize(line[(tab + 1)..]) is not { } path
                    || items.Any(r => FolderPaths.Same(r.Path, path)))
                {
                    continue;
                }

                items.Add(new RecentFolder(path, when.ToUniversalTime()));
                if (items.Count == Capacity)
                {
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Couldn't read recent session folders: {ex.Message}");
        }

        return items;
    }

    // Must be called under _gate.
    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_storePath)!);
            File.WriteAllLines(_storePath, _items.Select(r =>
                r.LastUsedUtc.ToString("o", CultureInfo.InvariantCulture) + "\t" + r.Path));
        }
        catch (Exception ex)
        {
            // History is a convenience: this session still has it in memory.
            Log.Warn($"Couldn't save recent session folders: {ex.Message}");
        }
    }
}
