using System.Linq;
using Godot;

namespace WorldMapStudio;

public static class EnvironmentScriptApiTests
{
    private sealed class SphereSource : SceneComponent, IEnvironmentSource
    {
        public override string TypeId => "test-sphere-environment-source";

        public override string DisplayName => "Test Sphere Source";

        public bool IsGlobal => false;

        public float WeightAt(Vector3 worldPosition) =>
            EnvironmentFalloff.Sphere(worldPosition.DistanceTo(Owner!.Transform.Origin), 10.0f, 20.0f);

        public EnvironmentValues Evaluate(in EnvironmentTime time) => new() { AmbientColor = Colors.Red, FogEnd = 500.0f };

        public override SceneComponent Clone() => new SphereSource();
    }

    [EditorTest(Category = "Environment Script", Thread = TestThread.Main)]
    public static void Sample_blends_by_position_and_leaves_current_alone()
    {
        (EditorContext context, ScriptEngineHost host) = NewRig();
        double baseline = Number(host, "wms.environment.Current().AmbientColor[0].toString()");
        int version = context.Environments.Version;

        double inside = Number(host, "wms.environment.Sample(105, 0, 0).AmbientColor[0].toString()");
        double outside = Number(host, "wms.environment.Sample(1000, 0, 0).AmbientColor[0].toString()");
        double between = Number(host, "wms.environment.Sample(115, 0, 0).AmbientColor[0].toString()");

        Assert.AreApproximatelyEqual(1.0, inside, 1e-4, "inside the inner radius the source has full weight");
        Assert.AreApproximatelyEqual(0.28, outside, 1e-4, "beyond the outer radius the default ambient shows");
        Assert.IsTrue(between > outside && between < inside, "between the radii the blend is partial");
        Assert.AreEqual(version, context.Environments.Version, "sampling leaves the viewport's environment alone");
        Assert.AreApproximatelyEqual(baseline, Number(host, "wms.environment.Current().AmbientColor[0].toString()"));
    }

    [EditorTest(Category = "Environment Script", Thread = TestThread.Main)]
    public static void Active_lists_the_contributing_entities_with_weights()
    {
        (EditorContext context, ScriptEngineHost host) = NewRig();
        context.Environments.Update(new Vector3(105, 0, 0));

        Assert.AreEqual("1", host.Evaluate("wms.environment.Active().length.toString()"));
        Assert.AreEqual("1", host.Evaluate("wms.environment.Active()[0].Weight.toString()"));
    }

    private static double Number(ScriptEngineHost host, string code) =>
        double.Parse(host.Evaluate(code), System.Globalization.CultureInfo.InvariantCulture);

    private static (EditorContext, ScriptEngineHost) NewRig()
    {
        var context = new EditorContext(new Node3D(), new Project { Name = "__wms_environment_script_test__" });
        var entity = new MapSceneEntity { Map = context.Maps.CurrentMap };
        entity.AddComponent(new SphereSource());
        entity.Transform = new Transform3D(Basis.Identity, new Vector3(100, 0, 0));
        context.Scene.Add(entity);

        var host = new ScriptEngineHost([context.Scripting.EnvironmentScriptApi]);
        return (context, host);
    }
}
