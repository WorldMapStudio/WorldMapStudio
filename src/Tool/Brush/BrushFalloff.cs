using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace WorldMapStudio;

/// <summary>The brush weight curve: a smoothstep from the rim in to the hardness radius, then flat.</summary>
public static class BrushFalloff
{
    /// <summary>Test hook: forces the scalar row kernel so a test can diff it against the AVX2 one.</summary>
    internal static bool ForceScalar;

    /// <summary>Weight at <paramref name="t"/> (distance over radius), 0 outside the disc.</summary>
    public static float Weight(float t, float hardness)
    {
        if (t > 1.0f)
        {
            return 0.0f;
        }

        float s = 1.0f - (MathF.Max(t - hardness, 0.0f) * InverseSpan(hardness));
        return s * s * (3.0f - (2.0f * s));
    }

    /// <summary>
    /// Adds one brush row into <paramref name="values"/>: each pixel gets the falloff weight scaled by
    /// <paramref name="amountSign"/> (negative to erase), where the squared distance is within the unit
    /// disc. <paramref name="values"/> and <paramref name="columnDistSq"/> are the same length and index
    /// in lockstep.
    /// </summary>
    public static void AddRow(
        Span<float> values, ReadOnlySpan<float> columnDistSq, float dySq, float amountSign, float hardness, ref bool changed)
    {
        if (!ForceScalar && Avx2.IsSupported && Fma.IsSupported && values.Length >= Vector256<float>.Count)
        {
            AddRowAvx2(values, columnDistSq, dySq, amountSign, hardness, ref changed);
        }
        else
        {
            AddRowScalar(values, columnDistSq, dySq, amountSign, hardness, ref changed);
        }
    }

    // A hardness of exactly 1 has no ramp, so the span is floored to keep a hard disc finite.
    private static float InverseSpan(float hardness) => 1.0f / MathF.Max(1.0f - hardness, 1e-6f);

    private static void AddRowScalar(
        Span<float> values, ReadOnlySpan<float> columnDistSq, float dySq, float amountSign, float hardness, ref bool changed)
    {
        float inverseSpan = InverseSpan(hardness);
        for (int i = 0; i < values.Length; i++)
        {
            AddPixel(ref values[i], columnDistSq[i] + dySq, amountSign, hardness, inverseSpan, ref changed);
        }
    }

    private static void AddPixel(
        ref float value, float distSq, float amountSign, float hardness, float inverseSpan, ref bool changed)
    {
        if (distSq > 1.0f)
        {
            return;
        }

        float s = 1.0f - (MathF.Max(MathF.Sqrt(distSq) - hardness, 0.0f) * inverseSpan);
        float delta = amountSign * (s * s * (3.0f - (2.0f * s)));
        if (delta == 0.0f)
        {
            return;
        }

        float after = value + delta;
        if (after != value)
        {
            value = after;
            changed = true;
        }
    }

    // Exact Avx.Sqrt, the 3 - 2s term fused, and outside-the-disc lanes zeroed by AND-ing the delta
    // with the compare mask rather than a select. Reciprocal sqrt was measured no faster and NaNs at
    // the exact centre.
    private static void AddRowAvx2(
        Span<float> values, ReadOnlySpan<float> columnDistSq, float dySq, float amountSign, float hardness, ref bool changed)
    {
        ref float dst = ref MemoryMarshal.GetReference(values);
        ref float col = ref MemoryMarshal.GetReference(columnDistSq);
        int n = values.Length;

        float inverseSpan = InverseSpan(hardness);
        Vector256<float> zero = Vector256<float>.Zero;
        Vector256<float> one = Vector256.Create(1.0f);
        Vector256<float> two = Vector256.Create(2.0f);
        Vector256<float> three = Vector256.Create(3.0f);
        Vector256<float> dySqVec = Vector256.Create(dySq);
        Vector256<float> amountSignVec = Vector256.Create(amountSign);
        Vector256<float> hardnessVec = Vector256.Create(hardness);
        Vector256<float> inverseSpanVec = Vector256.Create(inverseSpan);

        int i = 0;
        for (; i <= n - Vector256<float>.Count; i += Vector256<float>.Count)
        {
            Vector256<float> distSq = Avx.Add(Vector256.LoadUnsafe(ref col, (nuint)i), dySqVec);
            Vector256<float> inside = Avx.CompareLessThanOrEqual(distSq, one);
            if (Avx.MoveMask(inside) == 0)
            {
                continue;
            }

            Vector256<float> ramp = Avx.Multiply(Avx.Max(Avx.Subtract(Avx.Sqrt(distSq), hardnessVec), zero), inverseSpanVec);
            Vector256<float> s = Avx.Subtract(one, ramp);
            Vector256<float> weight = Avx.Multiply(Avx.Multiply(s, s), Fma.MultiplyAddNegated(two, s, three));
            Vector256<float> delta = Avx.And(Avx.Multiply(weight, amountSignVec), inside);

            Vector256<float> before = Vector256.LoadUnsafe(ref dst, (nuint)i);
            Vector256<float> after = Avx.Add(before, delta);
            Vector256<float> moved = Avx.CompareNotEqual(after, before);
            if (Avx.MoveMask(moved) != 0)
            {
                Avx.BlendVariable(before, after, moved).StoreUnsafe(ref dst, (nuint)i);
                changed = true;
            }
        }

        for (; i < n; i++)
        {
            AddPixel(ref Unsafe.Add(ref dst, i), columnDistSq[i] + dySq, amountSign, hardness, inverseSpan, ref changed);
        }
    }
}
