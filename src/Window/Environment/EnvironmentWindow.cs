using Godot;
using ImGuiNET;
using NVector2 = System.Numerics.Vector2;
using NVector4 = System.Numerics.Vector4;

namespace WorldMapStudio;

/// <summary>
/// Time-of-day controls and a live readout of the blended <see cref="EnvironmentValues"/> and its
/// contributing sources — the generic counterpart of a lighting tool's "current time" panel.
/// </summary>
[Subsystem(nameof(WindowManager))]
public sealed class EnvironmentWindow : Window
{
    public override string? Category => "World";
    public override KeyboardShortcut DefaultShortcut => new(ImGuiKey.Y, ShortcutModifiers.Alt);

    private readonly WorldClock _clock;
    private readonly EnvironmentSystem _environments;
    private readonly SelectionSystem _selection;

    public EnvironmentWindow(WindowManager manager)
        : base("Environment", defaultSize: new NVector2(380.0f, 520.0f))
    {
        _clock = manager.Context.Clock;
        _environments = manager.Context.Environments;
        _selection = manager.Context.Selection;
    }

    protected override void DrawContent()
    {
        DrawClock();
        ImGui.Separator();
        DrawActiveSources();
        ImGui.Separator();
        DrawCurrent();
    }

    private void DrawClock()
    {
        ImGui.Text("Time of Day");
        ImGui.Separator();

        int totalMinutes = (int)(_clock.DayFraction * 24.0f * 60.0f);
        int hour = (totalMinutes / 60) % 24;
        int minute = totalMinutes % 60;

        ImGui.SetNextItemWidth(90.0f);
        if (ImGui.SliderInt("##hour", ref hour, 0, 23, $"{hour:00}h"))
        {
            SetClock(hour, minute);
        }

        ImGui.SameLine();
        ImGui.SetNextItemWidth(90.0f);
        if (ImGui.SliderInt("Time", ref minute, 0, 59, $"{minute:00}m"))
        {
            SetClock(hour, minute);
        }

        bool playing = _clock.Playing;
        if (ImGui.Checkbox("Playing", ref playing))
        {
            _clock.Playing = playing;
        }

        float secondsPerDay = _clock.SecondsPerDay;
        if (ImGui.DragFloat("Seconds / day", ref secondsPerDay, 1.0f, 1.0f, 86400.0f))
        {
            _clock.SecondsPerDay = secondsPerDay;
        }
    }

    private void SetClock(int hour, int minute) =>
        _clock.DayFraction = ((hour * 60.0f) + minute) / (24.0f * 60.0f);

    private void DrawActiveSources()
    {
        ImGui.Text("Active Sources");
        if (_environments.Active.Count == 0)
        {
            ImGui.TextDisabled(_environments.HasSources ? "None in range." : "No environment sources placed.");
            return;
        }

        foreach ((IEnvironmentSource source, float weight) in _environments.Active)
        {
            SceneEntity? owner = (source as SceneComponent)?.Owner;
            string label = owner?.DisplayName ?? source.GetType().Name;

            if (owner != null)
            {
                if (ImGui.Selectable($"{label}##{owner.Id.Value}"))
                {
                    _selection.Set(owner);
                }
            }
            else
            {
                ImGui.TextUnformatted(label);
            }

            ImGui.SameLine();
            ImGui.TextDisabled(source.IsGlobal ? "(global)" : $"weight {weight:0.00}");
        }
    }

    private void DrawCurrent()
    {
        EnvironmentValues values = _environments.Current;

        ImGui.Text("Current");
        DrawColorRow("Sun", values.SunColor);
        ImGui.Text($"Sun direction: {Format(values.SunDirection)}, energy {values.SunEnergy:0.00}");
        DrawColorRow("Ambient", values.AmbientColor);
        ImGui.Text($"Ambient energy: {values.AmbientEnergy:0.00}");

        ImGui.Separator();
        ImGui.Text($"Sky gradient ({values.SkyGradient.Count} stops)");
        foreach (SkyGradientStop stop in values.SkyGradient)
        {
            DrawColorRow($"{stop.ElevationDegrees:0}°", stop.Color);
        }

        ImGui.Separator();
        ImGui.Text(values.FogEnabled ? "Fog: on" : "Fog: off");
        if (values.FogEnabled)
        {
            DrawColorRow("Fog color", values.FogColor);
            ImGui.Text($"Start {values.FogStart:0.#}, End {values.FogEnd:0.#}, Curve {values.FogCurve:0.00}");
        }

        if (values.ViewDistance > 0.0f)
        {
            ImGui.Text($"View distance: {values.ViewDistance:0.#}");
        }

        if (values.SkyLayers.Count > 0)
        {
            ImGui.Separator();
            ImGui.Text("Sky layers");
            foreach (SkyLayer layer in values.SkyLayers)
            {
                ImGui.TextDisabled($"{layer.ModelPath} — weight {layer.Weight:0.00}, scale {layer.Scale:0.###}");
            }
        }

        if (values.Floats.Count > 0)
        {
            ImGui.Separator();
            ImGui.Text("Floats");
            foreach ((string key, float value) in values.Floats)
            {
                ImGui.Text($"{key}: {value:0.###}");
            }
        }

        if (values.Colors.Count > 0)
        {
            ImGui.Separator();
            ImGui.Text("Colors");
            foreach ((string key, Color color) in values.Colors)
            {
                DrawColorRow(key, color);
            }
        }
    }

    private static void DrawColorRow(string label, Color color)
    {
        var swatch = new NVector4(color.R, color.G, color.B, 1.0f);
        ImGui.ColorButton($"##{label}", swatch, ImGuiColorEditFlags.NoTooltip | ImGuiColorEditFlags.NoDragDrop, new NVector2(14.0f, 14.0f));
        ImGui.SameLine();
        ImGui.Text(label);
    }

    private static string Format(Vector3 v) => $"({v.X:0.00}, {v.Y:0.00}, {v.Z:0.00})";
}
