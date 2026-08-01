# OneLauncher UI Fundamental Instructions

`UI_FI` 是 UI Fundamental Instructions 的简称。本文件是 OneLauncher UI 层的基础约定；新增页面、Pane、控件或样式前必须先阅读。

## 1. UI 项目结构

UI 项目使用 Avalonia 和 MVVM，主要目录职责如下：

- `Assets`：图片、字体和其他视觉资源。
- `Codes`：确实无法归入 ViewModel 或 Core、但必须存在于 UI 层的后台逻辑。
- `Views`：AXAML 视图、视图 code-behind 和 ViewModel。
- `Views/Controls`：可复用的模板控件，只定义控件契约，不承载业务页面。
- `Views/ViewModels`：主页面 ViewModel，以及页面列表使用的 `XXXItem` 展示模型。
- `Views/Converters`：模型值与 UI 显示值之间的转换器。
- `Views/Styles`：主题资源、控件模板和全局样式。
- `Views/Windows`：需要独立 Window 的界面。
- `Views/Windows/WindowsViewModels`：独立 Window 对应的 ViewModel。
- `Views/Panes`：覆盖在页面右侧的任务 Pane。
- `Views/Panes/PaneViewModels`：Pane 对应的 ViewModel。
- `Views/Panes/PaneViewModels/Factories`：需要依赖注入的 Pane ViewModel 工厂。

依赖注入目前在 `Views/MainWindow.axaml.cs` 配置。UI 使用数据管理器时，通过构造函数依赖注入获取全局单例，不要在 View 内自行创建管理器。

## 2. 样式加载与资源分层

全局样式只从 `App.axaml` 加载一次：

1. `Theme.axaml` 定义颜色、背景、圆角和间距等语义资源。
2. `Styles.axaml` 是样式聚合入口。
3. 聚合入口继续加载 `Typography.axaml`、`Controls.axaml`、`Label.axaml`、`Pane.axaml`、`Shells.axaml` 和 `TransparentListBox.axaml`。

页面和 Pane 不要再次添加这些 `StyleInclude`。需要修改所有同类控件时，修改对应 Styles 文件；只属于单个页面的特殊表现才放在该页面的本地 Styles 中。

主要样式职责：

- `Theme.axaml`：亮色/暗色主题和语义资源。
- `Typography.axaml`：`page-title`、`section-title`、`item-title`、`item-meta` 等排版。
- `Controls.axaml`：工具栏按钮、项目操作按钮、状态标签、搜索框、设置控件和 Flyout 表面。
- `Label.axaml`：超链接按钮，使用 `Button.link-button`；不得改成匹配所有 Button 的全局选择器。
- `TransparentListBox.axaml`：页面卡片列表，使用 `ListBox.card-list`。
- `Pane.axaml`：Pane 内部标题、表面、字段和操作按钮。
- `Shells.axaml`：`PageShell` 和 `PaneHeader` 的控件模板。

新增颜色前先判断它是否是跨页面语义。如果是，应在 `Theme.axaml` 同时提供 Light 和 Dark 值，不要在多个页面重复硬编码颜色。

## 3. PageShell：页面与模态 Pane

所有带右侧 Pane 的主页面使用 `PageShell`：

```xml
<controls:PageShell IsPaneOpen="{Binding IsPaneShow, Mode=TwoWay}"
                    PaneContent="{Binding PaneContent}">
    <!-- 页面主要内容 -->
</controls:PageShell>
```

`PageShell` 继承 `ContentControl`，其 C# 类只声明可绑定契约：

- `Content`：页面主要内容，继承自 `ContentControl`。
- `PaneContent`：右侧任务 Pane。
- `IsPaneOpen`：Pane 打开状态。
- `OpenPaneLength`：Pane 宽度，默认 650。

视觉结构位于 `Shells.axaml`，而不是写死在 `PageShell.cs` 中。这样可以统一更换 Pane 动画、遮罩和布局，不需要修改每个页面。

右侧 Pane 的语义是模态任务层，不是普通导航抽屉：

- Pane 打开后必须显示遮罩，并禁用原页面交互。
- 禁止点击背景自动关闭，即 `UseLightDismissOverlayMode="False"`。
- 只有完成、保存、取消或返回命令可以关闭 Pane。
- Pane ViewModel 完成任务后，通过传入的关闭回调设置页面的 `IsPaneShow = false`。

## 4. PaneHeader：统一 Pane 头部

Pane 使用 `PaneHeader`，不要复制返回按钮、标题和右侧工具区：

```xml
<controls:PaneHeader Title="编辑实例"
                     BackCommand="{Binding ClosePaneCommand}" />
```

需要右侧操作时使用 `TrailingContent`：

```xml
<controls:PaneHeader Title="模组管理"
                     BackCommand="{Binding ClosePaneCommand}">
    <controls:PaneHeader.TrailingContent>
        <Button Classes="pane-toolbar" Command="{Binding RefreshCommand}">
            <TextBlock Text="刷新" />
        </Button>
    </controls:PaneHeader.TrailingContent>
</controls:PaneHeader>
```

`PaneHeader` 继承 `TemplatedControl`。标题、返回命令和尾部内容是控件 API；具体排版在 `Shells.axaml` 中。

## 5. ContentControl、TemplatedControl 与 UserControl

- `UserControl` 用于完整页面和业务 Pane。其内部视觉树属于该功能本身，通常由 AXAML 与 code-behind 配对。
- `ContentControl` 适合具有一个主要内容槽、但外观需要通过模板统一替换的复用控件，例如 `PageShell`。
- `TemplatedControl` 适合只定义控件契约、完全由 Style 决定外观的基础控件，例如 `PaneHeader`。

不要因为一个控件可以写成 `UserControl.axaml + .cs` 就默认使用 UserControl。跨页面的结构性控件应优先保持“C# 定义 API，Styles 定义外观”，以便主题和模板可以独立演进。

## 6. 页面列表规则

页面卡片列表统一使用：

```xml
<ListBox Classes="card-list" ItemsSource="{Binding Items}">
    <!-- ItemTemplate -->
</ListBox>
```

列表交互规则：

- 静止状态必须有可辨认的表面、边框和阴影。
- `pointerover` 增强阴影和表面亮度。
- `pressed` 降低阴影并提供按下反馈。
- 不使用 ScaleTransform 放大卡片，避免悬浮时越过列表边界。
- 管理类卡片没有选中语义时，`:selected` 必须与普通状态完全一致；选中后仍要保留 hover/pressed 动画。
- 如果列表完全不需要选择能力，同时项目内部还有 Toggle 或 Button，优先使用 `ItemsControl`，例如模组管理 Pane。

页面列表项通常在 ViewModel 中使用独立的 `XXXItem` 展示模型。展示状态、单项命令参数和纯 UI 派生属性可以放入该模型；数据持久化仍交给对应管理器。

## 7. 页面工具栏规则

- 页面标题和常用操作尽量保持在同一行。
- 使用 `Button.toolbar-action`，不要在每个页面重复按钮背景、边框、阴影和对齐属性。
- 图标与文字必须垂直居中；图文按钮内部使用横向 StackPanel，并保持一致间距。
- 不为只有两个固定选项的操作增加一层 MenuFlyout。
- 短期输入任务可以使用 Flyout，但常用主操作应直接显示。

游戏数据页的标签交互是特殊动作项模式：

- `AvailableTags` 的索引 0 固定为 `新建标签...`。
- 该项使用 `IsCreateAction` 标识，不伪造数据库中的 `GameDataTag`。
- 选择特殊项后打开创建 Flyout，并立即清空 ComboBox 选择，因此它不参与过滤。
- 创建按钮与 TextBox 的 Enter 都绑定同一个 `CreateNewTagCommand`。

## 8. MVVM 与 code-behind 边界

必须放在 ViewModel：

- 输入校验。
- 数据管理器调用和持久化。
- 异步任务。
- 业务状态与业务命令。
- 成功、失败和任务完成通知。

可以放在 code-behind：

- `FlyoutBase.ShowAttachedFlyout` 和 `Hide`。
- Window、焦点、指针和纯视觉生命周期事件。
- 将 ViewModel 的“任务完成”消息映射为关闭某个视觉层。

不要在 code-behind 中直接执行 ViewModel 命令或写入管理器。需要在任务成功后关闭 Flyout 时，由 ViewModel 发送完成消息，View 只负责响应消息关闭视觉层。

## 9. Pane ViewModel 工厂与关闭回调

在页面中打开 Pane 时，如果 Pane ViewModel 使用数据管理器等注入服务，应创建对应 Factory：

1. Factory 在依赖注入中注册为单例。
2. 页面 ViewModel 构造函数接收 Factory。
3. 打开 Pane 时由 Factory 创建 Pane ViewModel。
4. 页面将 `() => IsPaneShow = false` 作为关闭回调传入。

即使 Pane 当前没有注入依赖，也建议保持 Factory/回调结构，避免未来增加依赖时把创建逻辑重新搬回 View。

## 10. 视觉边界

- 主窗口标题栏和左侧导航保留项目原有的半透明窗口底材与交互样式，不再叠加第二套全局导航样式。
- 中央主要内容区必须完全不透明，保证内容可读性。
- Flyout 等覆盖层可以使用较厚的半透明材质、边框和圆角。
- 右侧 Pane 是覆盖原页面的模态任务表面。
- 主页保留原有 385px 内容卡片与 300px 启动区域构图；`PageShell` 只提供结构复用，不应改变主页比例。
- 阴影用于表达表面层级；动画用于反馈状态，不应让元素改变布局尺寸或越界。

## 11. 新增 UI 的检查清单

新增页面或 Pane 前检查：

- 是否复用了 `PageShell` 和 `PaneHeader`。
- 是否使用现有语义资源，而不是新增重复颜色。
- 是否把共用样式放入正确的 Styles 文件。
- 是否避免在页面中重复 `StyleInclude`。
- 列表是否正确选择 `ListBox.card-list` 或 `ItemsControl`。
- 无业务意义的选中态是否与普通态一致。
- hover/pressed 是否有反馈且不会越界。
- 页面顶部操作是否在单行内清晰对齐。
- 业务逻辑是否全部位于 ViewModel。
- Pane 是否只能通过显式完成或取消命令关闭。
- Light/Dark 主题下文字、边框和状态色是否仍可读。
- 最终是否完成 UI 项目编译和实际窗口截图检查。
