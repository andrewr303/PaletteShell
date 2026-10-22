using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PaletteShellExtension.Classes;

/// <summary>
/// Windows command-line quoting for programs that parse their arguments with the MSVCRT /
/// <c>CommandLineToArgvW</c> rules — which is every C, C++, and .NET program we hand a command
/// line to here, including <c>wt.exe</c>. Raw strings are never pasted into a command line
/// unquoted, so a folder or title with spaces, quotes, or a trailing backslash survives intact.
/// </summary>
internal static class WindowsCommandLine
{
    /// <summary>Quotes one argument so it parses back as exactly <paramref name="arg"/>. Arguments
    /// with no whitespace or quotes pass through unchanged, keeping command lines readable in logs.</summary>
    public static string Quote(string arg)
    {
        if (arg.Length > 0 && arg.IndexOfAny([' ', '\t', '\n', '\v', '"']) < 0)
        {
            return arg;
        }

        var sb = new StringBuilder(arg.Length + 2);
        sb.Append('"');
        var backslashes = 0;
        foreach (var c in arg)
        {
            if (c == '\\')
            {
                backslashes++;
                continue;
            }

            if (c == '"')
            {
                // Backslashes before a quote are literal only when doubled, and the quote
                // itself needs one more to be taken literally.
                sb.Append('\\', (backslashes * 2) + 1).Append('"');
            }
            else
            {
                sb.Append('\\', backslashes).Append(c);
            }

            backslashes = 0;
        }

        // Trailing backslashes sit right before the closing quote, so they're doubled too —
        // otherwise "C:\src\" would escape its own closing quote.
        sb.Append('\\', backslashes * 2).Append('"');
        return sb.ToString();
    }

    /// <summary>Quotes each argument and joins them with spaces.</summary>
    public static string Join(IEnumerable<string> args) => string.Join(' ', args.Select(Quote));

    /// <summary>
    /// Splits a user-typed command line into the program and the rest of the line, the way
    /// <c>CreateProcess</c> finds the program: a leading double-quoted token (for paths with
    /// spaces) or everything up to the first whitespace. The remainder is returned untouched so
    /// the user's own quoting of arguments is preserved.
    /// </summary>
    public static (string Program, string Arguments) SplitProgram(string? commandLine)
    {
        var text = (commandLine ?? "").Trim();
        if (text.Length == 0)
        {
            return ("", "");
        }

        if (text[0] == '"')
        {
            var close = text.IndexOf('"', 1);
            return close < 0
                ? (text[1..].Trim(), "")
                : (text[1..close].Trim(), text[(close + 1)..].Trim());
        }

        var end = 0;
        while (end < text.Length && !char.IsWhiteSpace(text[end]))
        {
            end++;
        }

        return (text[..end], text[end..].Trim());
    }

    /// <summary>The inverse of <see cref="SplitProgram"/>: quotes the program only when it needs it.</summary>
    public static string JoinProgram(string program, string? arguments)
    {
        var head = program.Any(char.IsWhiteSpace) ? $"\"{program}\"" : program;
        return string.IsNullOrWhiteSpace(arguments) ? head : $"{head} {arguments.Trim()}";
    }
}
