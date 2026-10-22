using Microsoft.CommandPalette.Extensions.Toolkit;
using PaletteShellExtension.Classes;

namespace PaletteShellExtension.Forms;

/// <summary>Adds a favorite session folder. The folder has to exist; <c>~</c> and
/// <c>%VARIABLES%</c> are expanded.</summary>
internal sealed partial class FavoriteFolderForm : FormContent
{
    private readonly AgentSessionService _service;

    public FavoriteFolderForm(AgentSessionService service)
    {
        _service = service;
        TemplateJson = $$"""
{
  "$schema": "http://adaptivecards.io/schemas/adaptive-card.json",
  "type": "AdaptiveCard",
  "version": "1.6",
  "body": [
    { "type": "TextBlock", "size": "Medium", "weight": "Bolder", "text": "Add favorite folder", "wrap": true },
    { "type": "TextBlock", "text": "Favorites are offered first when you start a session, and searched a few levels deep for project folders — a folder that holds all your repos makes a good favorite.", "wrap": true, "isSubtle": true },
    {
      "type": "Input.Text",
      "id": "folder",
      "label": "Folder path",
      "placeholder": "{{FormJson.Escape(@"C:\src or ~\code")}}",
      "isRequired": true,
      "errorMessage": "Enter a folder path"
    }
  ],
  "actions": [
    { "type": "Action.Submit", "title": "Add favorite", "data": { "verb": "add" } }
  ]
}
""";
    }

    public override CommandResult SubmitForm(string inputs, string data)
    {
        var folder = FolderPaths.TryNormalize(FormJson.Read(inputs, "folder"));
        if (folder is null)
        {
            return CommandResult.ShowToast(@"Enter a full folder path, like C:\src");
        }

        if (!FolderPaths.Exists(folder))
        {
            return CommandResult.ShowToast($"Folder not found: {folder}");
        }

        var added = _service.AddFavorite(folder);
        return CommandResult.ShowToast(new ToastArgs
        {
            Message = added ? $"Added {FolderPaths.DisplayName(folder)} to favorites" : "That folder is already a favorite",
            Result = CommandResult.GoBack(),
        });
    }
}
