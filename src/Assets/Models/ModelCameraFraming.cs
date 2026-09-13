using System;
using Godot;

namespace WorldMapStudio;

/// <summary>Shared "look at the whole model from a nice angle" camera placement, used by both the live
/// model preview and the thumbnail baker so they frame a model identically.</summary>
public static class ModelCameraFraming
{
    public static void Frame(Camera3D camera, Aabb bounds)
    {
        Vector3 center = bounds.Position + bounds.Size * 0.5f;
        float radius = Math.Max(0.75f, bounds.Size.Length() * 0.5f);
        Vector3 direction = new Vector3(1.0f, 0.65f, 1.0f).Normalized();
        camera.GlobalPosition = center + direction * radius * 2.6f;
        camera.LookAt(center, Vector3.Up);
    }
}
