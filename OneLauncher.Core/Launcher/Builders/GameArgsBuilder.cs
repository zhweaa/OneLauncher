using OneLauncher.Core.Global;
using OneLauncher.Core.Helper.Models;
using OneLauncher.Core.Launcher.Strategys;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OneLauncher.Core.Launcher;
public partial class LaunchCommandBuilder
{
    private IEnumerable<string> BuildGameArgs(string gamePath,IModArgStrategy? strategy)
    {
        if(loginUser == null) loginUser = Init.AccountManager.GetDefaultUser();
        List<string> Args = new List<string>(20);
        ConfigureAuthentication();
        string userType = loginUser.UserType switch
        {
            AccountType.Offline => "legacy",
            AccountType.Msa => "msa",
            AccountType.Yggdrasil => "mojang",
            _ => "mojang"
        };
        if (serverInfo != null)
        {
            Args.Add($"--server");
            Args.Add($"\"{((ServerInfo)serverInfo).Ip}\"");
            Args.Add($"--port");
            Args.Add($"\"{((ServerInfo)serverInfo).Port}\"");
            if (new Version(versionId) > new Version("1.20"))
            {
                Args.Add($"--quickPlayMultiplayer");
                Args.Add($"\"{serverInfo.Value.Ip}:{serverInfo.Value.Port}\"");
            }
        }
        // 添加基本的
        Args.AddRange([
            "--username",
            $"\"{loginUser.Name}\"",
            "--version",
            $"\"{versionId}\"",
            "--gameDir",
            $"\"{gamePath}\"",
            "--assetsDir",
            $"\"{(Path.Combine(basePath, "assets"))}\""
            ]);
        if (new Version(versionId) > new Version("1.7"))
            Args.AddRange([
                "--assetIndex",
                $"\"{versionInfo.GetAssetIndexVersion()}\"",
                "--uuid",
                $"\"{loginUser.uuid}\"",
                "--accessToken",
                $"\"{loginUser.AccessToken}\"",
                "--userType",
                $"\"{userType}\"",
                "--versionType",
                $"\"OneLauncher\"",
                "--userProperties {}"
            ]);
        else
            Args.Add($"--session \"{loginUser.AccessToken}\"");
        if(strategy != null)
            Args.AddRange(strategy.GetAdditionalGameArgs());
        if(extraGameArgs != null)
            Args.AddRange(extraGameArgs);
        return Args;
    }

    private void ConfigureAuthentication()
    {
        if (loginUser.UserType != AccountType.Yggdrasil) return;
        commandArgs.Add($"-javaagent:\"{Path.Combine(Init.InstalledPath, "authlib.jar")}\"={loginUser.YggdrasilInfo!.Value.AuthUrl}");
        commandArgs.Add($"-Dauthlibinjector.yggdrasil.prefetched={loginUser.YggdrasilInfo.Value.APIMetadata}");
    }
}
