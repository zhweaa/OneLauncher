# IPageHost 账户侧栏绑定修复

## 问题与原因

`AccountPageViewModel` 实现 `IPageHost` 时，将 `IsPaneShow` 和 `PaneContent` 改为了普通自动属性。普通自动属性不会自动发送 `INotifyPropertyChanged.PropertyChanged` 通知；即使 ViewModel 继承 `ObservableObject`，自动属性的 setter 也不会因此自动获得通知逻辑。

账户页面通过编译绑定把这两个属性连接到 `PageShell.IsPaneOpen` 和 `PageShell.PaneContent`。命令修改自动属性后，绑定无法收到更新，因此无法正常显示侧栏或替换内容。

## 修改文件与实现

- `OneLauncher/ViewModels/Pages/AccountPageViewModel.cs`：使用带 `[ObservableProperty]` 的 `_isPaneShow`、`_paneContent` 字段，生成带变更通知的公开 `IsPaneShow`、`PaneContent` 属性；新建账户和皮肤管理命令先设置内容，再打开侧栏。
- 本文档：记录原因、实现和验证范围。

生成的公开属性能够隐式实现 `IPageHost`，不需要手写同名自动属性。`PaneContent` 使用可空类型，与接口一致，表达尚未打开侧栏时没有内容的状态。子 Pane 仍通过已有回调设置 `IsPaneShow = false`，该操作也会发送通知，驱动侧栏关闭。账户页面的 XAML 绑定无需修改。

## 后续扩展

其他 Page 实现 `IPageHost` 时，也可以使用 `[ObservableProperty]` 字段生成接口所需属性。接口只定义属性契约，不自动提供通知行为。若需要手写属性，应在 setter 中调用 `SetProperty(ref backingField, value)`。不要同时声明同名自动属性和生成属性。

本次修复仅涉及账户页面，其他页面接入接口的范围由后续任务决定。

## 验证

执行 `dotnet build OneLauncher/OneLauncher.csproj --no-restore --nologo -v minimal -clp:ErrorsOnly` 成功，0 个错误、267 个警告。这验证了生成属性可实现接口，以及账户页面编译绑定可以解析属性。

未运行图形界面交互验证。可在应用中依次检查“新建”打开登录侧栏、返回关闭侧栏，以及正版账户“皮肤管理”打开对应内容并返回关闭。
