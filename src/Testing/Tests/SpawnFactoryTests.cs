using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Microsoft.EntityFrameworkCore;

namespace WorldMapStudio;

/// <summary>Covers the generic <see cref="ISpawnFactory"/> seam itself — the pieces with no real
/// implementation yet but that don't need one to be tested: that <see cref="Storage.Spawners"/>
/// discovers any registered factory regardless of concrete type, and that going through
/// <see cref="ISpawnFactory.Create"/> produces the same undo/session behaviour as every other creation
/// path in the editor.</summary>
public static class SpawnFactoryTests
{
    // A minimal ISpawnFactory: creation records a CreateEntityCommand into the context it was given,
    // exactly what a real factory is expected to do, without needing a database behind it.
    private sealed class FakeSpawnFactory : ISpawnFactory
    {
        public string SpawnKind => "Fake";

        public string PickerCatalogName => "Fake Catalog";

        public bool Handles(IEntity entity) => false;

        public Action Stage(DbContext context, IEntity entity) => () => { };

        public void StageDelete(DbContext context, IEntity entity) { }

        public SceneEntity Create(EditorContext context, MapId map, Transform3D transform, string key)
        {
            if (key.Length == 0)
            {
                throw new InvalidOperationException("key required");
            }

            var entity = new SceneEntity { Name = "Fake", Map = map, Transform = transform };
            var command = new CreateEntityCommand(context.Scene, entity);
            command.Apply();
            context.EditSessions.Record(command);
            return entity;
        }
    }

    // A minimal Storage: only HostedSubsystems needs a body, so this proves Spawners is a plain
    // Facet<ISpawnFactory>() projection rather than something that needs a live database to work.
    private sealed class FakeStorage(IEnumerable<ISubsystem> hosted) : Storage
    {
        public override string Name => "Fake";

        protected override IEnumerable<ISubsystem> HostedSubsystems => hosted;
    }

    [EditorTest(Category = "Spawn", Thread = TestThread.Background)]
    public static void Storage_spawners_discovers_a_registered_factory_by_interface_alone()
    {
        var storage = new FakeStorage([new FakeSpawnFactory()]);

        Assert.AreEqual(1, storage.Spawners.Count());
        Assert.AreEqual("Fake", storage.Spawners.Single().SpawnKind);
    }

    [EditorTest(Category = "Spawn", Thread = TestThread.Background)]
    public static void Storage_spawners_is_empty_when_nothing_implements_the_interface()
    {
        var storage = new FakeStorage([]);

        Assert.AreEqual(0, storage.Spawners.Count());
    }

    [EditorTest(Category = "Spawn", Thread = TestThread.Background)]
    public static void Create_adds_the_entity_and_records_an_undoable_command()
    {
        var context = new EditorContext(new Godot.Node3D(), new Project { Name = "__wms_spawn_factory_test__" });
        ISpawnFactory factory = new FakeSpawnFactory();

        var transform = new Transform3D(Basis.Identity, new Vector3(10, 20, 30));
        SceneEntity entity = factory.Create(context, new MapId(1), transform, "68");

        Assert.IsTrue(context.Scene.Contains(entity), "the created entity must be in the scene");
        Assert.IsTrue(context.EditSessions.Active.IsDirty, "creation through ISpawnFactory must pin the session like any other edit");
        Assert.IsTrue(entity.Transform.Origin.IsEqualApprox(transform.Origin));

        context.EditSessions.Undo();
        Assert.IsFalse(context.Scene.Contains(entity), "undo after a scripted spawn must remove it, like undo after any other creation");
    }

    [EditorTest(Category = "Spawn", Thread = TestThread.Background)]
    public static void Create_throws_loudly_for_a_malformed_key()
    {
        var context = new EditorContext(new Godot.Node3D(), new Project { Name = "__wms_spawn_factory_bad_key_test__" });
        ISpawnFactory factory = new FakeSpawnFactory();

        Assert.Throws<InvalidOperationException>(
            () => factory.Create(context, new MapId(1), Transform3D.Identity, ""));
    }
}
