using System.Diagnostics;
using System.Globalization;

namespace FFPorter.Core.Common.Fidelity;

public sealed class FidelityTracker
{
    private sealed class NoteState
    {
        public required FidelityGrade? Grade { get; init; }
        public required Func<long, string?, string> Text { get; init; }
        public required int Order { get; init; }
        public long Count { get; set; }
        public string? Sample { get; set; }
    }

    private sealed class Part
    {
        public readonly long[] Units = new long[Enum.GetValues<FidelityGrade>().Length];
        public readonly Dictionary<string, (string Singular, string Plural, long Count, int Order)> Contents = new(StringComparer.Ordinal);
        public readonly Dictionary<string, NoteState> Notes = new(StringComparer.Ordinal);
        public bool Changed;

        public long Total => Units.Sum();

        public double? Score => Total == 0 ? null : Units.Select((count, grade) => count * FidelityScale.Score((FidelityGrade)grade)).Sum() / Total;

        public bool AllExact => Total > 0 && Units[(int)FidelityGrade.Exact] == Total;
    }

    private readonly Part[] _parts = FidelityScale.Dimensions.Select(_ => new Part()).ToArray();
    private readonly Action<FidelitySnapshot>? _emit;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly List<string> _problems = [];
    private long _lastEmit = long.MinValue / 2;
    private string _stage = "", _detail = "", _state = FidelityStates.Running, _headline = "";
    private double _progress;
    private int _order;

    public FidelityTracker(string game, string map, Action<FidelitySnapshot>? emit)
    {
        Game = game;
        Map = map;
        _emit = emit;
    }

    public string Game { get; }
    public string Map { get; }

    public string Subject { get; set; } = "map";

    public TimeSpan Interval { get; set; } = TimeSpan.FromMilliseconds(250);

    public string State => _state;

    public IReadOnlyList<string> Problems => _problems;

    public void Add(FidelityDimension dimension, FidelityGrade grade, long count = 1)
    {
        if (count <= 0)
            return;
        Part part = _parts[(int)dimension];
        part.Units[(int)grade] += count;
        part.Changed = true;
        Tick();
    }

    public void Downgrade(FidelityDimension dimension, FidelityGrade grade, long count = 1)
    {
        if (count <= 0 || grade == FidelityGrade.Exact)
            return;
        Part part = _parts[(int)dimension];
        long moved = Math.Min(count, part.Units[(int)FidelityGrade.Exact]);
        part.Units[(int)FidelityGrade.Exact] -= moved;
        part.Units[(int)grade] += count;
        part.Changed = true;
        Tick();
    }

    public void Content(FidelityDimension dimension, string singular, string plural, long count = 1)
    {
        if (count <= 0)
            return;
        Part part = _parts[(int)dimension];
        part.Contents[plural] = part.Contents.TryGetValue(plural, out var existing)
            ? (singular, plural, existing.Count + count, existing.Order)
            : (singular, plural, count, _order++);
    }

    public void Note(FidelityDimension dimension, string key, FidelityGrade? grade, long count, Func<long, string?, string> text, string? sample = null)
    {
        if (count <= 0)
            return;
        Part part = _parts[(int)dimension];
        if (!part.Notes.TryGetValue(key, out NoteState? note))
            part.Notes[key] = note = new NoteState { Grade = grade, Text = text, Order = _order++ };
        note.Count += count;
        note.Sample ??= sample;
        part.Changed = true;
    }

    public void Problem(string text)
    {
        if (_problems.Count < 200)
            _problems.Add(text);
        Tick();
    }

    public void Stage(string stage, string detail = "")
    {
        _stage = stage;
        _detail = detail;
        Emit();
    }

    public void Detail(string detail)
    {
        _detail = detail;
        Tick();
    }

    public void Progress(double fraction)
    {
        _progress = Math.Clamp(Math.Max(_progress, fraction), 0, 1);
        Tick();
    }

    public FidelitySnapshot Finish(string state, string headline)
    {
        _state = state;
        _headline = headline;
        if (state == FidelityStates.Done)
            _progress = 1;
        _stage = state switch { FidelityStates.Done => "Finished", FidelityStates.Cancelled => "Cancelled", _ => "Failed" };
        _detail = "";
        FidelitySnapshot snapshot = Snapshot();
        _emit?.Invoke(snapshot);
        _lastEmit = _clock.ElapsedMilliseconds;
        return snapshot;
    }

    public (double? Score, FidelityGrade? Grade) Overall()
    {
        double weighted = 0, weights = 0;
        bool allExact = true;
        foreach (FidelityDimension dimension in FidelityScale.Dimensions)
        {
            Part part = _parts[(int)dimension];
            if (part.Score is not double score)
                continue;
            weighted += score * FidelityScale.Weight(dimension);
            weights += FidelityScale.Weight(dimension);
            allExact &= part.AllExact;
        }
        if (weights == 0)
            return (null, null);
        double overall = weighted / weights;
        return (overall, FidelityScale.GradeOf(overall, allExact));
    }

    public string Headline(bool built, int problems)
    {
        if (!built)
            return problems == 0
                ? "The conversion stopped before the PS4 fastfile was written."
                : $"The PS4 fastfile was not written: {Count(problems, "problem", "problems")} stopped the conversion. The notes and the activity log say what to fix.";
        if (problems > 0)
            return $"The fastfile was written but failed its checks ({Count(problems, "problem", "problems")}), so it may not load. See the activity log.";
        return Overall().Grade switch
        {
            FidelityGrade.Exact => "Everything converted exactly and the fastfile passed the PS4 loader check. Verify in-game before treating it as done.",
            FidelityGrade.Strong => $"The fastfile built cleanly and nearly all of the {Subject} came across; the notes list what was approximated. Verify in-game before treating it as done.",
            FidelityGrade.Good => $"The fastfile built, but some of the {Subject} was approximated. Check the notes and test in-game.",
            FidelityGrade.Approximate => $"The fastfile built, but much of the {Subject} was approximated or is missing. Expect visible problems in-game.",
            FidelityGrade.Weak => $"The fastfile built, but most of the {Subject} did not come across faithfully. It is unlikely to look right in-game.",
            _ => "The fastfile built, but nothing in it could be scored.",
        };
    }

    public FidelitySnapshot Snapshot()
    {
        (double? score, FidelityGrade? grade) = Overall();
        var snapshot = new FidelitySnapshot
        {
            Game = Game,
            Map = Map,
            State = _state,
            Stage = _stage,
            Detail = _detail,
            Progress = _progress,
            Score = score is double s ? Math.Round(s, 2) : null,
            Percent = score is double p ? FidelityScale.Percent(p, grade == FidelityGrade.Exact) : null,
            Grade = grade is FidelityGrade g ? FidelityScale.Name(g) : null,
            Headline = _state == FidelityStates.Running ? $"Converting. Scores fill in as each part of the {Subject} is converted." : _headline,
            Problems = [.. _problems],
            ElapsedSeconds = Math.Round(_clock.Elapsed.TotalSeconds, 1),
        };
        foreach (FidelityDimension dimension in FidelityScale.Dimensions)
        {
            Part part = _parts[(int)dimension];
            double? partScore = part.Score;
            FidelityGrade? partGrade = partScore is double ps ? FidelityScale.GradeOf(ps, part.AllExact) : null;
            snapshot.Dimensions.Add(new FidelityDimensionReport
            {
                Name = FidelityScale.Name(dimension),
                Description = FidelityScale.Description(dimension),
                Score = partScore is double rounded ? Math.Round(rounded, 2) : null,
                Percent = partScore is double percent ? FidelityScale.Percent(percent, part.AllExact) : null,
                Grade = partGrade is FidelityGrade named ? FidelityScale.Name(named) : null,
                Units = part.Total,
                Active = part.Changed && _state == FidelityStates.Running,
                Summary = Summary(part),
                Notes = part.Notes.Values
                    .OrderBy(n => n.Grade is FidelityGrade severity ? -(int)severity : 1)
                    .ThenBy(n => n.Order)
                    .Select(n => new FidelityNote { Grade = n.Grade is FidelityGrade ng ? FidelityScale.Name(ng) : "info", Text = n.Text(n.Count, n.Sample) })
                    .ToList(),
            });
            part.Changed = false;
        }
        return snapshot;
    }

    public void Tick()
    {
        if (_emit != null && _clock.ElapsedMilliseconds - _lastEmit >= Interval.TotalMilliseconds)
            Emit();
    }

    public void Emit()
    {
        if (_emit == null)
            return;
        _lastEmit = _clock.ElapsedMilliseconds;
        _emit(Snapshot());
    }

    public static string Count(long count, string singular, string plural) =>
        count.ToString("N0", CultureInfo.InvariantCulture) + " " + (count == 1 ? singular : plural);

    private static string Summary(Part part)
    {
        var items = part.Contents.Values.OrderByDescending(c => c.Count).ThenBy(c => c.Order).ToList();
        if (items.Count == 0)
            return "";
        var shown = items.Take(items.Count > 4 ? 3 : 4).Select(c => Count(c.Count, c.Singular, c.Plural)).ToList();
        if (items.Count > 4)
            shown.Add(Count(items.Skip(3).Sum(c => c.Count), "other item", "other items"));
        return (shown.Count == 1 ? shown[0] : string.Join(", ", shown.Take(shown.Count - 1)) + " and " + shown[^1]) + ".";
    }
}
