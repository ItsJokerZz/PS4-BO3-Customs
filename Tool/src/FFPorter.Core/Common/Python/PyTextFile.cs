using System.Text;

namespace FFPorter.Core.Common.Python;

public static class PyTextFile
{
    public static void Write(string path, string text) => AtomicFile.WriteBytes(path, Encode(text));

    public static void WriteJson(string path, object? value, int? indent = 2) => Write(path, PyJson.Dumps(value, indent));

    public static byte[] Encode(string text)
    {
        string translated = text.Contains('\n') ? text.Replace("\n", "\r\n") : text;
        return Encoding.UTF8.GetBytes(translated);
    }
}
