using Microsoft.CommandPalette.Extensions.Toolkit;
using PaletteShellExtension.Classes;

namespace PaletteShellExtension.Forms;

/// <summary>Adds a new agent CLI, or edits <c>existing</c> in place.</summary>
internal sealed partial class AgentCliForm : FormContent
{
    private readonly AgentSessionService _service;
    private readonly AgentCli? _existing;

    public AgentCliForm(AgentSessionService service, AgentCli? existing)
    {
        _service = service;
        _existing = existing;

        var heading = existing is null ? "Add agent CLI" : $"Edit {existing.Name}";

        // The command is shown quoted when it has spaces, so saving it unchanged doesn't split a
        // path like C:\Program Files\Tool\tool.exe into a program and arguments.
        TemplateJson = $$"""
{
  "$schema": "http://adaptivecards.io/schemas/adaptive-card.json",
  "type": "AdaptiveCard",
  "version": "1.6",
  "body": [
    { "type": "TextBlock", "size": "Medium", "weight": "Bolder", "text": "{{FormJson.Escape(heading)}}", "wrap": true },
    { "type": "TextBlock", "text": "Any command-line program works. It starts in the folder you pick, inside a terminal running your session shell (see Settings).", "wrap": true, "isSubtle": true },
    {
      "type": "Input.Text",
      "id": "command",
      "label": "Command",
      "placeholder": "claude",
      "value": "{{FormJson.Escape(existing is null ? null : WindowsCommandLine.JoinProgram(existing.Command, null))}}",
      "isRequired": true,
      "errorMessage": "Enter the command to run"
    },
    {
      "type": "Input.Text",
      "id": "arguments",
      "label": "Arguments (optional)",
      "placeholder": "--model opus",
      "value": "{{FormJson.Escape(existing?.Arguments)}}"
    },
    {
      "type": "Input.Text",
      "id": "name",
      "label": "Display name (optional — defaults to the command)",
      "placeholder": "Claude Code",
      "value": "{{FormJson.Escape(existing?.Name)}}"
    }
  ],
  "actions": [
    { "type": "Action.Submit", "title": "Save", "data": { "verb": "save" } }
  ]
}
""";
    }

    public override CommandResult SubmitForm(string inputs, string data)
    {
        var agent = AgentCliList.Create(
            FormJson.Read(inputs, "name"),
            FormJson.Read(inputs, "command"),
            FormJson.Read(inputs, "arguments"));

        if (agent is null)
        {
            return CommandResult.ShowToast("Enter the command to run, e.g. claude");
        }

        _service.SaveAgent(_existing, agent);
        return CommandResult.ShowToast(new ToastArgs
        {
            Message = _existing is null ? $"Added {agent.Name}" : $"Saved {agent.Name}",
            Result = CommandResult.GoBack(),
        });
    }
}
