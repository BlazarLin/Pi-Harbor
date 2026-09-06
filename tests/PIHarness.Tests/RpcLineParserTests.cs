// Created: 2026-09-06
// Purpose: Verify robust separation of JSON protocol frames and plain diagnostics.

using PIHarness.Core.Rpc;

namespace PIHarness.Tests;

internal static class RpcLineParserTests
{
    [TestCase("TEST-05A", "RPC 解析忽略普通日志并保留后续有效 JSON")]
    public static void IgnoresNonJsonDiagnostics()
    {
        AssertEx.False(RpcLineParser.TryParse("[dashboard] endpoint ws://127.0.0.1", out _), "普通日志不应视为 JSON 帧");
        AssertEx.True(RpcLineParser.TryParse("{\"type\":\"agent_start\"}", out var document), "有效 JSON 必须被解析");
        using (document)
        {
            AssertEx.Equal("agent_start", document!.RootElement.GetProperty("type").GetString(), "事件类型应保持不变");
        }
    }
}
