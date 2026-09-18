namespace WorldMapStudio;

/// <summary>Covers the runtime-supplied flag options behind <see cref="CatalogFieldSheet.Flags"/>'s
/// list overload — how a stored value is described against options that aren't an enum type.</summary>
public static class CatalogEnumFieldTests
{
    private static readonly (string Label, int Bit)[] Options =
    [
        ("Alpha", 1 << 0),
        ("Beta", 1 << 1),
        ("Omega", 1 << 21),
        ("Everyone", -1),
    ];

    [EditorTest(Category = "Catalog", Thread = TestThread.Background)]
    public static void Runtime_flag_options_are_ordered_by_bit()
    {
        EnumChoices choices = EnumChoices.ForFlags(Options);

        Assert.IsTrue(choices.IsFlags);
        Assert.AreEqual(4, choices.Ordered.Count);
        Assert.AreEqual("Alpha", choices.Ordered[0].Name);
        Assert.AreEqual("Omega", choices.Ordered[2].Name);
    }

    [EditorTest(Category = "Catalog", Thread = TestThread.Background)]
    public static void Describe_joins_the_set_options_and_hexes_the_leftover()
    {
        EnumChoices choices = EnumChoices.ForFlags(Options);

        Assert.AreEqual("Alpha | Beta", choices.Describe(3));
        Assert.AreEqual("Alpha | 0x4", choices.Describe(5));
        Assert.AreEqual("None", choices.Describe(0));
    }

    [EditorTest(Category = "Catalog", Thread = TestThread.Background)]
    public static void An_all_bits_option_matches_every_value()
    {
        EnumChoices choices = EnumChoices.ForFlags(Options);

        Assert.AreEqual("Alpha | Beta | Omega | Everyone", choices.Describe(-1));
    }
}
