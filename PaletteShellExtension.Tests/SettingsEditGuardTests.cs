using PaletteShellExtension.Classes;
using Xunit;

namespace PaletteShellExtension.Tests;

public class SettingsEditGuardTests
{
    private static string Normalize(string? text) => FavoriteFolderList.Format(FavoriteFolderList.Parse(text));

    [Fact]
    public void Reconcile_StaleFormAfterAPaletteEdit_KeepsThePaletteValue()
    {
        var guard = new SettingsEditGuard();
        using var temp = new TestFolder();
        var before = temp.Create("a");
        var after = before + "\n" + temp.Create("b");

        guard.RecordPaletteEdit("favorites", before, after, Normalize);

        // A Settings form built before the edit submits the old list, with the text box's \r breaks.
        Assert.Equal(after, guard.Reconcile("favorites", before + "\r", Normalize));
    }

    [Fact]
    public void Reconcile_DeliberateSettingsEdit_Wins()
    {
        var guard = new SettingsEditGuard();
        using var temp = new TestFolder();
        var a = temp.Create("a");
        var b = temp.Create("b");
        var c = temp.Create("c");

        guard.RecordPaletteEdit("favorites", a, a + "\n" + b, Normalize);

        Assert.Equal(c, guard.Reconcile("favorites", c, Normalize));

        // The palette's value is now the superseded one: a later stale submit of it is ignored.
        Assert.Equal(c, guard.Reconcile("favorites", a + "\n" + b, Normalize));
    }

    [Fact]
    public void Reconcile_SubmittingTheCurrentValue_IsKept()
    {
        var guard = new SettingsEditGuard();
        guard.RecordPaletteEdit("agents", "A | a", "A | a\nB | b", t => AgentCliList.Format(AgentCliList.Parse(t)));

        Assert.Equal("A | a\r\nB | b", guard.Reconcile("agents", "A | a\r\nB | b", t => AgentCliList.Format(AgentCliList.Parse(t))));
    }

    [Fact]
    public void Reconcile_WithoutPaletteEdits_PassesThrough()
    {
        Assert.Equal("anything", new SettingsEditGuard().Reconcile("agents", "anything", t => t ?? ""));
    }
}
