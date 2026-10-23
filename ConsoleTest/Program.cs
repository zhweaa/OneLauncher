using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using OneLauncher.Core.Global;

try
{
    if (args.Contains("--protocol")) return await ForgeProtocolChecks.RunAsync();
    if (args.Contains("--modpack-cleanup"))
    {
        int outputIndex = Array.IndexOf(args, "--output");
        var output = Path.GetFullPath(outputIndex >= 0 && outputIndex + 1 < args.Length
            ? args[outputIndex + 1] : "build/modpack-cleanup-20261023");
        Directory.CreateDirectory(output);
        AppBuilder.Configure<OneLauncher.App>().UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .SetupWithoutStarting();
        var checks = ModpackUiChecks.RunAsync(output);
        while (!checks.IsCompleted) Dispatcher.UIThread.RunJobs();
        await checks;
        return 0;
    }
    var services = await Init.Initialize();
    using var serviceProvider = services.BuildServiceProvider();
    var core = serviceProvider.GetRequiredService<UnitedCore>();
    return await ForgeIntegrationTests.RunAsync(core, args);
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception is OlanException olan ? $"FAIL {olan.Title}: {olan.Message}" : "FAIL " + exception);
    return 1;
}
