using System.Text.Json.Serialization;

namespace PIHarness.Core.Models;

public sealed record PromptImage(
    [property: JsonPropertyName("data")] string Data,
    [property: JsonPropertyName("mimeType")] string MimeType)
{
    [JsonPropertyName("type")]
    public string Type => "image";
}
