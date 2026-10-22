using PaletteShellExtension.Classes;
using Xunit;

namespace PaletteShellExtension.Tests;

public class WindowsCommandLineTests
{
    [Theory]
    [InlineData("plain")]
    [InlineData("")]
    [InlineData("with space")]
    [InlineData(@"C:\Program Files\Tool\")]
    [InlineData(@"C:\src\")]
    [InlineData("say \"hi\"")]
    [InlineData(@"back\\""slash")]
    [InlineData(@"ends with backslashes\\")]
    [InlineData("tab\there")]
    public void Quote_RoundTripsThroughArgvParsing(string arg)
    {
        var parsed = CommandLineArgv.Parse("program " + WindowsCommandLine.Quote(arg));

        Assert.Equal(["program", arg], parsed);
    }

    [Fact]
    public void Quote_SimpleArgument_IsUnchanged()
    {
        Assert.Equal("-NoExit", WindowsCommandLine.Quote("-NoExit"));
        Assert.Equal(@"C:\src\app", WindowsCommandLine.Quote(@"C:\src\app"));
    }

    [Fact]
    public void Join_PreservesEveryArgument()
    {
        string[] args = ["-d", @"C:\My Projects\app\", "--title", "a \"b\" c", ""];

        Assert.Equal(args, CommandLineArgv.Parse(WindowsCommandLine.Join(args)));
    }

    [Theory]
    [InlineData("claude", "claude", "")]
    [InlineData("  claude   --model opus  ", "claude", "--model opus")]
    [InlineData("\"C:/Program Files/Tool/tool.exe\" --flag \"x y\"", "C:/Program Files/Tool/tool.exe", "--flag \"x y\"")]
    [InlineData("\"unterminated path", "unterminated path", "")]
    [InlineData("", "", "")]
    public void SplitProgram_SeparatesProgramFromArguments(string commandLine, string program, string arguments)
    {
        Assert.Equal((program, arguments), WindowsCommandLine.SplitProgram(commandLine));
    }

    [Fact]
    public void JoinProgram_QuotesProgramOnlyWhenItHasSpaces()
    {
        Assert.Equal("codex --full-auto", WindowsCommandLine.JoinProgram("codex", "--full-auto"));
        Assert.Equal("\"C:/Program Files/x.exe\"", WindowsCommandLine.JoinProgram("C:/Program Files/x.exe", "  "));
    }
}
