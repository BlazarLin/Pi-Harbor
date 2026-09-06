// Created: 2026-09-06
// Purpose: Separate valid pi JSONL protocol frames from plain extension diagnostics.

using System.Text.Json;

namespace PIHarness.Core.Rpc;

public static class RpcLineParser
{
    public static bool TryParse(string line, out JsonDocument? document)
    {
        document = null;
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        try
        {
            document = JsonDocument.Parse(line.TrimEnd('\r'));
            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                return true;
            }

            document.Dispose();
            document = null;
            return false;
        }
        catch (JsonException)
        {
            document?.Dispose();
            document = null;
            return false;
        }
    }
}
