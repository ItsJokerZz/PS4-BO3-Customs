using System.Text.Json;
using System.Text.Json.Serialization;

namespace FFPorter.Core.Common.Fidelity;

public enum FidelityGrade
{
    Exact,
    Strong,
    Good,
    Approximate,
    Weak,
    Missing,
}

public enum FidelityDimension { Geometry, Textures, Materials, Lighting, Collision, Fx, Sound, Entities, Scripts, World }

public static class FidelityScale
{
    public static readonly FidelityDimension[] Dimensions = Enum.GetValues<FidelityDimension>();

    public static double Score(FidelityGrade grade) => grade switch
    {
        FidelityGrade.Exact => 100,
        FidelityGrade.Strong => 90,
        FidelityGrade.Good => 75,
        FidelityGrade.Approximate => 50,
        FidelityGrade.Weak => 20,
        _ => 0,
    };

    public static FidelityGrade GradeOf(double score, bool allExact) =>
        allExact ? FidelityGrade.Exact : score >= 88 ? FidelityGrade.Strong : score >= 70 ? FidelityGrade.Good : score >= 45 ? FidelityGrade.Approximate : FidelityGrade.Weak;

    public static string Name(FidelityGrade grade) => grade.ToString().ToLowerInvariant();

    public static string Name(FidelityDimension dimension) => dimension == FidelityDimension.Fx ? "FX" : dimension.ToString();

    public static string Description(FidelityDimension dimension) => dimension switch
    {
        FidelityDimension.Geometry => "Models, meshes and their streamed vertex data",
        FidelityDimension.Textures => "Images, texture combos and streamed image data",
        FidelityDimension.Materials => "Materials, technique sets and shaders",
        FidelityDimension.Lighting => "Primary lights, light descriptions, reflection probes and probe volumes",
        FidelityDimension.Collision => "Collision, physics, breakable glass and AI navigation",
        FidelityDimension.Fx => "Effects, tracers, beams, lasers, impact effects and lens flares",
        FidelityDimension.Sound => "Sound banks, aliases and sound tables",
        FidelityDimension.Entities => "Map entities, AI, characters, vehicles, weapons and animations",
        FidelityDimension.Scripts => "Scripts, raw files, tables, localized strings and UI data",
        _ => "World surfaces and visibility, skyboxes, streaming trees and movies",
    };

    public static double Weight(FidelityDimension dimension) => dimension switch
    {
        FidelityDimension.Geometry or FidelityDimension.Textures or FidelityDimension.Materials => 14,
        FidelityDimension.World or FidelityDimension.Scripts => 12,
        FidelityDimension.Lighting or FidelityDimension.Collision or FidelityDimension.Entities => 8,
        _ => 5,
    };

    public static int Percent(double score, bool allExact) => allExact ? 100 : (int)Math.Min(99, Math.Floor(score));
}

public static class FidelityStates
{
    public const string Running = "running", Done = "done", Failed = "failed", Cancelled = "cancelled";
}

public sealed class FidelitySnapshot
{
    public string Game { get; set; } = "";
    public string Map { get; set; } = "";
    public string State { get; set; } = FidelityStates.Running;
    public string Stage { get; set; } = "";
    public string Detail { get; set; } = "";
    public double Progress { get; set; }
    public double? Score { get; set; }
    public int? Percent { get; set; }
    public string? Grade { get; set; }
    public string Headline { get; set; } = "";
    public List<string> Problems { get; set; } = [];
    public List<FidelityDimensionReport> Dimensions { get; set; } = [];
    public double ElapsedSeconds { get; set; }
}

public sealed class FidelityDimensionReport
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public double? Score { get; set; }
    public int? Percent { get; set; }
    public string? Grade { get; set; }
    public long Units { get; set; }
    public bool Active { get; set; }
    public string Summary { get; set; } = "";
    public List<FidelityNote> Notes { get; set; } = [];
}

public sealed class FidelityNote
{
    public string Grade { get; set; } = "info";
    public string Text { get; set; } = "";
}

public static class FidelityProtocol
{
    public const string Prefix = "FFPORTER_FIDELITY ";

    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public static readonly JsonSerializerOptions IndentedJson = new(Json) { WriteIndented = true };

    public static string Format(FidelitySnapshot snapshot) => Prefix + JsonSerializer.Serialize(snapshot, Json);

    public static bool TryParse(string line, out FidelitySnapshot? snapshot)
    {
        snapshot = null;
        if (!line.StartsWith(Prefix, StringComparison.Ordinal))
            return false;
        try
        {
            snapshot = JsonSerializer.Deserialize<FidelitySnapshot>(line.AsSpan(Prefix.Length), Json);
        }
        catch (JsonException)
        {
            return false;
        }
        return snapshot != null;
    }

    public static FidelitySnapshot? Load(string path)
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<FidelitySnapshot>(File.ReadAllText(path), Json) : null;
        }
        catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static void Save(string path, FidelitySnapshot snapshot) => File.WriteAllText(path, JsonSerializer.Serialize(snapshot, IndentedJson));
}
