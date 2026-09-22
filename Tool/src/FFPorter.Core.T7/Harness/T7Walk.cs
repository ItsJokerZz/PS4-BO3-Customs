using System.Text;

namespace FFPorter.Core.T7.Harness;

public enum T7WalkKind : byte
{
    BeginAsset = 1,
    Read = 2,
    String = 3,
    Register = 4,
    Pointer = 5,
    DeferQueued = 6,
    EndAsset = 7,
    Final = 8,
    Header = 9,
    DeferRead = 10,
    AliasSlot = 11,
    AliasSlotOf = 12,
    InlineRegistration = 13,
}

public enum T7PointerKind : byte
{
    Inline = 0,
    InlineAlias = 1,
    Packed = 2,
    PackedAlias = 3,
}

public readonly record struct T7BlockAddress(int Block, long Offset)
{
    public static readonly T7BlockAddress None = new(-1, 0);
    public ulong Packed => ((ulong)(uint)Block << 60 | (ulong)Offset) + 1;
    public override string ToString() => Block < 0 ? "none" : $"{Block}:{Offset:x}";
}

public sealed class T7WalkWriter : IDisposable
{
    public static ReadOnlySpan<byte> Magic => "T7WALK02"u8;
    private readonly BinaryWriter _writer;

    public T7WalkWriter(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        _writer = new BinaryWriter(new BufferedStream(File.Create(path), 1 << 20));
        _writer.Write(Magic);
    }

    public long Records { get; private set; }

    public void Header(string platform, long zoneBytes, IReadOnlyList<ulong> blockSizes)
    {
        Begin(T7WalkKind.Header);
        WriteString(platform);
        _writer.Write(zoneBytes);
        _writer.Write(blockSizes.Count);
        foreach (ulong size in blockSizes)
            _writer.Write(size);
    }

    public void BeginAsset(int index, int type, long filePos)
    {
        Begin(T7WalkKind.BeginAsset);
        _writer.Write(index);
        _writer.Write(type);
        _writer.Write(filePos);
    }

    public void EndAsset(int index, long filePos)
    {
        Begin(T7WalkKind.EndAsset);
        _writer.Write(index);
        _writer.Write(filePos);
    }

    public void Read(T7WalkKind kind, int asset, long fileOffset, long size, T7BlockAddress at)
    {
        Begin(kind);
        _writer.Write(asset);
        _writer.Write(fileOffset);
        _writer.Write(size);
        _writer.Write((sbyte)at.Block);
        _writer.Write(at.Offset);
    }

    public void Register(int asset, int type, string? name, T7BlockAddress header, long filePos)
    {
        Begin(T7WalkKind.Register);
        _writer.Write(asset);
        _writer.Write(type);
        WriteString(name ?? "");
        _writer.Write((sbyte)header.Block);
        _writer.Write(header.Offset);
        _writer.Write(filePos);
    }

    public void Pointer(long fieldFileOffset, T7PointerKind kind, T7BlockAddress target)
    {
        Begin(T7WalkKind.Pointer);
        _writer.Write(fieldFileOffset);
        _writer.Write((byte)kind);
        _writer.Write((sbyte)target.Block);
        _writer.Write(target.Offset);
    }

    public void AliasSlot(int asset, T7BlockAddress slot, T7BlockAddress header, int registration)
    {
        Begin(T7WalkKind.AliasSlotOf);
        _writer.Write(asset);
        _writer.Write((sbyte)slot.Block);
        _writer.Write(slot.Offset);
        _writer.Write((sbyte)header.Block);
        _writer.Write(header.Offset);
        _writer.Write(registration);
    }

    public void InlineRegistration(int asset, long fieldFileOffset, int registration)
    {
        Begin(T7WalkKind.InlineRegistration);
        _writer.Write(asset);
        _writer.Write(fieldFileOffset);
        _writer.Write(registration);
    }

    public void DeferQueued(int asset, long size, T7BlockAddress at)
    {
        Begin(T7WalkKind.DeferQueued);
        _writer.Write(asset);
        _writer.Write(size);
        _writer.Write((sbyte)at.Block);
        _writer.Write(at.Offset);
    }

    public void Final(long filePos, IReadOnlyList<long> highWater)
    {
        Begin(T7WalkKind.Final);
        _writer.Write(filePos);
        _writer.Write(highWater.Count);
        foreach (long top in highWater)
            _writer.Write(top);
    }

    private void Begin(T7WalkKind kind)
    {
        _writer.Write((byte)kind);
        Records++;
    }

    private void WriteString(string text)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        _writer.Write((ushort)bytes.Length);
        _writer.Write(bytes);
    }

    public void Flush() => _writer.Flush();

    public void Dispose() => _writer.Dispose();
}

public sealed class T7Walk
{
    public sealed class Asset
    {
        public int Index;
        public int Type;
        public long Start;
        public long End;
        public readonly List<Span> Reads = [];
        public readonly List<Registration> Registrations = [];
        public readonly List<Span> Deferred = [];
        public readonly List<(T7BlockAddress Slot, T7BlockAddress Header, int Registration)> AliasSlots = [];
        public string? Name => Registrations.Count > 0 ? Registrations[^1].Name : null;
    }

    public readonly record struct Span(T7WalkKind Kind, long FileOffset, long Size, T7BlockAddress At);
    public readonly record struct Registration(int Type, string Name, T7BlockAddress Header, long FilePos);
    public readonly record struct PointerField(long FieldFileOffset, T7PointerKind Kind, T7BlockAddress Target);

    public string Platform = "";
    public long ZoneBytes;
    public ulong[] BlockSizes = [];
    public readonly List<Asset> Assets = [];
    public readonly List<Span> Preamble = [];
    public readonly List<PointerField> Pointers = [];
    public readonly Dictionary<long, (int Asset, int Registration)> InlineRegistrations = [];
    public long FinalFilePos = -1;
    public long[] HighWater = [];

    public static T7Walk Read(string path)
    {
        using var reader = new BinaryReader(new BufferedStream(File.OpenRead(path), 1 << 20));
        if (!reader.ReadBytes(8).AsSpan().SequenceEqual(T7WalkWriter.Magic))
            throw new InvalidDataException($"{path} is not a T7 walk");
        var walk = new T7Walk();
        Asset? current = null;
        var deferOwners = new Queue<int>();
        while (reader.BaseStream.Position < reader.BaseStream.Length)
        {
            var kind = (T7WalkKind)reader.ReadByte();
            switch (kind)
            {
                case T7WalkKind.Header:
                    walk.Platform = ReadString(reader);
                    walk.ZoneBytes = reader.ReadInt64();
                    walk.BlockSizes = new ulong[reader.ReadInt32()];
                    for (int i = 0; i < walk.BlockSizes.Length; i++)
                        walk.BlockSizes[i] = reader.ReadUInt64();
                    break;
                case T7WalkKind.BeginAsset:
                    current = new Asset { Index = reader.ReadInt32(), Type = reader.ReadInt32(), Start = reader.ReadInt64() };
                    if (current.Index != walk.Assets.Count)
                        throw new InvalidDataException("Walk assets are not sequential");
                    walk.Assets.Add(current);
                    break;
                case T7WalkKind.EndAsset:
                {
                    int index = reader.ReadInt32();
                    walk.Assets[index].End = reader.ReadInt64();
                    current = null;
                    break;
                }
                case T7WalkKind.Read:
                case T7WalkKind.String:
                case T7WalkKind.DeferRead:
                {
                    int asset = reader.ReadInt32();
                    var span = new Span(kind, reader.ReadInt64(), reader.ReadInt64(), new T7BlockAddress(reader.ReadSByte(), reader.ReadInt64()));
                    if (kind == T7WalkKind.DeferRead)
                        walk.Assets[asset].Deferred.Add(span);
                    else if (asset < 0)
                        walk.Preamble.Add(span);
                    else
                        walk.Assets[asset].Reads.Add(span);
                    break;
                }
                case T7WalkKind.Register:
                {
                    int asset = reader.ReadInt32();
                    var registration = new Registration(reader.ReadInt32(), ReadString(reader), new T7BlockAddress(reader.ReadSByte(), reader.ReadInt64()), reader.ReadInt64());
                    if (asset >= 0)
                        walk.Assets[asset].Registrations.Add(registration);
                    break;
                }
                case T7WalkKind.Pointer:
                    walk.Pointers.Add(new PointerField(reader.ReadInt64(), (T7PointerKind)reader.ReadByte(), new T7BlockAddress(reader.ReadSByte(), reader.ReadInt64())));
                    break;
                case T7WalkKind.AliasSlot:
                case T7WalkKind.AliasSlotOf:
                {
                    int asset = reader.ReadInt32();
                    var slot = new T7BlockAddress(reader.ReadSByte(), reader.ReadInt64());
                    var header = new T7BlockAddress(reader.ReadSByte(), reader.ReadInt64());
                    int registration = kind == T7WalkKind.AliasSlotOf ? reader.ReadInt32() : -1;
                    walk.Assets[asset].AliasSlots.Add((slot, header, registration));
                    break;
                }
                case T7WalkKind.InlineRegistration:
                {
                    int asset = reader.ReadInt32();
                    long field = reader.ReadInt64();
                    walk.InlineRegistrations[field] = (asset, reader.ReadInt32());
                    break;
                }
                case T7WalkKind.DeferQueued:
                    reader.ReadInt32();
                    reader.ReadInt64();
                    reader.ReadSByte();
                    reader.ReadInt64();
                    break;
                case T7WalkKind.Final:
                    walk.FinalFilePos = reader.ReadInt64();
                    walk.HighWater = new long[reader.ReadInt32()];
                    for (int i = 0; i < walk.HighWater.Length; i++)
                        walk.HighWater[i] = reader.ReadInt64();
                    break;
                default:
                    throw new InvalidDataException($"Unknown walk record {kind} at {reader.BaseStream.Position - 1}");
            }
        }
        return walk;
    }

    private static string ReadString(BinaryReader reader) => Encoding.UTF8.GetString(reader.ReadBytes(reader.ReadUInt16()));
}
