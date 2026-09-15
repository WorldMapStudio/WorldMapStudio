using System.Numerics;
using System.Text.Json.Nodes;

namespace WorldMapStudio;

/// <summary>A numeric <c>ImGuiStyle</c> field value: either a plain float (e.g. <c>WindowRounding</c>)
/// or an [x, y] pair (e.g. <c>FramePadding</c>).</summary>
public readonly struct StyleVarValue
{
    public float X { get; }
    public float Y { get; }
    public bool IsVector { get; }

    private StyleVarValue(float x, float y, bool isVector)
    {
        X = x;
        Y = y;
        IsVector = isVector;
    }

    public static StyleVarValue Scalar(float value) => new(value, value, false);
    public static StyleVarValue Vector(float x, float y) => new(x, y, true);
    public static StyleVarValue Vector(Vector2 value) => new(value.X, value.Y, true);

    public float AsFloat() => X;
    public Vector2 AsVector2() => new(X, Y);

    public static StyleVarValue Parse(JsonNode? node, string path, System.Collections.Generic.List<StyleProblem> problems)
    {
        switch (node)
        {
            case JsonValue value when value.TryGetValue(out float f):
                return Scalar(f);

            case JsonArray { Count: 2 } array when TryReadNumber(array[0], out float x) && TryReadNumber(array[1], out float y):
                return Vector(x, y);

            default:
                problems.Add(new StyleProblem(path, $"Expected a number or [x, y], got '{node}'."));
                return Scalar(0f);
        }
    }

    private static bool TryReadNumber(JsonNode? node, out float value)
    {
        value = 0f;
        return node is JsonValue v && v.TryGetValue(out value);
    }

    public JsonNode ToJson() => IsVector
        ? new JsonArray(JsonValue.Create(X), JsonValue.Create(Y))
        : JsonValue.Create(X);
}
