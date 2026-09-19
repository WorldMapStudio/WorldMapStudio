using System.Linq;

namespace WorldMapStudio;

/// <summary>Covers <see cref="AxisConventionPresets"/> discovery and <see cref="AxisConvention.Apply"/>.</summary>
public static class AxisConventionPresetTests
{
    [EditorTest(Category = "Project")]
    public static void Built_in_godot_preset_is_registered_first()
    {
        IAxisConventionPreset[] presets = AppSystems.Instance.AxisConventionPresets.Presets.ToArray();

        Assert.IsTrue(presets.Length > 0);
        Assert.AreEqual("Godot (default)", presets[0].Name);
        Assert.IsTrue(presets[0].CreateConvention().IsGodotDefault);
    }

    [EditorTest(Category = "Project")]
    public static void Apply_overwrites_axes_of_an_existing_convention_in_place()
    {
        AxisConvention convention = AxisConvention.GodotDefault;
        AxisConvention preset = AxisConvention.Create(SignedAxis.PosX, SignedAxis.NegZ, SignedAxis.PosY);

        convention.Apply(preset);

        Assert.AreEqual(SignedAxis.PosX, convention.X);
        Assert.AreEqual(SignedAxis.NegZ, convention.Y);
        Assert.AreEqual(SignedAxis.PosY, convention.Z);
        Assert.IsTrue(convention.ToGodot(Godot.Vector3.Forward).IsEqualApprox(preset.ToGodot(Godot.Vector3.Forward)));
    }
}
