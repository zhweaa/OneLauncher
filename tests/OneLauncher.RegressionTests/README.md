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
