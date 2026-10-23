# Linux 实例快捷方式改为 .desktop

日期：2026-10-22。基于 master `34ea71bb349500dad65f8c1580eaa46bd294fc1f`。

## 问题与改动

原来的 Linux 实例快捷方式是仅包含启动命令的 `.sh` 文件，缺少执行权限，不能作为可用的桌面启动入口。

源码只修改 `EditGameDataPaneViewModel.AddToQuicklyPlay`：

- Linux 输出 `.desktop`，写入 `[Desktop Entry]`、`Type=Application`、`Name`、`Exec` 和 `Terminal=false`。
- `Exec` 保留现有 `--quicklyPlay <实例ID>` 接口；引用可执行文件路径，按 desktop entry 与命令参数两层规则转义。
- 等待文件写完，再为当前用户增加执行权限，保留其他已有权限。
- 命令改为异步 Task，绑定名称不变；Windows/macOS 继续使用原来的文件格式。

没有增加依赖、修改 UI 布局、引入单例获取方式或取消令牌。局部转义函数仅服务本方法；若以后需要统一服务器快捷方式，可另行提取共享实现，本次不扩大范围。

## 验证

- Linux Mint 22.3 上按原 `LinuxAot.pubxml` 发布成功，未关闭 AOT 或 trimming。
- Windows `win-x64` Debug 编译通过。验证时使用任务目录内的独立 NuGet 配置，避开电脑上不存在的旧包源；没有修改项目配置。
- 用真实 AOT 界面的“创建桌面快捷启动”生成文件，`desktop-file-validate` 返回 0；文件权限为 `0764`，包含当前用户执行位。
- 使用包含空格、中文、美元符号、反引号、双引号、反斜杠的启动器路径测试；`gio launch` 返回 0，实际进程收到正确的可执行路径、`--quicklyPlay` 和实例 ID，Minecraft 1.20.1 进入主菜单并正常退出。
- `git diff --check` 通过。

图形验证使用隔离 HOME 与 Xvfb/Mesa。没有验证 Cinnamon 桌面的首次信任确认流程，也没有覆盖用户旧安装和旧快捷方式；修复后需重新生成快捷方式。

特殊路径边界：当可执行文件路径含字面 `%` 时，规范要求的 `%%` 转义能通过格式校验，但本机 GIO 拒绝加载该入口；补充 `TryExec` 也未解决。当前改动没有引入额外包装脚本来绕过这一桌面组件限制，实际安装路径应避免 `%`。其他上述路径字符已完成实际启动验证。

验证记录保留在本次任务的 `evidence` 目录，包括 `publish-aot-final.log`、`shortcut-validation-final.json`、`gio-launch-final-result.json`、`desktop-launched-game.png` 和 `percent-path-limitation.json`。
