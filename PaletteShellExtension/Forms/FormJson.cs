using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PaletteShellExtension.Forms;

/// <summary>Helpers for the Adaptive Card JSON the Agent sessions forms build and receive.</summary>
internal static class FormJson
{
    /// <summary>Escapes a value for embedding inside a JSON string literal in a card template —
    /// quotes, backslashes (every Windows path), and control characters, leaving other text readable.</summary>
    public static string Escape(string? value) =>
        JsonEncodedText.Encode(value ?? "", JavaScriptEncoder.UnsafeRelaxedJsonEscaping).ToString();

    /// <summary>Reads a trimmed input value from the submitted inputs; null when absent.</summary>
    public static string? Read(string inputs, string id)
    {
        try
        {
            return JsonNode.Parse(inputs)?[id]?.ToString().Trim();
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
