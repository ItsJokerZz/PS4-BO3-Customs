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
    }
}
