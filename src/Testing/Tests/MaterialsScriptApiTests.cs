using System.Linq;
using Godot;

namespace WorldMapStudio;

public static class MaterialsScriptApiTests
{
    [EditorTest(Category = "Materials Script", Thread = TestThread.Main)]
    public static void Create_edit_undo_and_delete_a_preset()
    {
        (EditorContext context, ScriptEngineHost host) = NewRig();
        string standard = StandardMeshMaterial.TypeId;

        host.Evaluate($"var preset = wms.materials.CreatePreset('Brick', '{standard}')");
        Assert.AreEqual("Brick", host.Evaluate("preset.Name"));
        Assert.AreEqual(1, context.Materials().Count(p => p.Name == "Brick"));

        host.Evaluate("preset.Parameters = '{}'");
        host.Evaluate("preset.Name = 'Stone'");
        Assert.AreEqual("Stone", context.Materials().Single().Name);

        context.EditSessions.Undo();
        Assert.AreEqual("Brick", context.Materials().Single().Name);

        host.Evaluate("wms.materials.DeletePreset(preset)");
        Assert.AreEqual(0, context.Materials().Count());

        context.EditSessions.Undo();
        Assert.AreEqual(1, context.Materials().Count());
    }

    [EditorTest(Category = "Materials Script", Thread = TestThread.Main)]
    public static void Validate_reports_an_unknown_type()
    {
        (_, ScriptEngineHost host) = NewRig();
        string standard = StandardMeshMaterial.TypeId;
        host.Evaluate($"var preset = wms.materials.CreatePreset('Odd', '{standard}')");
        Assert.AreEqual("0", host.Evaluate("wms.materials.Validate().length.toString()"));

        host.Evaluate("preset.TypeId = 'no.such.type'");

        Assert.AreEqual("Error", host.Evaluate("wms.materials.Validate()[0].Severity"));
        Assert.IsTrue(host.Evaluate("wms.materials.Validate()[0].Message").Contains("no.such.type"));
    }

    [EditorTest(Category = "Materials Script", Thread = TestThread.Main)]
    public static void Types_list_the_declared_parameters()
    {
        (_, ScriptEngineHost host) = NewRig();

        Assert.AreEqual("true", host.Evaluate($"(wms.materials.Types().filter(t => t.Id == '{StandardMeshMaterial.TypeId}')[0].Parameters.length > 0).toString()"));
    }

    [EditorTest(Category = "Materials Script", Thread = TestThread.Main)]
    public static void Creating_a_preset_of_an_unknown_type_is_refused()
    {
        (EditorContext context, ScriptEngineHost host) = NewRig();

        string error = host.Evaluate("try { wms.materials.CreatePreset('X', 'no.such.type'); 'created' } catch (e) { e.message }");

        Assert.IsTrue(error.Contains("no.such.type"), error);
        Assert.AreEqual(0, context.Materials().Count());
    }

    private static System.Collections.Generic.IEnumerable<MeshMaterialPreset> Materials(this EditorContext context) =>
        context.MeshMaterials.Presets;

    private static (EditorContext, ScriptEngineHost) NewRig()
    {
        var context = new EditorContext(new Node3D(), new Project { Name = "__wms_materials_script_test__" });
        var host = new ScriptEngineHost([context.Scripting.MaterialsScriptApi]);
        return (context, host);
    }
}
