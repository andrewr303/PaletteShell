using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using PaletteShellExtension.Classes;
using PaletteShellExtension.Commands;
using System.Linq;

namespace PaletteShellExtension.Pages;

/// <summary>The folder-first flow: a folder is chosen (from Favorite folders), now pick which
/// agent to start in it.</summary>
internal sealed partial class AgentPickerPage : ListPage
{
    private readonly AgentSessionService _service;
    private readonly string _folder;

    public AgentPickerPage(AgentSessionService service, string folder)
    {
        _service = service;
        _folder = folder;
        Title = $"Start a session in {SessionFolderItems.Title(folder)}";
        Name = "Choose agent";
        Icon = SessionIcons.Agent;
        PlaceholderText = "Choose an agent CLI";
    }

    public override IListItem[] GetItems()
    {
        var agents = _service.Agents;
        if (agents.Count == 0)
        {
            return
            [
                new ListItem(AgentSessionsPage.AgentFormPage(_service, null))
                {
                    Title = "Add an agent CLI",
                    Subtitle = "No agent CLIs are set up yet",
                    Icon = SessionIcons.Add,
                },
            ];
        }

        return [.. agents.Select(agent => new ListItem(new LaunchAgentSessionCommand(_service, agent, _folder))
        {
            Title = agent.Name,
            Subtitle = agent.CommandLine,
            Icon = SessionIcons.Agent,
        })];
    }
}
