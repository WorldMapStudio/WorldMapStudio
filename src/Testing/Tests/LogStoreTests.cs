namespace WorldMapStudio;

/// <summary>
/// Covers how raw engine output turns into log rows: the level prefixes Godot uses, the indented
/// trace lines that must fold into the entry above rather than pile up as rows, and the bounded
/// buffer dropping the oldest lines without losing its per-level counts.
/// </summary>
public static class LogStoreTests
{
    [EditorTest(Category = "Log", Thread = TestThread.Background)]
    public static void Plain_lines_are_info()
    {
        var store = new LogStore();
        store.Ingest("[Database] Storage 'Editor' ready.");

        Assert.AreEqual(1, store.Entries.Count);
        Assert.AreEqual(LogLevel.Info, store.Entries[0].Level);
        Assert.AreEqual("[Database] Storage 'Editor' ready.", store.Entries[0].Message);
    }

    [EditorTest(Category = "Log", Thread = TestThread.Background)]
    public static void Prefixes_set_the_level_and_are_stripped()
    {
        var store = new LogStore();
        store.Ingest("WARNING: something is off");
        store.Ingest("ERROR: it broke");
        store.Ingest("SCRIPT ERROR: bad script");

        Assert.AreEqual(LogLevel.Warning, store.Entries[0].Level);
        Assert.AreEqual("something is off", store.Entries[0].Message);
        Assert.AreEqual(LogLevel.Error, store.Entries[1].Level);
        Assert.AreEqual("it broke", store.Entries[1].Message);
        Assert.AreEqual(LogLevel.Error, store.Entries[2].Level);

        (int info, int warning, int error) = store.Counts;
        Assert.AreEqual(0, info);
        Assert.AreEqual(1, warning);
        Assert.AreEqual(2, error);
    }

    [EditorTest(Category = "Log", Thread = TestThread.Background)]
    public static void Indented_lines_fold_into_the_entry_above()
    {
        var store = new LogStore();
        store.Ingest("ERROR: it broke");
        store.Ingest("   at: Thing.Method (res://Thing.cs:12)");
        store.Ingest("   C# backtrace (most recent call first):");
        store.Ingest("       [0] Thing.Method()");

        Assert.AreEqual(1, store.Entries.Count);
        Assert.IsTrue(store.Entries[0].Detail!.Contains("at: Thing.Method"));
        Assert.IsTrue(store.Entries[0].Detail!.Contains("[0] Thing.Method()"));
        Assert.IsTrue(store.Entries[0].Raw.StartsWith("it broke\n"));
    }

    [EditorTest(Category = "Log", Thread = TestThread.Background)]
    public static void An_indented_line_with_nothing_above_stands_alone()
    {
        var store = new LogStore();
        store.Ingest("   orphaned trace line");

        Assert.AreEqual(1, store.Entries.Count);
        Assert.AreEqual(LogLevel.Info, store.Entries[0].Level);
    }

    [EditorTest(Category = "Log", Thread = TestThread.Background)]
    public static void Blank_lines_are_dropped()
    {
        var store = new LogStore();
        store.Ingest("first");
        store.Ingest("");
        store.Ingest("   ");
        store.Ingest("second");

        Assert.AreEqual(2, store.Entries.Count);
    }

    [EditorTest(Category = "Log", Thread = TestThread.Background)]
    public static void The_buffer_is_bounded_and_keeps_its_counts()
    {
        var store = new LogStore(capacity: 100);
        for (int i = 0; i < 2000; i++)
        {
            store.Ingest(i % 2 == 0 ? $"line {i}" : $"WARNING: line {i}");
        }

        Assert.IsTrue(store.Entries.Count <= 100 + 512);

        (int info, int warning, int _) = store.Counts;
        Assert.AreEqual(store.Entries.Count, info + warning);
        foreach (LogEntry entry in store.Entries)
        {
            Assert.IsTrue(entry.Message.StartsWith("line "));
        }
    }

    [EditorTest(Category = "Log", Thread = TestThread.Background)]
    public static void Clear_resets_entries_and_counts()
    {
        var store = new LogStore();
        store.Ingest("ERROR: one");
        store.Ingest("two");
        store.Clear();

        Assert.AreEqual(0, store.Entries.Count);
        (int info, int warning, int error) = store.Counts;
        Assert.AreEqual(0, info + warning + error);
    }
}
