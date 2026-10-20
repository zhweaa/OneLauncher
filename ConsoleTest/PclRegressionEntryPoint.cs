internal static partial class RegressionTestRunner
{
    static partial void TryPcl(string[] args, ref Task? task)
    {
        if (args.Contains("--launch-regression-tests", StringComparer.Ordinal))
            task = GameLaunchRegressionTests.RunAsync();
        else if (args.Contains("--pcl-import-regression-tests", StringComparer.Ordinal))
            task = PclImportRegressionTests.RunAsync();
        else if (args.Length == 3 && args[0] == "--pcl-import-real-test")
            task = PclImportRegressionTests.ImportRealAsync(args[1], args[2]);
        else if (args.Length > 0 && args[0] == "--import-argv-child")
        {
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(args.Skip(1).ToList(),
                OneLauncher.Core.Minecraft.JsonModels.MinecraftJsonContext.Default.ListString));
            task = Task.CompletedTask;
        }
    }
}
