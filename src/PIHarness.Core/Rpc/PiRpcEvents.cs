// Created: 2026-09-06
// Purpose: Define pi process startup, protocol event, and failure contracts.

using System.Text.Json;

namespace PIHarness.Core.Rpc;

public sealed record PiStartOptions(
    string WorkingDirectory,
    string? SessionPath = null,
    bool NoSession = false,
    bool Offline = false,
    string? SessionDirectory = null);

public sealed record PiLaunchResult(bool Found, string? PiCommandPath, string? ErrorMessage);

public sealed record PiRpcEvent(string Type, JsonElement Payload);

public sealed class PiRpcException(string message, Exception? innerException = null)
    : Exception(message, innerException);
