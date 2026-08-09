using System;
using Godot;

namespace WorldMapStudio;

/// <summary>Camera control and viewport capture, exposed to JS as <c>wms.viewport</c>.</summary>
[Subsystem(nameof(ScriptingSystem))]
public sealed class ViewportScriptApi : IScriptModule
{
    private readonly EditorContext _context;

    public string Name => "viewport";

    public float Priority => 0f;

    public ViewportScriptApi(ScriptingSystem system)
    {
        _context = system.Context;
    }

    /// <summary>The camera's world position as [x, y, z].</summary>
    [ScriptFunction]
    public double[] GetPosition()
    {
        Vector3 position = _context.Focus.Position;
        return [position.X, position.Y, position.Z];
    }

    /// <summary>Teleports the camera, keeping its current orientation.</summary>
    [ScriptFunction]
    public void SetPosition(double x, double y, double z) =>
        _context.Focus.MoveTo(new Vector3((float)x, (float)y, (float)z));

    /// <summary>Backs the camera off and looks at the entity — the same "frame selected" move a click + focus does.</summary>
    [ScriptFunction]
    public void Focus(ScriptEntityHandle entity)
    {
        if (entity.Resolve() is SceneEntity scene)
        {
            _context.Focus.LookAt(scene.Transform.Origin);
        }
    }

    /// <summary>The live 3D view as a base64-encoded PNG, or null if the viewport hasn't rendered anything yet.</summary>
    [ScriptFunction]
    public string? Screenshot()
    {
        Image? image = _context.Maps.CaptureView?.Invoke();
        return image is null ? null : Convert.ToBase64String(image.SavePngToBuffer());
    }
}
