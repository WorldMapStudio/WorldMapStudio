using System.Linq;
using Godot;

namespace WorldMapStudio;

/// <summary>
/// Covers <see cref="SetProceduralModelFieldCommand{T}"/> — the reason a model field edit is not a
/// plain <see cref="SetFieldCommand{T}"/>. The authored data lives on the shared catalog model, so
/// the chunk impact has to be captured across every placement of it or an export never sees the
/// geometry move.
/// </summary>
public static class ProceduralModelChunkImpactTests
{
    [EditorTest(Category = "Procedural", Thread = TestThread.Background)]
    public static void Editing_a_shared_model_field_invalidates_every_placement()
    {
        var context = new EditorContext(new Node3D(), new Project { Name = "__wms_procedural_model_impact_test__" });
        var model = new ProceduralModel { RecordId = 1, Name = "Shared" };
        context.Catalog.Add(model);

        SceneEntity first = Place(context, model);
        SceneEntity second = Place(context, model);

        var command = new SetProceduralModelFieldCommand<string>(
            context.Procedural, model, "parameters", value => model.Parameters = value, model.Parameters, "radius=4");

        Assert.AreEqual(2, command.ChunkImpacts.Count, "both placements of the model must be reported");
        Assert.IsTrue(
            command.ChunkImpacts.All(impact => impact.Before!.Fingerprint != impact.After!.Fingerprint),
            "the edit moves each placement's content fingerprint, so ReduceImpacts keeps it");
        Assert.IsTrue(
            command.ChunkImpacts.Any(impact => impact.Entity == first) &&
            command.ChunkImpacts.Any(impact => impact.Entity == second));
    }

    [EditorTest(Category = "Procedural", Thread = TestThread.Background)]
    public static void A_model_with_no_loaded_placements_reports_no_impact()
    {
        var context = new EditorContext(new Node3D(), new Project { Name = "__wms_procedural_model_no_placement_test__" });
        var model = new ProceduralModel { RecordId = 2, Name = "Unused" };
        context.Catalog.Add(model);

        var command = new SetProceduralModelFieldCommand<string>(
            context.Procedural, model, "parameters", value => model.Parameters = value, model.Parameters, "radius=4");

        Assert.AreEqual(0, command.ChunkImpacts.Count);
    }

    private static SceneEntity Place(EditorContext context, ProceduralModel model)
    {
        var entity = new SceneEntity { Name = "Placement" };
        entity.AddComponent(new ProceduralComponent(context.Procedural) { ModelId = model.RecordId });
        context.Scene.Add(entity);
        return entity;
    }
}
