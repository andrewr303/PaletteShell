using Microsoft.CommandPalette.Extensions.Toolkit;
using PaletteShellExtension.Classes;

namespace PaletteShellExtension.Commands;

/// <summary>Removes an agent CLI from the list after a confirmation. Only the list entry goes —
/// nothing is uninstalled — and built-in agents can be brought back with "Restore built-in agents".</summary>
internal sealed partial class RemoveAgentCliCommand(AgentSessionService service, AgentCli agent) : InvokableCommand
{
    public override string Name => "Remove agent";

    public override IconInfo Icon => SessionIcons.Delete;

    public override CommandResult Invoke() =>
        CommandResult.Confirm(new ConfirmationArgs
        {
            Title = $"Remove {agent.Name}?",
            Description = $"'{agent.CommandLine}' will no longer be listed in Agent sessions. The CLI itself isn't touched.",
            PrimaryCommand = new CallbackCommand("Remove", () =>
            {
                service.RemoveAgent(agent);
                return CommandResult.ShowToast(new ToastArgs { Message = $"Removed {agent.Name}", Result = CommandResult.KeepOpen() });
            }),
            IsPrimaryCommandCritical = true,
        });
}
