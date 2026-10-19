internal static partial class RegressionTestRunner
{
    static partial void TryForge(string[] args, ref Task? task)
    {
        if (args.Contains("--download-regression-tests", StringComparer.Ordinal))
            task = DownloadRegressionTests.RunAsync();
        else if (args.Contains("--processor-regression-tests", StringComparer.Ordinal))
            task = ForgeProcessorRegressionTests.RunAsync();
    }
}
