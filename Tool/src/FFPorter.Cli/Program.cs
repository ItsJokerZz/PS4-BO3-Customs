using System.Text;

var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
using var stdout = new StreamWriter(Console.OpenStandardOutput(), encoding) { AutoFlush = true };
using var stderr = new StreamWriter(Console.OpenStandardError(), encoding) { AutoFlush = true };

Environment.SetEnvironmentVariable(FFPorter.Core.Common.Native.NativeProcess.HostVariable, Environment.ProcessPath);
FFPorter.Core.T7.Harness.T7NativeTasks.Register();

if (args.Length > 0 && args[0] == FFPorter.Core.Common.Native.NativeChild.Verb)
    return FFPorter.Core.Common.Native.NativeChild.Main(args[1..], stdout, stderr);

if (args.Length > 0 && args[0] == "t7")
    return FFPorter.Cli.T7Cli.Run(args[1..], stdout, stderr);
if (args.Length > 0)
    return FFPorter.Cli.T7Cli.Run(args, stdout, stderr);

stdout.Write(FFPorter.Cli.T7Cli.Usage);
return 2;
