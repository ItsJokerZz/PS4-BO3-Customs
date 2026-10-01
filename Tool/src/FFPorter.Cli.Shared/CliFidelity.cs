using FFPorter.Core.Common.Fidelity;

namespace FFPorter.Cli;

public static class CliFidelity
{
    public static void Write(FidelitySnapshot report, TextWriter stdout)
    {
        stdout.WriteLine();
        stdout.WriteLine(report.Percent is int overall ? $"port fidelity: {overall}% ({report.Grade})" : "port fidelity: nothing scored");
        stdout.WriteLine($"  {report.Headline}");
        foreach (FidelityDimensionReport part in report.Dimensions)
        {
            string score = part.Percent is int percent ? $"{percent,3}%  {part.Grade,-12}"
                : report.State == FidelityStates.Done ? "  -   (not in map)  " : "  -   (not reached) ";
            stdout.WriteLine($"  {part.Name,-10} {score} {part.Summary}");
            foreach (FidelityNote note in part.Notes)
                stdout.WriteLine($"  {"",-10} {"",-18} [{note.Grade}] {note.Text}");
        }
        if (report.Steps is not { Count: > 0 } steps)
            return;
        stdout.WriteLine("steps, in the order they ran:");
        foreach (FidelityStepReport step in steps)
            stdout.WriteLine($"  {step.State,-9} {Elapsed(step.ElapsedSeconds),8}  {step.Title}");
    }

    private static string Elapsed(double seconds)
    {
        var time = TimeSpan.FromSeconds(seconds);
        return time.TotalHours >= 1 ? $"{(int)time.TotalHours}h {time.Minutes:00}m" : time.TotalMinutes >= 1 ? $"{(int)time.TotalMinutes}m {time.Seconds:00}s" : $"{time.TotalSeconds:0.0}s";
    }
}
