
namespace FFPorter.Core.Common.Python;

public sealed class PyKeyError(string key) : Exception(PyPath.Repr(key))
{
    public string Key { get; } = key;
}

public class PyValueError(string message) : Exception(message);
