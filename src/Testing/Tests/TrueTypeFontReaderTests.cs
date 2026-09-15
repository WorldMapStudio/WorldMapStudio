using System;
using System.Collections.Generic;
using System.Text;

namespace WorldMapStudio;

/// <summary>Covers <see cref="TrueTypeFontReader"/> against hand-built minimal sfnt/TTC buffers —
/// just enough table-directory and <c>name</c> table structure to parse, no real glyph data.</summary>
public static class TrueTypeFontReaderTests
{
    [EditorTest(Category = "Style", Thread = TestThread.Background)]
    public static void Reads_family_and_subfamily_from_a_single_face()
    {
        byte[] data = BuildSfnt(("TestFamily", 1), ("Bold Italic", 2));

        Assert.AreEqual(1, TrueTypeFontReader.FaceCount(data));
        TrueTypeFontReader.FaceNames? names = TrueTypeFontReader.ReadFaceNames(data, 0);
        Assert.IsNotNull(names);
        Assert.AreEqual("TestFamily", names!.Value.Family);
        Assert.AreEqual("Bold Italic", names.Value.Subfamily);
    }

    [EditorTest(Category = "Style", Thread = TestThread.Background)]
    public static void Prefers_typographic_name_ids_when_present()
    {
        byte[] data = BuildSfnt(("Family Bold", 1), ("Regular", 2), ("Family", 16), ("Bold", 17));

        TrueTypeFontReader.FaceNames? names = TrueTypeFontReader.ReadFaceNames(data, 0);
        Assert.AreEqual("Family", names!.Value.TypographicFamily);
        Assert.AreEqual("Bold", names.Value.TypographicSubfamily);
    }

    [EditorTest(Category = "Style", Thread = TestThread.Background)]
    public static void Ttc_collection_exposes_each_faces_own_names()
    {
        byte[] face0 = BuildSfnt(("Foo", 1), ("Regular", 2));
        byte[] face1 = BuildSfnt(("Bar", 1), ("Italic", 2));
        byte[] data = BuildTtc(face0, face1);

        Assert.AreEqual(2, TrueTypeFontReader.FaceCount(data));
        Assert.AreEqual("Foo", TrueTypeFontReader.ReadFaceNames(data, 0)!.Value.Family);
        Assert.AreEqual("Bar", TrueTypeFontReader.ReadFaceNames(data, 1)!.Value.Family);
        Assert.AreEqual("Italic", TrueTypeFontReader.ReadFaceNames(data, 1)!.Value.Subfamily);
    }

    /// <summary>A single sfnt with one table directory entry pointing at a hand-built <c>name</c>
    /// table containing the given (value, nameId) records, all on the Windows/Unicode platform.</summary>
    private static byte[] BuildSfnt(params (string Value, ushort NameId)[] records)
    {
        List<byte> nameTable = [];
        // header: format(2) count(2) stringOffset(2)
        ushort recordAreaSize = (ushort)(6 + records.Length * 12);
        WriteU16(nameTable, 0);
        WriteU16(nameTable, (ushort)records.Length);
        WriteU16(nameTable, recordAreaSize);

        List<byte> stringArea = [];
        foreach ((string value, ushort nameId) in records)
        {
            byte[] utf16Be = Encoding.BigEndianUnicode.GetBytes(value);
            WriteU16(nameTable, 3); // platformID: Windows
            WriteU16(nameTable, 1); // encodingID: Unicode BMP
            WriteU16(nameTable, 0x0409); // languageID: en-US
            WriteU16(nameTable, nameId);
            WriteU16(nameTable, (ushort)utf16Be.Length);
            WriteU16(nameTable, (ushort)stringArea.Count);
            stringArea.AddRange(utf16Be);
        }

        nameTable.AddRange(stringArea);

        const int sfntHeaderSize = 12;
        const int tableRecordSize = 16;
        int nameTableOffset = sfntHeaderSize + tableRecordSize;

        List<byte> sfnt = [];
        WriteU32(sfnt, TrueTypeFontReader.SfntTrueType);
        WriteU16(sfnt, 1); // numTables
        WriteU16(sfnt, 0); // searchRange
        WriteU16(sfnt, 0); // entrySelector
        WriteU16(sfnt, 0); // rangeShift

        WriteU32(sfnt, 0x6E616D65); // 'name'
        WriteU32(sfnt, 0); // checksum, unused by the reader
        WriteU32(sfnt, (uint)nameTableOffset);
        WriteU32(sfnt, (uint)nameTable.Count);

        sfnt.AddRange(nameTable);
        return sfnt.ToArray();
    }

    private static byte[] BuildTtc(params byte[][] faces)
    {
        const int ttcHeaderSize = 12;
        List<byte> ttc = [];
        WriteU32(ttc, TrueTypeFontReader.SfntCollection);
        WriteU16(ttc, 2); // majorVersion
        WriteU16(ttc, 0); // minorVersion
        WriteU32(ttc, (uint)faces.Length);

        int offset = ttcHeaderSize + faces.Length * 4;
        List<int> offsets = [];
        foreach (byte[] face in faces)
        {
            offsets.Add(offset);
            offset += face.Length;
        }

        foreach (int faceOffset in offsets)
        {
            WriteU32(ttc, (uint)faceOffset);
        }

        foreach (byte[] face in faces)
        {
            ttc.AddRange(face);
        }

        return ttc.ToArray();
    }

    private static void WriteU16(List<byte> buffer, ushort value)
    {
        buffer.Add((byte)(value >> 8));
        buffer.Add((byte)value);
    }

    private static void WriteU32(List<byte> buffer, uint value)
    {
        buffer.Add((byte)(value >> 24));
        buffer.Add((byte)(value >> 16));
        buffer.Add((byte)(value >> 8));
        buffer.Add((byte)value);
    }
}
