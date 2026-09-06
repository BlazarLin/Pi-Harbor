// Created: 2026-09-06
// Purpose: Run PI-Harness tests without third-party test packages.

using System.Reflection;

namespace PIHarness.Tests;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        TestContext.Arguments = args;
        var testMethods = Assembly.GetExecutingAssembly()
            .GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            .Select(method => (Method: method, Attribute: method.GetCustomAttribute<TestCaseAttribute>()))
            .Where(item => item.Attribute is not null)
            .OrderBy(item => item.Attribute!.Id, StringComparer.Ordinal)
            .ThenBy(item => item.Method.Name, StringComparer.Ordinal)
            .ToArray();

        var nFailed = 0;
        foreach (var item in testMethods)
        {
            try
            {
                var result = item.Method.Invoke(null, null);
                if (result is Task task)
                {
                    await task.ConfigureAwait(false);
                }

                Console.WriteLine($"[通过] {item.Attribute!.Id}：{item.Attribute.Goal}");
            }
            catch (Exception exception)
            {
                nFailed++;
                var actual = exception is TargetInvocationException { InnerException: not null }
                    ? exception.InnerException
                    : exception;
                Console.WriteLine($"[失败] {item.Attribute!.Id}：{item.Attribute.Goal}");
                Console.WriteLine($"        {actual.GetType().Name}: {actual.Message}");
            }
        }

        Console.WriteLine($"测试完成：总计 {testMethods.Length}，通过 {testMethods.Length - nFailed}，失败 {nFailed}");
        return nFailed == 0 ? 0 : 1;
    }
}

internal static class TestContext
{
    public static IReadOnlyList<string> Arguments { get; set; } = [];
}
