using Microsoft.CommandPalette.Extensions.Toolkit;
using PaletteShellExtension.Classes;

namespace PaletteShellExtension.Commands;

/// <summary>Moves a favorite folder one place up or down, which is the order the picker lists them in.</summary>
internal sealed partial class MoveFavoriteFolderCommand(AgentSessionService service, string folder, int offset) : InvokableCommand
{
    public override string Name => offset < 0 ? "Move up" : "Move down";

    public override IconInfo Icon => offset < 0 ? SessionIcons.MoveUp : SessionIcons.MoveDown;

    public override CommandResult Invoke()
    {
        service.MoveFavorite(folder, offset);
        return CommandResult.KeepOpen();
    }
}
