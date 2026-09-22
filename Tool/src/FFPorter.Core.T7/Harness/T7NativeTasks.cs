using FFPorter.Core.Common.Native;

namespace FFPorter.Core.T7.Harness;

public static class T7NativeTasks
{
    public static void Register()
    {
        NativeTasks.Register(T7Ps4Loader.TaskName, T7Ps4Loader.Run);
        NativeTasks.Register(T7PcLoader.TaskName, T7PcLoader.Run);
    }
}
