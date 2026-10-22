using Microsoft.CommandPalette.Extensions.Toolkit;
using PaletteShellExtension.Classes;
using System;
using System.IO;

namespace PaletteShellExtension.Commands;

/// <summary>
/// Starts an agent CLI in a folder and dismisses the palette. With
/// <paramref name="createFolder"/> the folder is created first (the picker offers this for a typed
/// path that doesn't exist yet — the "new project" case). Failures — a missing folder, shell, or
/// terminal — are explained in a dialog rather than a toast the user might miss.
/// </summary>
internal sealed partial class LaunchAgentSessionCommand(
    AgentSessionService service,
    AgentCli agent,
    string folder,
    bool createFolder = false) : InvokableCommand
{
    public override string Name => createFolder ? $"Create folder and start {agent.Name}" : $"Start {agent.Name}";

    public override IconInfo Icon => createFolder ? SessionIcons.NewFolder : SessionIcons.Start;

    public override CommandResult Invoke()
    {
        try
        {
            if (createFolder)
            {
                Directory.CreateDirectory(folder);
            }
            else if (!FolderPaths.Exists(folder))
            {
                return WarningDialog.Show("Folder not found", $"'{folder}' doesn't exist or isn't reachable right now.");
            }

            service.Launch(agent, folder);
            return CommandResult.Dismiss();
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to start agent session '{agent.Name}' in '{folder}'", ex);
            return WarningDialog.Show($"Couldn't start {agent.Name}", ex.Message);
        }
    }
}
