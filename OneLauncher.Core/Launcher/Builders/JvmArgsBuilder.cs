using OneLauncher.Core.Global;
using OneLauncher.Core.Helper;
using OneLauncher.Core.Helper.Models;
using OneLauncher.Core.Launcher.Strategys;
using OneLauncher.Core.Minecraft.JsonModels;
using OneLauncher.Core.Mod.ModLoader.fabric.quilt;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace OneLauncher.Core.Launcher;

public partial class LaunchCommandBuilder
{
    private IEnumerable<string> BuildJvmArgs(IModArgStrategy? strategy)
    {
        string osName;
#if WINDOWS
        osName = "windows";
#elif MACOS
        osName = "osx";
#else
        osName = "linux"; 
#endif  
        // AI 写的，干什么用的我也不知道
        string arch = RuntimeInformation.OSArchitecture.ToString().ToLower();
        string nativesDirectory = Path.Combine(basePath, "versions", versionId, "natives");
        Directory.CreateDirectory(nativesDirectory);
        foreach (var subdirectory in new[] { "java", "jna", "lwjgl", "netty" })
            Directory.CreateDirectory(Path.Combine(nativesDirectory, subdirectory));

        var placeholders = new Dictionary<string, string>
        {
            // 创建占位符映射表 
            // 参考1.21.5.json
            // 手动加上引号
            { "natives_directory", nativesDirectory },
            { "launcher_name", "OneLauncher" },
            { "launcher_version", Init.ApplicationVersoin },
            { "classpath","\""+BuildClassPath(strategy)+"\"" },
            // 一些仅限NeoForge的
            { "version_name" , versionId},
            { "library_directory" ,"\""+Path.Combine(basePath, "libraries")+"\""},
            { "classpath_separator" , separator.ToString()}
        };
        // 处理1.13以前版本没有Arguments的情况
        if (versionInfo.info.Arguments == null)
        {
            return
                // 针对 1.6.x 版本不存在log4j2的情况
                [(versionInfo.GetLoggingConfigPath() != null ?
                $"-Dlog4j.configurationFile=\"{versionInfo.GetLoggingConfigPath()}\"" : ""),
                // 处理特定平台要求的参数
                (osName == "windows" ? "-XX:HeapDumpPath=MojangTricksIntelDriversForPerformance_javaw.exe_minecraft.exe.heapdump"
                : osName == "osx" ? "-XstartOnFirstThread" : "") ,
                // 标准JVM参数
                $"-Djava.library.path={placeholders["natives_directory"]}" ,
                $"-Djna.tmpdir={placeholders["natives_directory"]}" ,
                $"-Dorg.lwjgl.system.SharedLibraryExtractPath={placeholders["natives_directory"]}" ,
                $"-Dio.netty.native.workdir={placeholders["natives_directory"]}" ,
                $"-Dminecraft.launcher.brand={placeholders["launcher_name"]}" ,
                $"-Dminecraft.launcher.version={placeholders["launcher_version"]}" ,
                $"-cp {placeholders["classpath"]}"];
        }
        else
        {
            List<string> jvmArgs = null;
            if (strategy != null)
                jvmArgs = strategy.GetAdditionalJvmArgs().ToList();
            if (jvmArgs == null)
                jvmArgs = new List<string>(versionInfo.info.Arguments.Jvm.Count);
            foreach (var item in versionInfo.info.Arguments.Jvm)
            {
                // 判断是规则套字符串还是简单字符串
                if (item is string str)
                {
                    string replaced = Tools.ReplacePlaceholders(str, placeholders);
                    jvmArgs.Add(replaced);
                }
                else if (item is MinecraftArgument arg)
                {
                    if (EvaluateRules(arg.Rules, osName, arch))
                    {
                        if (arg.Value is string valStr)
                        {
                            string replaced = Tools.ReplacePlaceholders(valStr, placeholders);
                            jvmArgs.Add(replaced);
                        }
                        else if (arg.Value is List<string> valList)
                        {
                            foreach (var val in valList)
                            {
                                string replaced = Tools.ReplacePlaceholders(val, placeholders);
                                jvmArgs.Add($"\"{replaced}\"");
                            }
                        }
                    }
                }
            }
            if(new Version("1.17") < new Version(versionId) && new Version(versionId) < new Version("1.18"))
                jvmArgs.Add("-Dlog4j2.formatMsgNoLookups=true"); // 修复1.17-1.18版本的log4j2漏洞
            var loggingConfigPath = versionInfo.GetLoggingConfigPath();
            if(loggingConfigPath != null)
                jvmArgs.Add($"-Dlog4j.configurationFile=\"{loggingConfigPath}\"");
            return jvmArgs;
        }
    }
    private bool EvaluateRules(List<MinecraftRule> rules, string osName, string arch)
    {
        if (rules == null || rules.Count == 0) return true; // 没有规则，默认允许

        var finalAction = "disallow"; // 假设一个初始的 "默认" 动作

        foreach (var rule in rules)
        {
            bool conditionMet = false;
            if (rule.Os != null)
            {
                // 只有当os.name和os.arch都匹配或未定义时，规则才适用
                if ((rule.Os.Name == null || rule.Os.Name == osName) &&
                    (rule.Os.Arch == null || rule.Os.Arch == arch))
                {
                    conditionMet = true;
                }
            }
            else
            {
                conditionMet = true; // 如果没有os字段，规则总是适用
            }

            if (conditionMet)
            {
                finalAction = rule.Action; // 如果规则适用，更新最终动作
            }
        }

        return finalAction == "allow"; // 只有当最终的适用动作为 "allow" 时才返回true
    }
}
