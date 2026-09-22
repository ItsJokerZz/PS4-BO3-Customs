namespace FFPorter.Core.Common.Tools;

public static class ShaderDecompiler
{
    public const string Variable = "FFPORTER_SHADER_DECOMPILER";

    public const string RelativePath = "shader_decompiler/cmd_Decompiler.exe";

    public static string Locate(Workspace? workspace, Func<string, string?>? environment = null)
    {
        environment ??= Environment.GetEnvironmentVariable;
        return environment(Variable) is { Length: > 0 } path ? path : ToolData.Locate(workspace, RelativePath);
    }
}
