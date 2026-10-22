using System;
using System.IO;
using System.Linq;
using PaletteShellExtension.Classes;
using Xunit;

namespace PaletteShellExtension.Tests;

public class AgentCliTests
{
    [Fact]
    public void DefaultText_ParsesToTheBuiltInAgentsInOrder()
    {
        var agents = AgentCliList.Parse(AgentCliList.DefaultText);

        Assert.Equal(["andrewcode", "claude", "agy", "muse", "codex"], agents.Select(a => a.Command));
        Assert.Equal(AgentCliList.BuiltIn, agents);
    }

    [Fact]
    public void TryParseLine_NameCommandAndArguments()
    {
        Assert.True(AgentCliList.TryParseLine("Claude Code | claude --model opus", out var agent));

        Assert.Equal(new AgentCli("Claude Code", "claude", "--model opus"), agent);
        Assert.Equal("claude --model opus", agent.CommandLine);
    }

    [Fact]
    public void TryParseLine_WithoutName_DerivesItFromTheCommand()
    {
        Assert.True(AgentCliList.TryParseLine("codex --full-auto", out var agent));

        Assert.Equal(new AgentCli("codex", "codex", "--full-auto"), agent);
    }

    [Fact]
    public void TryParseLine_QuotedProgramPath()
    {
        Assert.True(AgentCliList.TryParseLine("My Tool | \"C:/Program Files/Tool/tool.exe\" --x", out var agent));

        Assert.Equal("C:/Program Files/Tool/tool.exe", agent.Command);
        Assert.Equal("--x", agent.Arguments);
        Assert.Equal("\"C:/Program Files/Tool/tool.exe\" --x", agent.CommandLine);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("# Muse | muse")]
    [InlineData("Nameless |")]
    [InlineData("Nameless |   ")]
    public void TryParseLine_BlankCommentOrMissingCommand_IsSkipped(string line)
    {
        Assert.False(AgentCliList.TryParseLine(line, out _));
    }

    [Fact]
    public void Parse_HandlesEveryLineBreakStyleAndDropsDuplicates()
    {
        var agents = AgentCliList.Parse("A | a\r\nB | b\rC | c\n\nA | a\n# comment");

        Assert.Equal(["A", "B", "C"], agents.Select(a => a.Name));
    }

    [Fact]
    public void Format_RoundTripsThroughParse()
    {
        AgentCli[] agents =
        [
            new("Claude Code", "claude", "--model opus"),
            new("Tool", "C:/Program Files/Tool/tool.exe", "--flag \"two words\""),
            new("codex", "codex", ""),
        ];

        Assert.Equal(agents, AgentCliList.Parse(AgentCliList.Format(agents)));
    }

    [Fact]
    public void Create_CleansNameAndMergesInlineArguments()
    {
        var agent = AgentCliList.Create(" Fancy | Name\n", "claude --model opus", "--verbose");

        Assert.Equal(new AgentCli("Fancy / Name", "claude", "--model opus --verbose"), agent);
        Assert.Equal(agent, AgentCliList.Parse(AgentCliList.Format([agent!])).Single());
    }

    [Fact]
    public void Create_BlankName_DefaultsToTheProgramFileName()
    {
        Assert.Equal("agent", AgentCliList.Create("", "C:/tools/agent.exe", null)!.Name);
    }

    [Fact]
    public void Create_BlankCommand_ReturnsNull()
    {
        Assert.Null(AgentCliList.Create("Name", "   ", "--x"));
    }

    [Fact]
    public void Key_ChangesWhenArgumentsChange()
    {
        Assert.NotEqual(new AgentCli("A", "a", "").Key, new AgentCli("A", "a", "--x").Key);
    }

    [Fact]
    public void Create_UnquotedPathWithSpacesToAnExistingFile_IsTheWholeProgram()
    {
        using var temp = new TestFolder();
        var tool = Path.Combine(temp.Create("My Tools"), "my tool.exe");
        File.WriteAllText(tool, "");

        Assert.Equal(new AgentCli("my tool", tool, ""), AgentCliList.Create("", tool, null));
    }

    [Fact]
    public void Create_NameStartingWithHash_IsKeptAsAnAgentNotAComment()
    {
        var agent = AgentCliList.Create("#1 agent", "claude", null)!;

        Assert.Equal("1 agent", agent.Name);
        Assert.Equal([agent], AgentCliList.Parse(AgentCliList.Format([agent])));
    }

    [Fact]
    public void ReadLines_KeepsCommentsAndInvalidLinesVerbatim()
    {
        const string text = "# mine\nClaude Code | claude\nbroken |\n";
        var lines = AgentCliList.ReadLines(text);

        Assert.Equal([null, "claude", null, null], lines.Select(l => l.Agent?.Command));
        Assert.Equal(text.Replace("\n", Environment.NewLine, StringComparison.Ordinal), AgentCliList.WriteLines(lines));
    }
}
