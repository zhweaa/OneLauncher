# 离线回归检查

该项目为可独立运行的检查程序，不属于主解决方案，不使用真实用户配置，不调用启动初始化或在线服务。

```powershell
dotnet restore tests/OneLauncher.RegressionTests/OneLauncher.RegressionTests.csproj
dotnet build tests/OneLauncher.RegressionTests/OneLauncher.RegressionTests.csproj --no-restore
dotnet tests/OneLauncher.RegressionTests/bin/Debug/net10.0/OneLauncher.RegressionTests.dll artifacts/settings-regression
```

最后一个参数为可选输出目录，默认 `artifacts/settings-regression`。测试配置保存在 `data/<unique-id>`，并输出三张 Headless + Skia 运行时渲染截图。程序遇到断言失败会以非零状态退出。

覆盖版本迁移、损坏文件保护、失败重试、并发版本登记、排序持久化、全局管理器保存、安装前目录校验、设置筛选、导航编辑及保存、主窗口可见性和启动 DI 依赖图。

测试通过反射访问内部 ViewModel 和迁移入口，以避免给生产代码增加公开测试接口；如果重命名这些内部类型，需要同步更新检查程序。完整启动图使用 `ValidateOnBuild`，实际实例化全部已注册工厂及六个无需网络的页面；主页和资源检索页的构造函数会启动在线任务，因此只验证其依赖图。

通知点击问题可单独运行，不执行其他回归场景：

```powershell
dotnet tests/OneLauncher.RegressionTests/bin/Debug/net10.0/OneLauncher.RegressionTests.dll build/notification-click-regression-20261023 --notification-click
```

该场景使用真实下载页 ViewModel、任务中心工厂和通知回调，检查普通通知、点击打开、重复打开、旧关闭回调、关闭命令、空页面及事件订阅释放。它在 Headless 环境直接执行通知的 OnClick，不启动真实登录、下载或主窗口初始化服务。

资源检索与共享安装侧栏可单独运行：

```powershell
dotnet tests/OneLauncher.RegressionTests/bin/Debug/net10.0/OneLauncher.RegressionTests.dll build/shader-ui-20261023 --shader-ui
```

该场景使用隔离实例配置、HTTP 替身和真实 AXAML，覆盖模组/光影下拉选择、搜索编码、旧请求覆盖防护、空结果、搜索错误与恢复、安装按钮绑定、共享 `InstallModPane`、光影依赖跳过、各加载器实例安装、模组校验、重复安装防护、进度终态、任务中心取消、模组元数据取消、关闭时取消、失败重试及空实例提示。使用 Headless + Skia 输出资源页面与安装侧栏的浅色/深色、运行、完成、取消、失败和空实例截图。

当前文件共享锁故障模拟针对 Windows 环境。没有覆盖真实下载、登录、联机和 Native AOT 发布。

整合包检索与独立安装侧栏可单独运行：

```powershell
dotnet tests/OneLauncher.RegressionTests/bin/Debug/net10.0/OneLauncher.RegressionTests.dll build/modpack-ui-20261023 --modpack-ui
```

该场景使用隔离配置、已登记基础版本、模拟 HTTP 和真实 AXAML，验证整合包搜索、不可定位网络流、MrpackParser 信息、预览不创建实例、显式安装、覆盖文件和清单文件导入、包文件复用、重复安装防护、取消与终态、关闭侧栏清理临时包、失败后重试，并输出深浅色及小窗口截图。基础游戏及自定义加载器的实际安装由既有实现负责，本场景未联网验证。

整合包检查现还覆盖返回实例的 `.olc/customicon`、重启后图标读取、原包不变、HTTP/无效图片/缺失图标的回退、图标下载取消、登记后取消不重复导入、并发图标归属，以及真实任务中心 ViewModel 在任务创建前后打开的联动。图标使用本机临时 HTTP 服务器提供图片，包文件继续使用模拟 HTTP。

下载侧栏状态和实际 Java 启动目录可单独运行：

```powershell
dotnet tests/OneLauncher.RegressionTests/bin/Debug/net10.0/OneLauncher.RegressionTests.dll build/pack-improvements-20261023/download --download-ui
$env:ONELAUNCHER_TEST_JAVA_HOME = 'C:/Program Files/Eclipse Adoptium/jdk-21.0.7.6-hotspot'
dotnet tests/OneLauncher.RegressionTests/bin/Debug/net10.0/OneLauncher.RegressionTests.dll build/pack-improvements-20261023/launch --launch-directory
```

`--download-ui` 验证同一任务的侧栏和任务中心取消、状态图标、底部等宽按钮、失败重试、实例配置保存失败不能提前显示完成、迟到进度不能覆盖终态，并输出实际 AXAML 截图。

`--launch-directory` 需要 Java/Javac 21 或更高版本，优先读取 `ONELAUNCHER_TEST_JAVA_HOME`，否则读取 `JAVA_HOME` 或 PATH。检查使用实际 `GameLauncher.BasicLaunch` 启动隔离 Java 主类，验证工作目录与 `--gameDir` 一致、两个实例的相对配置隔离、根目录模式、含空格路径和共享资源。启动器写入的既有 `LastLaunchArgs.bat` 会备份并恢复；沙箱中运行时可能需要允许访问该诊断文件。
