using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace FFPorter.Core.T7.Sound;

public sealed class T7SoundBank
{
    public const uint Magic = 0x23585532;
    public const int HeaderSize = 0x800;
    public const int Align = 0x800;
    public const int EntrySize = 36;
    public const int NameSize = 128;
    public const byte FormatMp3 = 5;
    public const byte FormatFlac = 8;
    public static readonly int[] FrameRates = [8000, 12000, 16000, 24000, 32000, 44100, 48000, 96000, 192000];

    public sealed class Entry
    {
        public uint Id;
        public byte[] Data = [];
        public uint FrameCount;
        public uint Unknown0C;
        public byte RateIndex;
        public byte Channels;
        public byte Looping;
        public byte Format;
        public byte[] Meta = new byte[8];
        public byte[] SourceChecksum = new byte[16];
        public string Name = "";
        public int SampleRate => RateIndex < FrameRates.Length ? FrameRates[RateIndex] : 0;
    }

    public uint Version = 15;
    public uint DependencyCount = 8;
    public uint Unknown1C = 2;
    public byte[] BankChecksum = new byte[16];
    public List<string> Dependencies = [];
    public string Zone = "";
    public string Platform = "pc";
    public string Language = "al";
    public byte PlatformByte = 0x0C;
    public List<Entry> Entries = [];

    public static T7SoundBank Parse(ReadOnlySpan<byte> file)
    {
        if (file.Length < HeaderSize || BinaryPrimitives.ReadUInt32LittleEndian(file) != Magic)
            throw new InvalidDataException("Not a T7 sound bank (2UX#)");
        var bank = new T7SoundBank
        {
            Version = BinaryPrimitives.ReadUInt32LittleEndian(file[4..]),
            DependencyCount = BinaryPrimitives.ReadUInt32LittleEndian(file[0x18..]),
            Unknown1C = BinaryPrimitives.ReadUInt32LittleEndian(file[0x1C..]),
            BankChecksum = file.Slice(0x38, 16).ToArray(),
            Zone = CString(file.Slice(0x258, 64)),
            Platform = CString(file.Slice(0x298, 8)),
            Language = Encoding.Latin1.GetString(file.Slice(0x2A0, 2)),
            PlatformByte = file[0x2A2],
        };
        for (int i = 0; i < 8; i++)
        {
            string dependency = CString(file.Slice(0x48 + 64 * i, 64));
            if (dependency.Length > 0)
                bank.Dependencies.Add(dependency);
        }
        int count = (int)BinaryPrimitives.ReadUInt32LittleEndian(file[0x14..]);
        long entries = (long)BinaryPrimitives.ReadUInt64LittleEndian(file[0x28..]);
        long sourceChecksums = (long)BinaryPrimitives.ReadUInt64LittleEndian(file[0x248..]);
        long names = (long)BinaryPrimitives.ReadUInt64LittleEndian(file[0x250..]);
        for (int i = 0; i < count; i++)
        {
            ReadOnlySpan<byte> record = file.Slice((int)entries + EntrySize * i, EntrySize);
            long offset = (long)BinaryPrimitives.ReadUInt64LittleEndian(record[16..]);
            uint size = BinaryPrimitives.ReadUInt32LittleEndian(record[4..]);
            bank.Entries.Add(new Entry
            {
                Id = BinaryPrimitives.ReadUInt32LittleEndian(record),
                Data = file.Slice((int)offset, (int)size).ToArray(),
                FrameCount = BinaryPrimitives.ReadUInt32LittleEndian(record[8..]),
                Unknown0C = BinaryPrimitives.ReadUInt32LittleEndian(record[12..]),
                RateIndex = record[24],
                Channels = record[25],
                Looping = record[26],
                Format = record[27],
                Meta = record.Slice(28, 8).ToArray(),
                SourceChecksum = file.Slice((int)sourceChecksums + 16 * i, 16).ToArray(),
                Name = CString(file.Slice((int)names + NameSize * i, NameSize)),
            });
        }
        return bank;
    }

    public byte[] Build()
    {
        using var output = new MemoryStream();
        output.Write(new byte[HeaderSize]);
        var offsets = new List<long>();
        foreach (Entry entry in Entries)
        {
            offsets.Add(output.Position);
            output.Write(entry.Data);
        }
        long entryTable = Pad(output);
        var record = new byte[EntrySize];
        for (int i = 0; i < Entries.Count; i++)
        {
            Entry entry = Entries[i];
            Array.Clear(record);
            BinaryPrimitives.WriteUInt32LittleEndian(record, entry.Id);
            BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(4), (uint)entry.Data.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(8), entry.FrameCount);
            BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(12), entry.Unknown0C);
            BinaryPrimitives.WriteUInt64LittleEndian(record.AsSpan(16), (ulong)offsets[i]);
            record[24] = entry.RateIndex;
            record[25] = entry.Channels;
            record[26] = entry.Looping;
            record[27] = entry.Format;
            entry.Meta.AsSpan(0, 8).CopyTo(record.AsSpan(28));
            output.Write(record);
        }
        long checksums = Pad(output);
        foreach (Entry entry in Entries)
            output.Write(MD5.HashData(entry.Data));
        long sourceChecksums = Pad(output);
        foreach (Entry entry in Entries)
            output.Write(entry.SourceChecksum.AsSpan(0, 16));
        long names = Pad(output);
        foreach (Entry entry in Entries)
            output.Write(FixedString(entry.Name, NameSize));
        long fileSize = output.Length;

        byte[] bytes = output.ToArray();
        Span<byte> header = bytes.AsSpan(0, HeaderSize);
        BinaryPrimitives.WriteUInt32LittleEndian(header, Magic);
        BinaryPrimitives.WriteUInt32LittleEndian(header[4..], Version);
        BinaryPrimitives.WriteUInt32LittleEndian(header[8..], EntrySize);
        BinaryPrimitives.WriteUInt32LittleEndian(header[12..], 16);
        BinaryPrimitives.WriteUInt32LittleEndian(header[16..], 64);
        BinaryPrimitives.WriteUInt32LittleEndian(header[20..], (uint)Entries.Count);
        BinaryPrimitives.WriteUInt32LittleEndian(header[24..], DependencyCount);
        BinaryPrimitives.WriteUInt32LittleEndian(header[28..], Unknown1C);
        BinaryPrimitives.WriteUInt64LittleEndian(header[32..], (ulong)fileSize);
        BinaryPrimitives.WriteUInt64LittleEndian(header[40..], (ulong)entryTable);
        BinaryPrimitives.WriteUInt64LittleEndian(header[48..], (ulong)checksums);
        BankChecksum.AsSpan(0, 16).CopyTo(header[0x38..]);
        for (int i = 0; i < Dependencies.Count && i < 8; i++)
            FixedString(Dependencies[i], 64).CopyTo(header[(0x48 + 64 * i)..]);
        BinaryPrimitives.WriteUInt64LittleEndian(header[0x248..], (ulong)sourceChecksums);
        BinaryPrimitives.WriteUInt64LittleEndian(header[0x250..], (ulong)names);
        FixedString(Zone, 64).CopyTo(header[0x258..]);
        FixedString(Platform, 8).CopyTo(header[0x298..]);
        Encoding.Latin1.GetBytes(Language.PadRight(2)[..2]).CopyTo(header[0x2A0..]);
        header[0x2A2] = PlatformByte;
        return bytes;
    }

    private static long Pad(MemoryStream output)
    {
        long aligned = (output.Length + Align - 1) / Align * Align;
        output.Write(new byte[aligned - output.Length]);
        return output.Position;
    }

    private static byte[] FixedString(string text, int size)
    {
        byte[] raw = Encoding.Latin1.GetBytes(text);
        if (raw.Length >= size)
            throw new InvalidDataException($"'{text}' does not fit a {size}-byte field");
        var field = new byte[size];
        raw.CopyTo(field, 0);
        return field;
    }

    private static string CString(ReadOnlySpan<byte> field)
    {
        int end = field.IndexOf((byte)0);
        return Encoding.Latin1.GetString(end < 0 ? field : field[..end]);
    }

    public static uint HashName(string name)
    {
        uint h = 5381;
        foreach (byte c in Encoding.Latin1.GetBytes(name))
            h = unchecked(h * 65599 + (uint)(c is >= (byte)'A' and <= (byte)'Z' ? c + 32 : c));
        return h;
    }

    public static string Ps4AssetName(string name)
    {
        string[] parts = name.Split('.');
        if (parts.Length >= 4 && parts[^1].Equals("snd", StringComparison.OrdinalIgnoreCase) && parts[^3].Length >= 3)
        {
            string tag = parts[^3];
            return string.Join('.', parts[..^3]) + $".{tag[0]}{tag[1]}89.orbis.snd";
        }
        return name;
    }
}
