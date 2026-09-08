using System.Text.Json.Nodes;
using GVector3 = Godot.Vector3;

namespace WorldMapStudio;

/// <summary>
/// A fly camera's full navigable state — where it sits, where it looks, how fast it moves — as a
/// plain value that a layout profile can snapshot and hand back on the next load.
/// </summary>
public readonly record struct CameraPose(GVector3 Position, float Yaw, float Pitch, float FlySpeed)
{
    public JsonObject ToJson() => new()
    {
        ["x"] = Position.X,
        ["y"] = Position.Y,
        ["z"] = Position.Z,
        ["yaw"] = Yaw,
        ["pitch"] = Pitch,
        ["speed"] = FlySpeed,
    };

    public static CameraPose? FromJson(JsonNode? node)
    {
        if (node is not JsonObject obj)
        {
            return null;
        }

        return new CameraPose(
            new GVector3(Read(obj, "x"), Read(obj, "y"), Read(obj, "z")),
            Read(obj, "yaw"),
            Read(obj, "pitch"),
            Read(obj, "speed"));
    }

    private static float Read(JsonObject obj, string key) =>
        obj.TryGetPropertyValue(key, out JsonNode? value) && value is not null ? value.GetValue<float>() : 0.0f;
}
