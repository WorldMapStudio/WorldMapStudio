using System.Text;

namespace WorldMapStudio;

/// <summary>
/// Just enough of the TrueType/OpenType/TTC binary layout to read face names — no glyph parsing.
/// Used to classify a font file's header and, for a <c>.ttc</c> collection, find which face inside
/// it matches a requested family/subfamily (ImGui itself never sees this; it only rasterizes
/// through stb_truetype once <see cref="FontAtlasBuilder"/> picks a face).
/// </summary>
internal static class TrueTypeFontReader
{
    public readonly record struct FaceNames(string? Family, string? Subfamily, string? TypographicFamily, string? TypographicSubfamily);

    public const uint SfntTrueType = 0x00010000;
    public const uint SfntTrue = 0x74727565;
    public const uint SfntOtto = 0x4F54544F;
    public const uint SfntCollection = 0x74746366;

    private const uint NameTag = 0x6E616D65;

    public static uint ReadTag(byte[] data) => ReadU32(data, 0);

    public static int FaceCount(byte[] data)
    {
        if (ReadTag(data) != SfntCollection)
        {
            return 1;
        }

        return (int)ReadU32(data, 8);
    }

    public static FaceNames? ReadFaceNames(byte[] data, int faceIndex)
    {
        int sfntOffset = 0;
        if (ReadTag(data) == SfntCollection)
        {
            int numFonts = (int)ReadU32(data, 8);
            if (faceIndex < 0 || faceIndex >= numFonts)
            {
                return null;
            }

            sfntOffset = (int)ReadU32(data, 12 + faceIndex * 4);
        }

        ushort numTables = ReadU16(data, sfntOffset + 4);
        int? nameTableOffset = null;

        for (int i = 0; i < numTables; i++)
        {
            int recordOffset = sfntOffset + 12 + i * 16;
            if (recordOffset + 16 > data.Length)
            {
                break;
            }

            if (ReadU32(data, recordOffset) == NameTag)
            {
                nameTableOffset = (int)ReadU32(data, recordOffset + 8);
                break;
            }
        }

        return nameTableOffset is { } offset ? ParseNameTable(data, offset) : null;
    }

    private static FaceNames ParseNameTable(byte[] data, int tableOffset)
    {
        ushort count = ReadU16(data, tableOffset + 2);
        ushort stringAreaOffset = ReadU16(data, tableOffset + 4);
        string? family = null, subfamily = null, typoFamily = null, typoSubfamily = null;

        for (int i = 0; i < count; i++)
        {
            int recordOffset = tableOffset + 6 + i * 12;
            if (recordOffset + 12 > data.Length)
            {
                break;
            }

            ushort platformId = ReadU16(data, recordOffset);
            ushort nameId = ReadU16(data, recordOffset + 6);
            ushort length = ReadU16(data, recordOffset + 8);
            ushort offset = ReadU16(data, recordOffset + 10);

            // Windows platform, UTF-16BE strings; Mac Roman (platform 1) records are skipped.
            if (platformId != 3)
            {
                continue;
            }

            int stringStart = tableOffset + stringAreaOffset + offset;
            if (stringStart < 0 || stringStart + length > data.Length)
            {
                continue;
            }

            string value = Encoding.BigEndianUnicode.GetString(data, stringStart, length);
            switch (nameId)
            {
                case 1: family ??= value; break;
                case 2: subfamily ??= value; break;
                case 16: typoFamily ??= value; break;
                case 17: typoSubfamily ??= value; break;
            }
        }

        return new FaceNames(family, subfamily, typoFamily, typoSubfamily);
    }

    private static uint ReadU32(byte[] data, int offset) =>
        (uint)((data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3]);

    private static ushort ReadU16(byte[] data, int offset) =>
        (ushort)((data[offset] << 8) | data[offset + 1]);
}
