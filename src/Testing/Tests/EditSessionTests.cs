using System;
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

    private sealed class RecordingStore : IEditSessionStore
    {
        public List<IEntity> Persisted { get; } = [];

        public void Persist(EditSession session) => Persisted.AddRange(session.Pinned);
    }

    [EditorTest(Category = "EditSession")]
    public static void Committing_persists_before_clearing()
    {
        // Persisting used to be a second call every caller made for itself, and the scripting API
        // never made it — so script edits were dropped instead of saved. Committing is one act now,
        // and this is what stops it splitting back apart.
        var entity = new FakeEntity();
        var store = new RecordingStore();
        var sessions = new EditSessionManager();
        sessions.BindStore(store);

        entity.Value = 4;
        sessions.Record(new SetValueCommand(entity, 0, 4));
        sessions.Commit();

        Assert.AreEqual(1, store.Persisted.Count, "the commit must have written the pinned entity");
        Assert.IsTrue(ReferenceEquals(entity, store.Persisted[0]));
        Assert.IsFalse(sessions.Active.IsDirty, "committing starts a fresh session");
    }

    [EditorTest(Category = "EditSession")]
    public static void Aborting_persists_nothing()
    {
        var entity = new FakeEntity();
        var store = new RecordingStore();
        var sessions = new EditSessionManager();
        sessions.BindStore(store);

        entity.Value = 4;
        sessions.Record(new SetValueCommand(entity, 0, 4));
        sessions.Abort();

        Assert.AreEqual(0, store.Persisted.Count);
        Assert.AreEqual(0, entity.Value, "aborting reverts the edit");
    }

    private sealed class DerivedEntity : IDerivedEntity
    {
        public EntityId Id { get; } = EntityId.Next();
    }

    private sealed class TouchDerivedCommand(DerivedEntity entity) : IEditCommand
    {
        public IReadOnlyList<IEntity> Targets { get; } = new IEntity[] { entity };

        public string Description => "Touch derived";

        public void Apply() { }

        public void Revert() { }
    }

    [EditorTest(Category = "EditSession")]
    public static void The_history_revision_moves_on_every_change()
    {
        // Systems that derive something from the edited entities watch this to know an edit landed,
        // so it has to move for undo and redo too, not only for recording.
        var entity = new FakeEntity();
        var history = new UndoHistory();
        int start = history.Revision;

        history.Record(new SetValueCommand(entity, 0, 1));
        int recorded = history.Revision;
        Assert.AreNotEqual(start, recorded);

        history.Undo();
        int undone = history.Revision;
        Assert.AreNotEqual(recorded, undone);

        history.Redo();
        Assert.AreNotEqual(undone, history.Revision);
    }

    [EditorTest(Category = "EditSession")]
    public static void A_derived_entity_cannot_be_edited()
    {
        // Derived entities are computed from other entities, so a command targeting one would undo
        // into a value the next rebuild discards. Loud rather than silently dropped.
        var session = new EditSession();

        Assert.Throws<InvalidOperationException>(() => session.Record(new TouchDerivedCommand(new DerivedEntity())));
        Assert.IsFalse(session.IsDirty, "nothing should have been pinned");
    }
}
