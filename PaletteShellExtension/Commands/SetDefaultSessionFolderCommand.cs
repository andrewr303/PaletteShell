using Microsoft.CommandPalette.Extensions.Toolkit;
using PaletteShellExtension.Classes;

namespace PaletteShellExtension.Commands;

/// <summary>Makes a folder the default session folder (the one "Start in …" quick actions use).</summary>
internal sealed partial class SetDefaultSessionFolderCommand(AgentSessionService service, string folder) : InvokableCommand
{
    public override string Name => "Set as default folder";

    public override IconInfo Icon => SessionIcons.Home;

    public override CommandResult Invoke()
    {
        service.SetDefaultFolder(folder);
        return CommandResult.ShowToast(new ToastArgs
        {
            Message = $"Sessions now start in {FolderPaths.DisplayName(folder)} by default",
            Result = CommandResult.KeepOpen(),
        });
    }
}
