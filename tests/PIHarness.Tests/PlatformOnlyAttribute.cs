// 创建时间：2026-09-12
// 功能：声明测试用例只在指定平台有意义。
// 目的：让同一套测试在 Windows 与 Linux 运行，平台专属断言自动跳过而不是失败。

namespace PIHarness.Tests;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
internal sealed class PlatformOnlyAttribute(string platform) : Attribute
{
    public string Platform { get; } = platform;

    /// <summary>当前平台是否满足该限制；未知平台名一律视为不满足，避免静默执行错误用例。</summary>
    public bool IsSupported => Platform switch
    {
        "windows" => OperatingSystem.IsWindows(),
        "linux" => OperatingSystem.IsLinux(),
        "unix" => !OperatingSystem.IsWindows(),
        _ => false,
    };
}
