# UI 对齐与 Page / Pane 动画迁移

日期：2026-10-23。

## 迁移范围

根据用户最新要求，从 Codex 副本迁移到 `E:/OneLauncherProject/OneLauncher` 的生产代码仅保留工具栏高度对齐、Page 切换和 Pane 内容/遮罩过渡，共六个 AXAML 文件。之前副本中的其他 UI 设计已撤回，不整包覆盖实际项目。

| 文件 | 最终改动 |
|---|---|
| OneLauncher/Styles/Theme.axaml | 公共工具栏高度 40 DIP、Pane 过渡 160 ms、Page 过渡 180 ms |
| OneLauncher/Styles/Controls.axaml | 工具栏 Button、搜索 TextBox / AutoCompleteBox、ComboBox.toolbar-field 等高且居中 |
| OneLauncher/Styles/Shells.axaml | Pane 内容 CrossFade 的进入/退出缓动；遮罩用 Opacity 连续显隐 |
| OneLauncher/Views/MainWindow.axaml | Page 切换改为 180 ms CrossFade，进入 CubicEaseOut、退出 CubicEaseIn |
| OneLauncher/Views/Pages/gamedata.axaml | 仅给标签 ComboBox 增加 toolbar-field 类名 |
| OneLauncher/Views/Pages/ModsBrowser.axaml | 仅给资源类型 ComboBox 增加 toolbar-field 类名，删除覆盖公共高度的局部 MinHeight |

另新增本文作为实现记录。

## 视觉与布局

工具栏将实际 Height 与 MinHeight 统一约束为 40 DIP，避免不同模板的自然高度造成上下边缘不齐。AutoCompleteBox 的文本居中设置在模板内 TextBox 上，适配该控件的属性契约。

游戏数据卡片保持原来的 96 DIP 高度、信息与操作并排、操作区横向 StackPanel；ItemTemplate 与实际项目原版逐字一致。没有迁移卡片分行、省略/Tooltip、新增 WrapPanel 或条目按钮尺寸规则。

TransparentListBox.axaml 完全保留实际项目原版：常态阴影 `0 4 14 1 #32000000`、悬停阴影 `0 8 24 2 #42000000`、按下阴影 `0 2 8 1 #2A000000`。工具栏自己的阴影也保持原值。

附图搜索区的额外空白来自上一版副本新增的状态行：即使搜索成功、状态为空，仍保留 MinHeight=18 和底部边距 4。该占位已撤回，资源检索恢复原来的 HasSearchStatus 显隐绑定和滚动结构。搜索成功时状态行不参与布局，搜索框到首个列表项的正常间距为 23 DIP，比上一版少 22 DIP；搜索中、失败或无结果时仍可显示提示。

## 动画逻辑与扩展

Page 和 Pane 内容均使用 Avalonia 原生 CrossFade，进入逐渐减速、退出逐渐加速。遮罩保持在视觉树中，通过状态样式在 0/1 之间过渡；不参与命中测试。Pane 打开仍禁用底层页面，完成或取消后恢复，不能点击背景自动关闭。

后续调节节奏只需修改 Theme 中 MotionPageDuration / MotionPaneDuration；新工具栏 ComboBox 使用 toolbar-field。Pane 的请求宽度、导航事件、主页构图、条目交互和业务代码未迁移任何额外修改。

## 验证与清理

所有构建、测试和真实窗口截图在 Codex 副本内部完成，使用 ConsoleTest 的隔离夹具，不读取实际账户或游戏数据。最终 ConsoleTest 和 OneLauncher.Desktop 构建通过，0 错误；最终增量构建均为 0 警告。

Headless 与 Win32 真实窗口分别通过 12 组检查：Light / Dark，880×560、760×500、1200×800，导航收起 / 展开。验证实际工具栏上/下边缘、96 DIP 单行卡片、清除按钮、搜索状态显隐和最大 23 DIP 的搜索区间距；同时检查遮罩连续中间状态、原页面禁用/恢复，以及快速切换 Page / Pane、反复打开关闭后的最终状态。已人工查看真实窗口的资源检索和游戏数据截图。

迁移前校验实际源文件与准备副本时的 SHA256 一致；迁移后校验六个文件与通过验证的副本逐字一致，并检查 Git 差异仅包含上述代码范围。列表样式、Pane 样式、PageShell.cs、MainWindow.axaml.cs、Home.axaml 与原版一致。

测试入口和依赖只用于 Codex 副本，不迁移到实际项目。测试配置、实例、截图、临时目录及本轮 bin/obj 在验证完毕后清理。工作区旧补丁同步收缩为本次六个生产文件的差异。
