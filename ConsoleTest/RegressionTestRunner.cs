internal static partial class RegressionTestRunner
{
    internal static Task? TryRun(string[] args)
    {
        if (args.Length > 0 && args[0] == "--processor-test-child")
            return RunChildAsync(args);
        Task? task = null;
        TryPcl(args, ref task);
        TryForge(args, ref task);
        return task;
    }

    // 可选的部分方法让每组 PR 单独编译，不依赖尚未合并的测试文件。
    static partial void TryPcl(string[] args, ref Task? task);
    static partial void TryForge(string[] args, ref Task? task);

    private static async Task RunChildAsync(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        if (args[1] == "wait")
        {
            Console.WriteLine("child-ready");
            await Task.Delay(Timeout.Infinite);
        }
        Console.WriteLine(args.Length > 2 ? args[2] : "child-output");
        Console.Error.WriteLine("processor-final-error-中文");
        Environment.ExitCode = args[1] == "fail" ? 1 : 0;
    }
}
