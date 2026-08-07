#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;

namespace WorldMapStudio;

/// <summary>Thrown when an assertion fails. Surfaces as a <see cref="TestOutcome.Failed"/> result.</summary>
public sealed class TestAssertException : Exception
{
    public TestAssertException(string message) : base(message) { }
}

/// <summary>Thrown by <see cref="Assert.Skip"/> to end a test early as <see cref="TestOutcome.Skipped"/>.</summary>
public sealed class TestSkippedException : Exception
{
    public TestSkippedException(string reason) : base(reason) { }
}

/// <summary>
/// Assertion helpers for <see cref="EditorTestAttribute"/> tests. Each failure throws a
/// <see cref="TestAssertException"/> that the runner records with its message.
/// </summary>
public static class Assert
{
    /// <summary>Fails the test unconditionally.</summary>
    public static void Fail(string message = "Assert.Fail") =>
        throw new TestAssertException(message);

    /// <summary>Ends the test early, marking it skipped rather than passed or failed.</summary>
    public static void Skip(string reason = "Skipped") =>
        throw new TestSkippedException(reason);

    public static void IsTrue(bool condition, string? message = null)
    {
        if (!condition)
        {
            throw new TestAssertException(message ?? "Expected true but was false.");
        }
    }

    public static void IsFalse(bool condition, string? message = null)
    {
        if (condition)
        {
            throw new TestAssertException(message ?? "Expected false but was true.");
        }
    }

    public static void IsNull(object? value, string? message = null)
    {
        if (value is not null)
        {
            throw new TestAssertException(message ?? $"Expected null but was <{Describe(value)}>.");
        }
    }

    public static void IsNotNull(object? value, string? message = null)
    {
        if (value is null)
        {
            throw new TestAssertException(message ?? "Expected non-null but was null.");
        }
    }

    public static void AreEqual<T>(T expected, T actual, string? message = null)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new TestAssertException(
                message ?? $"Expected <{Describe(expected)}> but was <{Describe(actual)}>.");
        }
    }

    public static void AreNotEqual<T>(T notExpected, T actual, string? message = null)
    {
        if (EqualityComparer<T>.Default.Equals(notExpected, actual))
        {
            throw new TestAssertException(
                message ?? $"Expected a value other than <{Describe(notExpected)}>.");
        }
    }

    /// <summary>Passes when <paramref name="actual"/> is strictly greater than <paramref name="threshold"/>.</summary>
    public static void Greater<T>(T actual, T threshold, string? message = null) where T : IComparable<T>
    {
        if (actual.CompareTo(threshold) <= 0)
        {
            throw new TestAssertException(
                message ?? $"Expected <{Describe(actual)}> to be greater than <{Describe(threshold)}>.");
        }
    }

    /// <summary>Passes when two doubles are within <paramref name="tolerance"/> of each other.</summary>
    public static void AreApproximatelyEqual(double expected, double actual, double tolerance = 1e-6, string? message = null)
    {
        if (Math.Abs(expected - actual) > tolerance)
        {
            throw new TestAssertException(
                message ?? $"Expected <{expected}> ± {tolerance} but was <{actual}>.");
        }
    }

    /// <summary>Asserts that <paramref name="action"/> throws <typeparamref name="TException"/> and returns it.</summary>
    public static TException Throws<TException>(Action action, string? message = null) where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException expected)
        {
            return expected;
        }
        catch (Exception other)
        {
            throw new TestAssertException(
                message ?? $"Expected {typeof(TException).Name} but caught {other.GetType().Name}: {other.Message}");
        }

        throw new TestAssertException(
            message ?? $"Expected {typeof(TException).Name} but nothing was thrown.");
    }

    private static string Describe(object? value)
    {
        switch (value)
        {
            case null:
                return "null";
            case string s:
                return $"\"{s}\"";
            case IEnumerable enumerable and not string:
                List<string> parts = new();
                foreach (object? item in enumerable)
                {
                    parts.Add(item?.ToString() ?? "null");
                    if (parts.Count >= 8)
                    {
                        parts.Add("…");
                        break;
                    }
                }
                return $"[{string.Join(", ", parts)}]";
            default:
                return value.ToString() ?? "null";
        }
    }
}
