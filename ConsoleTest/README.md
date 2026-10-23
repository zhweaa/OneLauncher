# Forge / NeoForge 串行集成测试

在仓库根目录构建并运行：

```powershell
dotnet build ConsoleTest/ConsoleTest.csproj
dotnet ConsoleTest/bin/Debug/net10.0/ConsoleTest.dll --cases forge:1.14.3,forge:1.21.1,neoforge:1.21.1 --seconds 45
```

使用 `Init.Initialize`、`VersionInstallerService.InstallNewVersion` 和 `GameLaunchService.CreateLaunchService`。读取当前应用配置的游戏目录，实际安装、登记版本，并创建 `forge-test-*` 测试实例目录；不保存测试实例或离线测试账户到实例/账户列表。

每一项等待安装完成后才启动。观察时限结束后停止当前 Java 进程，等待启动会话结束再进入下一项。输出日志及 `results.json` 默认位于 `build/forge-expanded-20261022/integration`。`render-smoke-passed` 表示在观察期间出现渲染初始化日志且进程持续运行，结束时的 -1 是测试主动停止产生的退出码，不代表自然退出通过，也不替代主菜单/世界人工验收。

选项：

| 参数 | 用途 |
| --- | --- |
| `--cases forge:1.21.1,neoforge:1.21.1` | 指定串行测试清单 |
| `--seconds 45` | 单次启动观察时间 |
| `--install-only` | 只安装 |
| `--launch-only` | 启动已安装版本 |
| `--recommended` | Forge 优先官方推荐版 |
| `--allow-beta` | NeoForge 允许 beta |
| `--output <目录>` | 独立结果目录，避免覆盖前一轮结果 |
| `--info` | 初始化后输出当前游戏目录和 Java 大版本 |
| `--protocol` | 不初始化核心，读取保存的官方安装器作协议检查 |

默认测试清单覆盖本次报错的 Forge 版本、1.13.1 未发布场景，以及 NeoForge 1.20.1、1.21.1、26.2。下载来自现有配置所选择的来源；不要在同一游戏目录同时运行本测试和产品的下载/启动操作。

用户已确认以下 Forge 版本的下载及启动通过：1.7.10、1.8.9、1.12.2、1.15、1.16.5、1.18.2、1.19.2、1.20.1。此结论来自用户人工测试，独立于本程序的自动测试记录。

整合包取消与异常清理的离线检查：

```powershell
dotnet build ConsoleTest/ConsoleTest.csproj
dotnet ConsoleTest/bin/Debug/net10.0/ConsoleTest.dll --modpack-cleanup --output build/modpack-cleanup-20261023
```

该模式复用整合包侧栏回归夹具，不初始化实际启动器配置。包文件、解压目录、测试实例和截图均生成在输出目录内，检查取消或异常后清理临时文件与未完成的实例目录、登记保存失败回滚、重新下载重试、已有目录及成功实例保留，以及取消后保留最后的进度文字。
