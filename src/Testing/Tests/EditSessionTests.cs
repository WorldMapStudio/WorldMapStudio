using System.Collections.Generic;

namespace WorldMapStudio;

/// <summary>Covers the in-memory undo history and edit session semantics.</summary>
public static class EditSessionTests
{
    private sealed class FakeEntity : IEntity
    {
        public EntityId Id { get; } = EntityId.Next();

        public int Value;
    }

    // Mutates a fake entity's value; assumes it was already applied when recorded.
    private sealed class SetValueCommand(FakeEntity entity, int before, int after) : IEditCommand
    {
        public IReadOnlyList<IEntity> Targets { get; } = new IEntity[] { entity };

        public string Description => $"Set {before} -> {after}";

        public void Apply() => entity.Value = after;

        public void Revert() => entity.Value = before;
    }

    [EditorTest(Category = "EditSession")]
    public static void Undo_and_redo_round_trip()
    {
        var entity = new FakeEntity();
        var history = new UndoHistory();

        entity.Value = 5;
        history.Record(new SetValueCommand(entity, 0, 5));

        Assert.IsTrue(history.CanUndo);
        history.Undo();
        Assert.AreEqual(0, entity.Value);

        Assert.IsTrue(history.CanRedo);
        history.Redo();
        Assert.AreEqual(5, entity.Value);
    }

    [EditorTest(Category = "EditSession")]
    public static void Recording_clears_redo()
    {
        var entity = new FakeEntity();
        var history = new UndoHistory();

        history.Record(new SetValueCommand(entity, 0, 1));
        history.Undo();
        Assert.IsTrue(history.CanRedo);

        history.Record(new SetValueCommand(entity, 0, 2));
        Assert.IsFalse(history.CanRedo);
    }

    [EditorTest(Category = "EditSession")]
    public static void Session_pins_edited_entities()
    {
        var entity = new FakeEntity();
        var session = new EditSession();

        Assert.IsFalse(session.IsDirty);
        entity.Value = 3;
        session.Record(new SetValueCommand(entity, 0, 3));

        Assert.IsTrue(session.IsDirty);
        Assert.IsTrue(new List<IEntity>(session.Pinned).Contains(entity));
    }

    [EditorTest(Category = "EditSession")]
    public static void Commit_keeps_edits_and_clears_history()
    {
        var entity = new FakeEntity();
        var session = new EditSession();

        entity.Value = 7;
        session.Record(new SetValueCommand(entity, 0, 7));
        session.Commit();

        Assert.AreEqual(7, entity.Value);
        Assert.IsFalse(session.IsDirty);
        Assert.IsFalse(session.History.CanUndo);
    }

    [EditorTest(Category = "EditSession")]
    public static void Abort_reverts_all_edits()
    {
        var entity = new FakeEntity();
        var session = new EditSession();

        entity.Value = 1;
        session.Record(new SetValueCommand(entity, 0, 1));
        entity.Value = 2;
        session.Record(new SetValueCommand(entity, 1, 2));

        session.Abort();

        Assert.AreEqual(0, entity.Value);
        Assert.IsFalse(session.IsDirty);
        Assert.IsFalse(session.History.CanUndo);
    }
}
