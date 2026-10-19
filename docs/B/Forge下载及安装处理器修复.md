# Forge／NeoForge 下载及安装处理器修复

## 问题

Forge 的运行依赖和安装依赖可能包含相同目标文件。原合并列表会创建多个写同一 `.downloading` 文件的任务，Windows 因而报文件被占用；“其他进程”也可能是同一启动器中的另一个任务。

下载完成数原来在下载前增加，重试和缓存还可能导致超过总数。安装处理器失败时缺少完整持久化输出，难以定位退出码 1 的原因。客户端映射下载使用 Java 的网络路径，与启动器下载器配置不同。

## 修改

- 按规范化目标路径去重，普通、SHA1 和竞速下载共享路径锁，每个任务使用独立临时文件。
- 成功后才计数，重试成功只计一次；取消继续传播，不把取消当作成功。
- 下载计划不重复累加此前 loader 的依赖，完成计数只包含本次计划中的唯一目标。
- 明确支持的客户端 `DOWNLOAD_MOJMAPS` 使用启动器下载器按原版元数据获取文件并校验长度和 SHA1；不确定处理器版本／格式仍回退 Java。
- Java 参数使用 `ArgumentList`，等待 stdout/stderr EOF 后判断退出码并保存安装日志；取消时结束进程树并收尾。

本 PR 不包含 PCL 导入、游戏会话生命周期或首页通知修复。

## 独立验证

Windows x64、.NET SDK 10.0.302 上，桌面项目及 ConsoleTest 构建成功，0 错误，原有警告仍存在。独立分支通过 8 项下载和 6 项 Forge processor 回归：重复路径、并发互斥、失败／取消／重试、缓存恢复、映射校验、未知变体回退、输出收尾及 Unicode 参数。

```powershell
dotnet build ConsoleTest/ConsoleTest.csproj -c Debug
dotnet ConsoleTest/bin/Debug/net10.0/ConsoleTest.dll --download-regression-tests
dotnet ConsoleTest/bin/Debug/net10.0/ConsoleTest.dll --processor-regression-tests
```

拆分前曾使用真实 Forge 47.4.10 安装清单验证依赖下载、映射及全部客户端处理器。本次拆分只重复独立构建和自动化回归，没有重复真实联网安装。

路径锁仅协调本进程的相关下载入口，不替代跨进程安装锁。用户那次处理器 2/6 退出码 1 的唯一底层原因未完整复现，不将网络差异假说写成已证明原因。

共享下载工具和测试分发入口与 PCL PR 保持完全相同，使两份 PR 可独立编译并合并；不上传账号、游戏实例、日志或编译产物。

## 同步上游

发布期间上游更新，现已通过普通 merge 同步 master `27cb3a3`，未改写已发布历史。独立桌面和 ConsoleTest 构建再次通过，14 项回归全部通过；三份修复组合无冲突，组合桌面构建及 31 项回归通过。

上游 `global.json` 要求 SDK 10.0.402，本机仅有 10.0.302；本次从仓库外以项目绝对路径使用现有 SDK 验证，未修改锁定配置。尚未在锁定 SDK 上复测，也未运行目标平台矩阵脚本。
