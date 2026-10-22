using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace PaletteShellExtension.Pages;

/// <summary>A page that shows a single form — the shape every Agent sessions form needs, so
/// they share one wrapper instead of a page class each.</summary>
internal sealed partial class FormContentPage : ContentPage
{
    private readonly FormContent _form;

    public FormContentPage(string title, string id, IconInfo icon, FormContent form)
    {
        Title = title;
        Name = "Open";
        Id = id;
        Icon = icon;
        _form = form;
    }

    public override IContent[] GetContent() => [_form];
}
