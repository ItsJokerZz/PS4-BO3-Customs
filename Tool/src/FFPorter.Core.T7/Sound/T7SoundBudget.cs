namespace FFPorter.Core.T7.Sound;

public static class T7SoundBudget
{
    public const long LoadedBytes = 96L << 20;
    public const long AllocationOverhead = 0x2800;

    public sealed record Move(string LoadedBank, string StreamBank, int Count, long Bytes, long Before, long After, string Longest);

    public sealed record Result(List<Move> Moves, long Loaded, bool Fits);

    public static bool IsLoadedBank(string path) => path.EndsWith(".sabl", StringComparison.OrdinalIgnoreCase);

    public static string Language(string path) => Path.GetExtension(Path.GetFileNameWithoutExtension(path)).TrimStart('.');

    public static double Seconds(T7SoundBank.Entry entry) => entry.SampleRate > 0 ? (double)entry.FrameCount / entry.SampleRate : 0;

    public static Result Fit(IReadOnlyDictionary<string, T7SoundBank> banks, ISet<uint> streamed, long budget = LoadedBytes)
    {
        var slots = new List<Slot>();
        foreach ((string path, T7SoundBank bank) in banks)
        {
            if (!IsLoadedBank(path))
                continue;
            string pair = Path.ChangeExtension(path, ".sabs");
            T7SoundBank? stream = banks.FirstOrDefault(p => p.Key.Equals(pair, StringComparison.OrdinalIgnoreCase)).Value;
            slots.Add(new Slot(path, bank, stream, Language(path).Equals("all", StringComparison.OrdinalIgnoreCase)));
        }
        Slot? all = slots.FirstOrDefault(slot => slot.All);
        List<Slot> languages = slots.Where(slot => !slot.All).ToList();

        long Total() => (all?.Cost ?? 0) + (languages.Count > 0 ? languages.Max(slot => slot.Cost) : 0);

        while (Total() > budget)
        {
            Slot? language = languages.OrderByDescending(slot => slot.Cost).FirstOrDefault();
            T7SoundBank.Entry? fromAll = all?.Longest();
            T7SoundBank.Entry? fromLanguage = language?.Longest();
            Slot? source = fromLanguage != null && (fromAll == null || Seconds(fromLanguage) > Seconds(fromAll)) ? language
                : fromAll != null ? all : null;
            if (source == null)
                break;
            streamed.Add(source.Take().Id);
        }

        var moves = new List<Move>();
        foreach (Slot slot in slots.Where(slot => slot.Moved.Count > 0))
        {
            slot.Commit();
            moves.Add(new Move(slot.Path, Path.ChangeExtension(slot.Path, ".sabs"), slot.Moved.Count, slot.Original - slot.Payload,
                slot.Original, slot.Payload, slot.Moved[0].Name));
        }
        long total = Total();
        return new Result(moves, total, total <= budget);
    }

    private sealed class Slot
    {
        private readonly T7SoundBank _bank;
        private readonly T7SoundBank? _stream;
        private readonly List<T7SoundBank.Entry> _byLength;
        private int _next;

        public Slot(string path, T7SoundBank bank, T7SoundBank? stream, bool all)
        {
            Path = path;
            All = all;
            _bank = bank;
            _stream = stream;
            Original = bank.Entries.Sum(entry => (long)entry.Data.Length);
            Payload = Original;
            HashSet<uint>? streamIds = stream?.Entries.Select(entry => entry.Id).ToHashSet();
            _byLength = streamIds == null ? [] : bank.Entries
                .Where(entry => entry.Data.Length > 0 && !streamIds.Contains(entry.Id))
                .OrderByDescending(Seconds)
                .ThenByDescending(entry => entry.Data.Length)
                .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public string Path { get; }
        public bool All { get; }
        public long Original { get; }
        public long Payload { get; private set; }
        public long Cost => Payload > 0 ? Payload + AllocationOverhead : 0;
        public List<T7SoundBank.Entry> Moved { get; } = [];

        public T7SoundBank.Entry? Longest() => _next < _byLength.Count ? _byLength[_next] : null;

        public T7SoundBank.Entry Take()
        {
            T7SoundBank.Entry entry = _byLength[_next++];
            Moved.Add(entry);
            Payload -= entry.Data.Length;
            return entry;
        }

        public void Commit()
        {
            var moved = Moved.ToHashSet();
            _bank.Entries.RemoveAll(moved.Contains);
            _stream!.Entries.AddRange(Moved);
        }
    }
}
