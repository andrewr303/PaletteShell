using Microsoft.CommandPalette.Extensions.Toolkit;
using PaletteShellExtension.Classes;

namespace PaletteShellExtension.Forms;

/// <summary>Sets the default session folder, or clears it (blank) to go back to the user folder.</summary>
internal sealed partial class DefaultSessionFolderForm : FormContent
{
    private readonly AgentSessionService _service;

    public DefaultSessionFolderForm(AgentSessionService service)
    {
        _service = service;
        var userFolder = FolderPaths.UserFolder;
        TemplateJson = $$"""
{
  "$schema": "http://adaptivecards.io/schemas/adaptive-card.json",
  "type": "AdaptiveCard",
  "version": "1.6",
  "body": [
    { "type": "TextBlock", "size": "Medium", "weight": "Bolder", "text": "Default session folder", "wrap": true },
    { "type": "TextBlock", "text": "{{FormJson.Escape($"Sessions start here when you don't pick a folder, and it's always searched for project folders. Leave blank to use your user folder ({userFolder}).")}}", "wrap": true, "isSubtle": true },
    {
      "type": "Input.Text",
      "id": "folder",
      "label": "Folder path",
      "placeholder": "{{FormJson.Escape(userFolder)}}",
      "value": "{{FormJson.Escape(service.ConfiguredDefaultFolder)}}"
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
        var raw = FormJson.Read(inputs, "folder");
        if (string.IsNullOrWhiteSpace(raw))
        {
            _service.SetDefaultFolder(null);
            return CommandResult.ShowToast(new ToastArgs { Message = "Sessions now start in your user folder by default", Result = CommandResult.GoBack() });
        }

        var folder = FolderPaths.TryNormalize(raw);
        if (folder is null)
        {
            return CommandResult.ShowToast(@"Enter a full folder path, like C:\src");
        }

        if (!FolderPaths.Exists(folder))
        {
            return CommandResult.ShowToast($"Folder not found: {folder}");
        }

        _service.SetDefaultFolder(folder);
        return CommandResult.ShowToast(new ToastArgs
        {
            Message = $"Sessions now start in {FolderPaths.DisplayName(folder)} by default",
            Result = CommandResult.GoBack(),
        });
    }
}
