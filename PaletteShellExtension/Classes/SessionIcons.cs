using Microsoft.CommandPalette.Extensions.Toolkit;

namespace PaletteShellExtension.Classes;

/// <summary>Segoe Fluent/MDL2 glyphs shared by the Agent sessions pages and commands. Kept as
/// escapes so the source stays ASCII.</summary>
internal static class SessionIcons
{
    public static readonly IconInfo Agent = new(""); // Robot
    public static readonly IconInfo Start = new(""); // Play
    public static readonly IconInfo Home = new(""); // Home
    public static readonly IconInfo Favorite = new(""); // FavoriteStar
    public static readonly IconInfo FavoriteFilled = new(""); // FavoriteStarFill
    public static readonly IconInfo Unfavorite = new(""); // Unfavorite
    public static readonly IconInfo Recent = new(""); // History
    public static readonly IconInfo Folder = new(""); // Folder
    public static readonly IconInfo Repo = new(""); // Code
    public static readonly IconInfo NewFolder = new(""); // NewFolder
    public static readonly IconInfo Add = new(""); // Add
    public static readonly IconInfo Edit = new(""); // Edit
    public static readonly IconInfo Delete = new(""); // Delete
    public static readonly IconInfo MoveUp = new(""); // Up
    public static readonly IconInfo MoveDown = new(""); // Down
    public static readonly IconInfo Restore = new(""); // UpdateRestore
    public static readonly IconInfo Warning = new(""); // Warning
}
