using System;
using System.Collections.Generic;

namespace PaletteShellExtension.Classes;

/// <summary>
/// Keeps a stale Settings page from undoing palette edits. The toolkit's settings form captures
/// every value when it's built and writes them all back on Save, and an extension can't make an
/// open form refresh — so after changing a list from the palette while Settings is open, saving
/// Settings (even just to flip a toggle) would put the old list back. This remembers, per setting,
/// the value the palette last wrote and the values it replaced. A Save that submits one of those
/// replaced values is stale and the palette's value is kept; any other submitted value is a
/// deliberate edit and wins. Values are compared after normalizing (parse and re-format), so line
/// endings and spacing from the Settings text box don't matter.
/// </summary>
internal sealed class SettingsEditGuard
{
    // Enough history for any realistic session; bounded so it can't grow without limit.
    private const int MaxReplaced = 50;

    private sealed class History
    {
        public string Current = "";
        public List<string> Replaced = [];
    }

    private readonly Dictionary<string, History> _history = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    /// <summary>Records that the palette changed <paramref name="key"/> from
    /// <paramref name="previous"/> to <paramref name="next"/>.</summary>
    public void RecordPaletteEdit(string key, string? previous, string next, Func<string?, string> normalize)
    {
        lock (_gate)
        {
            if (!_history.TryGetValue(key, out var history))
            {
                _history[key] = history = new History();
            }

            Remember(history, normalize(previous));
            history.Replaced.Remove(normalize(next));
            history.Current = next;
        }
    }

    /// <summary>The value to keep for <paramref name="key"/> after the Settings page saved
    /// <paramref name="submitted"/>.</summary>
    public string? Reconcile(string key, string? submitted, Func<string?, string> normalize)
    {
        lock (_gate)
        {
            if (!_history.TryGetValue(key, out var history))
            {
                return submitted;
            }

            var submittedNormalized = normalize(submitted);
            var currentNormalized = normalize(history.Current);
            if (submittedNormalized == currentNormalized)
            {
                return submitted;
            }

            if (history.Replaced.Contains(submittedNormalized))
            {
                Log.Info($"Ignored a stale Settings value for '{key}'; keeping the newer palette edit");
                return history.Current;
            }

            // A deliberate edit on the Settings page. The palette's value is now the superseded one.
            Remember(history, currentNormalized);
            history.Current = submitted ?? "";
            return submitted;
        }
    }

    private static void Remember(History history, string normalized)
    {
        if (history.Replaced.Contains(normalized))
        {
            return;
        }

        history.Replaced.Add(normalized);
        if (history.Replaced.Count > MaxReplaced)
        {
            history.Replaced.RemoveAt(0);
        }
    }
}
