// Created: 2026-09-06
// Purpose: Provide a dependency-free test attribute and assertions.

namespace PIHarness.Tests;

[AttributeUsage(AttributeTargets.Method)]
internal sealed class TestCaseAttribute(string id, string goal) : Attribute
{
    public string Id { get; } = id;
    public string Goal { get; } = goal;
}

internal static class AssertEx
{
    public static void True(bool bCondition, string message)
    {
        if (!bCondition)
        {
            throw new InvalidOperationException(message);
        }
    }

    public static void False(bool bCondition, string message) => True(!bCondition, message);

    public static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{message}；期望={expected}，实际={actual}");
        }
    }

    public static void NotNull<T>(T? value, string message) where T : class
    {
        if (value is null)
        {
            throw new InvalidOperationException(message);
        }
    }
}
