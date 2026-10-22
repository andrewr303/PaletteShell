using System;
using System.Text;

namespace PaletteShellExtension.Classes;

internal static class PowerShellQuoting
{
    // PowerShell ends a single-quoted string at any of these — not just the ASCII apostrophe —
    // so all of them must be doubled (PowerShell's own EscapeSingleQuotedStringContent does the
    // same). A folder named with a typographic apostrophe ("Sean’s repo") is common.
    private static readonly char[] SingleQuoteChars = ['\'', '‘', '’', '‚', '‛'];

    /// <summary>
    /// Wraps a value as a PowerShell single-quoted string literal, doubling embedded
    /// quotes so it can't break out of the string or inject commands.
    /// </summary>
    public static string SingleQuote(string? value)
    {
        var text = value ?? "";
        var sb = new StringBuilder(text.Length + 2).Append('\'');
        foreach (var c in text)
        {
            if (Array.IndexOf(SingleQuoteChars, c) >= 0)
            {
                sb.Append(c);
            }

            sb.Append(c);
        }

        return sb.Append('\'').ToString();
    }
}
