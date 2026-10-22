using System;
using System.IO;
using System.Linq;
using System.Text;
using PaletteShellExtension.Classes;
using Xunit;

namespace PaletteShellExtension.Tests;

public class AgentSessionLauncherTests
{
    private const string WindowsTerminal = @"C:\Users\me\AppData\Local\Microsoft\WindowsApps\wt.exe";
    private const string Pwsh = @"C:\Program Files\PowerShell\7\pwsh.exe";
    private const string Cmd = @"C:\Windows\System32\cmd.exe";

    private static readonly AgentCli Claude = new("Claude Code", "claude", "--model opus");

    private static string Folder(string name) => Path.Combine(Path.GetTempPath(), name);

    private static string DecodeScript(string encoded) => Encoding.Unicode.GetString(Convert.FromBase64String(encoded));

    [Fact]
    public void Build_WindowsTerminalWithPowerShell_OpensANewWindowInTheFolder()
    {
        var folder = Folder("My Project");

        var spec = AgentSessionLauncher.Build(Claude, folder, new AgentLaunchSettings(), WindowsTerminal, Pwsh, SessionShell.Pwsh);
        var argv = CommandLineArgv.Parse(spec.Arguments);

        Assert.Equal(WindowsTerminal, spec.FileName);
        Assert.Equal(folder, spec.WorkingDirectory);
        Assert.Equal(
            ["-w", "new", "new-tab", "-d", folder, "--title", "Claude Code · My Project", Pwsh, "-NoLogo", "-NoExit", "-EncodedCommand"],
            argv.Take(argv.Count - 1));

        var script = DecodeScript(argv[^1]);
        Assert.Contains($"Set-Location -LiteralPath '{folder}'", script, StringComparison.Ordinal);
        Assert.EndsWith("& 'claude' --model opus\n", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_NewTabSetting_TargetsTheMostRecentWindow()
    {
        var spec = AgentSessionLauncher.Build(Claude, Folder("p"), new AgentLaunchSettings(Window: TerminalWindowTarget.NewTab), WindowsTerminal, Pwsh, SessionShell.Pwsh);

        Assert.Equal(["-w", "0", "new-tab"], CommandLineArgv.Parse(spec.Arguments).Take(3));
    }

    [Fact]
    public void Build_KeepOpenOff_DropsNoExit()
    {
        var spec = AgentSessionLauncher.Build(Claude, Folder("p"), new AgentLaunchSettings(KeepOpen: false), null, Pwsh, SessionShell.Pwsh);

        Assert.DoesNotContain("-NoExit", CommandLineArgv.Parse(spec.Arguments));
    }

    [Fact]
    public void Build_ApostrophesInFolderAndCommand_AreEscapedInTheScript()
    {
        var folder = Folder("Sean's repo");
        var agent = new AgentCli("Tool", "C:/Sean's tools/tool.exe", "");

        var spec = AgentSessionLauncher.Build(agent, folder, new AgentLaunchSettings(), null, Pwsh, SessionShell.Pwsh);
        var script = DecodeScript(CommandLineArgv.Parse(spec.Arguments)[^1]);

        Assert.Contains($"Set-Location -LiteralPath '{folder.Replace("'", "''", StringComparison.Ordinal)}'", script, StringComparison.Ordinal);
        Assert.EndsWith("& 'C:/Sean''s tools/tool.exe'\n", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_SemicolonInFolder_PassesDotSoWindowsTerminalDoesNotSplitIt()
    {
        var folder = Folder("a;b");

        var spec = AgentSessionLauncher.Build(Claude, folder, new AgentLaunchSettings(), WindowsTerminal, Pwsh, SessionShell.Pwsh);
        var argv = CommandLineArgv.Parse(spec.Arguments);

        Assert.Equal(".", argv[argv.IndexOf("-d") + 1]);
        Assert.Equal("Claude Code · a,b", argv[argv.IndexOf("--title") + 1]);
        Assert.Equal(folder, spec.WorkingDirectory);
        Assert.DoesNotContain(argv, a => a.Contains(';', StringComparison.Ordinal));
    }

    [Fact]
    public void Build_ConsoleWithPowerShell_StartsTheShellDirectly()
    {
        var folder = Folder("p");

        var spec = AgentSessionLauncher.Build(Claude, folder, new AgentLaunchSettings(), null, Pwsh, SessionShell.Pwsh);
        var argv = CommandLineArgv.Parse(spec.Arguments);

        Assert.Equal(Pwsh, spec.FileName);
        Assert.Equal(folder, spec.WorkingDirectory);
        Assert.Equal(["-NoLogo", "-NoExit", "-EncodedCommand"], argv.Take(3));
        Assert.Contains("& 'claude' --model opus", DecodeScript(argv[3]), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true, "/s /k \"claude --model opus\"")]
    [InlineData(false, "/s /c \"claude --model opus\"")]
    public void Build_ConsoleWithCmd_PassesTheCommandLineRaw(bool keepOpen, string expected)
    {
        var spec = AgentSessionLauncher.Build(Claude, Folder("p"), new AgentLaunchSettings(KeepOpen: keepOpen), null, Cmd, SessionShell.Cmd);

        Assert.Equal(Cmd, spec.FileName);
        Assert.Equal(expected, spec.Arguments);
    }

    [Fact]
    public void Build_WindowsTerminalWithCmd_HandsCmdOneQuotedCommand()
    {
        var agent = new AgentCli("Tool", "C:/Program Files/Tool/tool.exe", "--name \"two words\"");

        var spec = AgentSessionLauncher.Build(agent, Folder("p"), new AgentLaunchSettings(), WindowsTerminal, Cmd, SessionShell.Cmd);
        var argv = CommandLineArgv.Parse(spec.Arguments);

        // wt re-joins its trailing arguments (quoting any with spaces) into cmd's command line,
        // so the /k payload must arrive as one argument holding the whole command.
        Assert.Equal([Cmd, "/s", "/k", "\"C:/Program Files/Tool/tool.exe\" --name \"two words\""], argv.Skip(argv.IndexOf(Cmd)));
    }

    [Fact]
    public void BuildPowerShellScript_NoArguments_InvokesTheBareCommand()
    {
        var script = AgentSessionLauncher.BuildPowerShellScript(new AgentCli("codex", "codex", ""), Folder("p"), "codex · p");

        Assert.Equal(
            $"$Host.UI.RawUI.WindowTitle = 'codex · p'\nSet-Location -LiteralPath '{Folder("p")}'\n& 'codex'\n",
            script);
    }

    [Fact]
    public void CurrentPathDirectories_IncludesTheProcessPath()
    {
        var first = (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();

        if (first is not null)
        {
            Assert.Contains(Environment.ExpandEnvironmentVariables(first.Trim().Trim('"')), AgentSessionLauncher.CurrentPathDirectories());
        }
    }

    [Fact]
    public void IsOnPath_FindsCommandsByExtensionInTheGivenDirectories()
    {
        using var temp = new TestFolder();
        File.WriteAllText(Path.Combine(temp.Path, "fakeagent.ps1"), "");

        Assert.True(AgentSessionLauncher.IsOnPath("fakeagent", [temp.Path]));
        Assert.False(AgentSessionLauncher.IsOnPath("missingagent", [temp.Path]));
        Assert.True(AgentSessionLauncher.IsOnPath(Path.Combine(temp.Path, "fakeagent.ps1")));
    }

    [Theory]
    [InlineData("Sean\u2019s repo", "Sean\u2019\u2019s repo")]
    [InlineData("x\u2018;calc;\u2019", "x\u2018\u2018;calc;\u2019\u2019")]
    [InlineData("a\u201Ab\u201Bc'd", "a\u201A\u201Ab\u201B\u201Bc''d")]
    public void SingleQuote_DoublesTypographicQuotesPowerShellAlsoEndsStringsOn(string value, string escaped)
    {
        Assert.Equal($"'{escaped}'", PowerShellQuoting.SingleQuote(value));
    }

    [Fact]
    public void Build_CmdOnAUncFolder_MapsItWithPushd()
    {
        const string share = @"\\server\share\repo";

        var spec = AgentSessionLauncher.Build(Claude, share, new AgentLaunchSettings(), null, Cmd, SessionShell.Cmd);

        Assert.Equal($"/s /k \"pushd \"{share}\" && claude --model opus\"", spec.Arguments);
    }

    [Fact]
    public void Build_WindowsTerminalWithCmd_EscapesSemicolonsForWindowsTerminal()
    {
        var agent = new AgentCli("Tool", "tool", "--list a;b");

        var spec = AgentSessionLauncher.Build(agent, Folder("p"), new AgentLaunchSettings(), WindowsTerminal, Cmd, SessionShell.Cmd);

        Assert.Equal("tool --list a\\;b", CommandLineArgv.Parse(spec.Arguments)[^1]);
    }
}
