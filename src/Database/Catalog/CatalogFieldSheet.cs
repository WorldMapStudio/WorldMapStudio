using System;
using System.Collections.Generic;

namespace WorldMapStudio;

/// <summary>
/// One open entity's worth of filterable catalog fields for <see cref="ICatalogBrowser.DrawFields"/>.
/// The catalog-flavoured adapter over core's <see cref="FieldFilter"/>: it holds the
/// <c>context, tracker, entity, navigate</c> quartet every field call used to repeat and forwards the
/// typed widgets to <see cref="CatalogFieldDrawing"/>, while the filter gate, grouping and separator
/// behaviour all live in <see cref="FieldFilter"/>.
///
/// Adopt it by taking the extra <c>fieldFilter</c> parameter, constructing one, and calling
/// <see cref="Int"/>/<see cref="Text"/>/<see cref="Group"/>/... in place of the static helpers.
/// </summary>
internal sealed class CatalogFieldSheet
{
    private readonly FieldFilter _filter;
    private readonly EditorContext _context;
    private readonly CatalogEntity _entity;
    private readonly FieldEditTracker _tracker;
    private readonly Action<string, string> _navigate;

    public CatalogFieldSheet(EditorContext context, CatalogEntity entity, FieldEditTracker tracker,
        Action<string, string> navigate, string fieldFilter)
    {
        _filter = new FieldFilter(fieldFilter);
        _context = context;
        _entity = entity;
        _tracker = tracker;
        _navigate = navigate;
    }

    public bool Filtering => _filter.Filtering;

    public void Text(string label, string current, uint maxLength, Action<string> set)
    {
        if (_filter.Field(label))
        {
            CatalogFieldDrawing.DrawText(_context, _tracker, _entity, label, current, maxLength, set);
        }
    }

    public void TextMultiline(string label, string current, uint maxLength, Action<string> set, float height = 90.0f)
    {
        if (_filter.Field(label))
        {
            CatalogFieldDrawing.DrawTextMultiline(_context, _tracker, _entity, label, current, maxLength, set, height);
        }
    }

    public void Int(string label, int current, Action<int> set)
    {
        if (_filter.Field(label))
        {
            CatalogFieldDrawing.DrawInt(_context, _tracker, _entity, label, current, set);
        }
    }

    public void UInt(string label, uint current, Action<uint> set)
    {
        if (_filter.Field(label))
        {
            CatalogFieldDrawing.DrawUInt(_context, _tracker, _entity, label, current, set);
        }
    }

    public void Float(string label, float current, Action<float> set,
        float speed = 0.01f, float? min = null, float? max = null, float? width = null)
    {
        if (_filter.Field(label))
        {
            CatalogFieldDrawing.DrawFloat(_context, _tracker, _entity, label, current, set, speed, min, max, width);
        }
    }

    public void Byte(string label, byte current, Action<byte> set)
    {
        if (_filter.Field(label))
        {
            CatalogFieldDrawing.DrawByte(_context, _tracker, _entity, label, current, set);
        }
    }

    public void SByte(string label, sbyte current, Action<sbyte> set)
    {
        if (_filter.Field(label))
        {
            CatalogFieldDrawing.DrawSByte(_context, _tracker, _entity, label, current, set);
        }
    }

    public void UShort(string label, ushort current, Action<ushort> set)
    {
        if (_filter.Field(label))
        {
            CatalogFieldDrawing.DrawUShort(_context, _tracker, _entity, label, current, set);
        }
    }

    public void Link(string label, int current, Action<int> set, string targetCatalogName, Func<int, bool>? isEmpty = null)
    {
        if (_filter.Field(label))
        {
            CatalogFieldDrawing.DrawLink(_context, _tracker, _entity, label, current, set, targetCatalogName, _navigate, isEmpty);
        }
    }

    /// <summary>A searchable dropdown over an enum type; the column stays a raw <c>int</c>.</summary>
    public void Enum(string label, int current, Action<int> set, Type enumType)
    {
        if (_filter.Field(label))
        {
            CatalogEnumField.DrawEnum(_context, _entity, label, current, set, enumType);
        }
    }

    /// <summary>A checkbox-popup bitmask editor over a <c>[Flags]</c> enum type; the column stays a raw <c>int</c>.</summary>
    public void Flags(string label, int current, Action<int> set, Type enumType)
    {
        if (_filter.Field(label))
        {
            CatalogEnumField.DrawFlags(_context, _entity, label, current, set, enumType);
        }
    }

    /// <summary>A checkbox-popup bitmask editor over options supplied at draw time (label plus the bit
    /// each one sets); the column stays a raw <c>int</c>. For flag sets that data defines rather than an
    /// enum type.</summary>
    public void Flags(string label, int current, Action<int> set, IReadOnlyList<(string Label, int Bit)> options)
    {
        if (_filter.Field(label))
        {
            CatalogEnumField.DrawFlags(_context, _entity, label, current, set, options);
        }
    }

    /// <summary>Any custom widget(s) — a raw ImGui block, a bespoke sub-draw — filtered as one unit
    /// by <paramref name="label"/>.</summary>
    public void Custom(string label, Action body) => _filter.Field(label, body);

    /// <summary>A rule between field groups. Suppressed while a filter is active.</summary>
    public void Separator() => _filter.Separator();

    /// <summary>A collapsible section — see <see cref="FieldFilter.Group"/>.</summary>
    public void Group(string title, Action body) => _filter.Group(title, body);
}
