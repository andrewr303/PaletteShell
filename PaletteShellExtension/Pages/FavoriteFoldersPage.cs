using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using PaletteShellExtension.Classes;
using PaletteShellExtension.Commands;
using PaletteShellExtension.Forms;
using System.Collections.Generic;

namespace PaletteShellExtension.Pages;

/// <summary>
/// Manages the favorite session folders: add, remove, reorder, make one the default. Enter on a
/// favorite opens <see cref="AgentPickerPage"/> to choose which agent to start there. The same
/// list is editable as text on the Settings page.
/// </summary>
internal sealed partial class FavoriteFoldersPage : ListPage
{
    private readonly AgentSessionService _service;

    public FavoriteFoldersPage(AgentSessionService service)
    {
        _service = service;
        Title = "Favorite folders";
        Name = "Open";
        Icon = SessionIcons.FavoriteFilled;
        Id = "AgentSessionFavorites";
        PlaceholderText = "Search favorite folders";
        service.Changed += (_, _) => RaiseItemsChanged();
    }

    /// <summary>The "add a favorite folder" form, shared by this page and the folder picker.</summary>
    public static FormContentPage AddFavoritePage(AgentSessionService service) =>
        new("Add favorite folder", "AgentSessionAddFavorite", SessionIcons.Add, new FavoriteFolderForm(service));

    public override IListItem[] GetItems()
    {
        var items = new List<IListItem>
        {
            new ListItem(AddFavoritePage(_service))
            {
                Title = "Add favorite folder",
                Subtitle = "Favorites are offered first when you start a session, and searched for project folders",
                Icon = SessionIcons.Add,
            },
        };

        var favorites = _service.Favorites;
        var defaultFolder = _service.DefaultFolder;
        for (var i = 0; i < favorites.Count; i++)
        {
            var folder = favorites[i];
            var exists = FolderPaths.Exists(folder);

            var commands = new List<CommandContextItem>
            {
                new(new ToggleFavoriteFolderCommand(_service, folder)),
            };
            if (i > 0)
            {
                commands.Add(new(new MoveFavoriteFolderCommand(_service, folder, -1)));
            }

            if (i < favorites.Count - 1)
            {
                commands.Add(new(new MoveFavoriteFolderCommand(_service, folder, +1)));
            }

            if (!FolderPaths.Same(folder, defaultFolder))
            {
                commands.Add(new(new SetDefaultSessionFolderCommand(_service, folder)));
            }

            commands.Add(new(new OpenFolderCommand(folder, "Open in File Explorer")));
            commands.Add(new(new CopyValueCommand(folder, "Copy path")));

            items.Add(new ListItem(new AgentPickerPage(_service, folder))
            {
                Title = SessionFolderItems.Title(folder),
                Subtitle = exists ? folder : $"⚠ Not found — {folder}",
                Icon = exists ? SessionIcons.FavoriteFilled : SessionIcons.Warning,
                MoreCommands = [.. commands],
            });
        }

        return [.. items];
    }
}
