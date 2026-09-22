using System.Collections.ObjectModel;
using FFPorter.Core.Common.Fidelity;

namespace FFPorter.Desktop.Models;

public sealed class FidelityView : Observable
{
    private string _map = "", _state = FidelityStates.Running, _stage = "", _detail = "", _headline = "", _grade = "", _percentText = "—", _elapsedText = "";
    private double _progress;
    private bool _hasScore;

    public FidelityView()
    {
        foreach (FidelityDimension dimension in FidelityScale.Dimensions)
            Dimensions.Add(new DimensionView(FidelityScale.Name(dimension), FidelityScale.Description(dimension)));
    }

    public ObservableCollection<DimensionView> Dimensions { get; } = [];
    public ObservableCollection<string> Problems { get; } = [];

    public string Map { get => _map; private set => Set(ref _map, value); }
    public string State { get => _state; private set { if (Set(ref _state, value)) { Raise(nameof(IsRunning)); Raise(nameof(StateLabel)); RaiseScore(); } } }
    public string Stage { get => _stage; private set => Set(ref _stage, value); }
    public string Detail { get => _detail; private set => Set(ref _detail, value); }
    public string Headline { get => _headline; private set => Set(ref _headline, value); }
    public string Grade { get => _grade; private set { if (Set(ref _grade, value)) RaiseScore(); } }
    public string PercentText { get => _percentText; private set => Set(ref _percentText, value); }
    public bool HasScore { get => _hasScore; private set { if (Set(ref _hasScore, value)) RaiseScore(); } }

    public string ScoreGrade => State is FidelityStates.Running or FidelityStates.Done ? Grade : "";

    public bool ShowGrade => HasScore && ScoreGrade.Length > 0;

    private void RaiseScore()
    {
        Raise(nameof(ScoreGrade));
        Raise(nameof(ShowGrade));
    }
    public double Progress { get => _progress; private set => Set(ref _progress, value); }
    public string ElapsedText { get => _elapsedText; private set => Set(ref _elapsedText, value); }
    public bool IsRunning => State == FidelityStates.Running;
    public bool HasProblems => Problems.Count > 0;

    public string ProblemSummary => Problems.Count switch
    {
        0 => "",
        1 => Problems[0],
        _ => $"{Problems.Count} problems. First: {Problems[0]}",
    };

    public string StateLabel => State switch
    {
        FidelityStates.Running => "Converting",
        FidelityStates.Done => "Finished",
        FidelityStates.Cancelled => "Cancelled",
        _ => "Failed",
    };

    public static FidelityView From(FidelitySnapshot snapshot)
    {
        var view = new FidelityView();
        view.Update(snapshot);
        return view;
    }

    public void Update(FidelitySnapshot snapshot)
    {
        Map = snapshot.Map;
        State = snapshot.State;
        Stage = snapshot.Stage;
        Detail = snapshot.Detail;
        Headline = snapshot.Headline;
        Grade = snapshot.Grade ?? "";
        HasScore = snapshot.Percent != null;
        PercentText = snapshot.Percent is int percent ? $"{percent}%" : "—";
        Progress = snapshot.Progress;
        ElapsedText = Elapsed(snapshot.ElapsedSeconds);
        for (int i = 0; i < Dimensions.Count && i < snapshot.Dimensions.Count; i++)
            Dimensions[i].Update(snapshot.Dimensions[i], snapshot.State);
        if (!Problems.SequenceEqual(snapshot.Problems))
        {
            Problems.Clear();
            foreach (string problem in snapshot.Problems)
                Problems.Add(problem);
            Raise(nameof(HasProblems));
            Raise(nameof(ProblemSummary));
        }
    }

    public void Interrupt(string state, string headline, IEnumerable<string>? problems = null)
    {
        State = state;
        Stage = state == FidelityStates.Cancelled ? "Cancelled" : "Failed";
        Detail = "";
        Headline = headline;
        foreach (DimensionView dimension in Dimensions)
            dimension.Settle();
        foreach (string problem in problems ?? [])
            Problems.Add(problem);
        Raise(nameof(HasProblems));
        Raise(nameof(ProblemSummary));
    }

    private static string Elapsed(double seconds)
    {
        var time = TimeSpan.FromSeconds(seconds);
        return time.TotalHours >= 1 ? $"{(int)time.TotalHours}h {time.Minutes}m" : time.TotalMinutes >= 1 ? $"{(int)time.TotalMinutes}m {time.Seconds}s" : $"{time.Seconds}s";
    }
}

public sealed class DimensionView : Observable
{
    private string _percentText = "—", _grade = "", _gradeLabel = "waiting", _summary = "";
    private double _scoreFraction;
    private bool _active, _hasUnits;

    public DimensionView(string name, string description)
    {
        Name = name;
        Description = description;
    }

    public string Name { get; }
    public string Description { get; }
    public ObservableCollection<NoteView> Notes { get; } = [];

    public string PercentText { get => _percentText; private set => Set(ref _percentText, value); }
    public string Grade { get => _grade; private set => Set(ref _grade, value); }
    public string GradeLabel { get => _gradeLabel; private set => Set(ref _gradeLabel, value); }
    public string Summary { get => _summary; private set { if (Set(ref _summary, value)) Raise(nameof(HasSummary)); } }
    public bool HasSummary => Summary.Length > 0;
    public double ScoreFraction { get => _scoreFraction; private set => Set(ref _scoreFraction, value); }
    public bool Active { get => _active; private set => Set(ref _active, value); }
    public bool HasUnits { get => _hasUnits; private set => Set(ref _hasUnits, value); }

    public void Update(FidelityDimensionReport report, string state)
    {
        HasUnits = report.Units > 0;
        Grade = report.Grade ?? "";
        GradeLabel = report.Grade ?? (state switch { FidelityStates.Running => "waiting", FidelityStates.Done => "not in map", _ => "not reached" });
        PercentText = report.Percent is int percent ? $"{percent}%" : "—";
        ScoreFraction = (report.Score ?? 0) / 100.0;
        Summary = report.Summary;
        Active = report.Active;
        if (Notes.Count != report.Notes.Count || Notes.Zip(report.Notes).Any(pair => pair.First.Text != pair.Second.Text || pair.First.Grade != pair.Second.Grade))
        {
            Notes.Clear();
            foreach (FidelityNote note in report.Notes)
                Notes.Add(new NoteView(note.Grade, note.Text));
        }
    }

    public void Settle()
    {
        Active = false;
        if (!HasUnits)
            GradeLabel = "not reached";
    }
}

public sealed record NoteView(string Grade, string Text);
