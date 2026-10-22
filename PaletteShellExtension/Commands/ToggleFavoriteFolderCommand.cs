using Microsoft.CommandPalette.Extensions.Toolkit;
using PaletteShellExtension.Classes;

namespace PaletteShellExtension.Commands;

/// <summary>
/// Adds a folder to, or removes it from, the favorite session folders. Like
/// <see cref="TogglePinCommand"/>, the label reflects the state when the row was built; the owning
/// page refreshes itself from <see cref="AgentSessionService.Changed"/>.
/// </summary>
internal sealed partial class ToggleFavoriteFolderCommand(AgentSessionService service, string folder) : InvokableCommand
{
    private readonly bool _wasFavorite = service.IsFavorite(folder);

    public override string Name => _wasFavorite ? "Remove from favorites" : "Add to favorites";

    public override IconInfo Icon => _wasFavorite ? SessionIcons.Unfavorite : SessionIcons.Favorite;

    public override CommandResult Invoke()
    {
        var message = _wasFavorite
            ? service.RemoveFavorite(folder) ? "Removed from favorites" : "Not a favorite"
            : service.AddFavorite(folder) ? "Added to favorites" : "Already a favorite";
        return CommandResult.ShowToast(new ToastArgs { Message = message, Result = CommandResult.KeepOpen() });
    }
}
