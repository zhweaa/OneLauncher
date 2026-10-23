# VersionList 旧配置迁移移除执行报告

日期：2026-10-23。

旧版本兼容期结束后，启动器不再从全局 `config.json` 导入版本列表。`UnitedCore` 直接初始化当前安装目录的 `VersionListManager`；索引存在时加载，不存在时创建合法空列表。存储位置仍为 `<InstalledPath>/minecraft/versions/version_list.json`。

## 改动

- `OneLauncher.Core/Global/ModelDataMangers/DBManager.cs`：删除 `LegacyVersionList`，对 `AppConfig` 显式设置 `JsonUnmappedMemberHandling.Skip`。旧 `VersionList` 作为未映射属性直接忽略；其他全局设置继续读取。正常保存配置时不再输出这个旧字段。
- `OneLauncher.Core/Global/UnitedCore.cs`：移除迁移辅助器调用，直接等待版本管理器初始化。
- 删除 `OneLauncher.Core/Global/ModelDataMangers/VersionListMigration.cs`，移除旧配置备份、条目迁移、清理和重试逻辑。
- `OneLauncher.Core/Global/ModelDataMangers/VersionListManager.cs`：删除迁移通知属性和仅用于迁移的文件重读比较方法。
- `OneLauncher.Core/Global/ModelDataMangers/BasicDataManger.cs`：更新读取方法注释；通用初始化、严格索引校验和临时文件提交保存能力保留。
- `OneLauncher/Views/MainWindow.axaml.cs`：移除启动时的迁移通知。
- `tests/OneLauncher.RegressionTests/Program.cs`：用旧字段忽略、独立索引初始化测试替换已失效的迁移测试；保留并发、保存失败、取消、UI 和依赖注入回归检查。

本次不删除真实用户数据或既有迁移备份，不因启动读取旧配置而自动重写 `config.json`。新索引损坏时继续报错并保留原文件，不以旧配置恢复或覆盖。

## 验证

```powershell
dotnet build tests/OneLauncher.RegressionTests/OneLauncher.RegressionTests.csproj --no-restore --nologo -v minimal -clp:ErrorsOnly
dotnet tests/OneLauncher.RegressionTests/bin/Debug/net10.0/OneLauncher.RegressionTests.dll build/version-list-no-migration-20261023
```

构建及完整离线回归通过。检查覆盖旧字段为数组、null、对象、数字、字符串时的忽略行为；全局设置保留；保存不输出旧字段；新索引缺失、已存在、损坏及不同安装目录的行为；版本并发登记、保存失败和取消；页面绑定、导航及依赖注入。

## 后续约定

版本列表只由 `VersionListManager` 管理，新安装入口继续复用公共安装服务。`AppConfig` 中的未映射属性不参与版本登记。早期迁移行动报告和执行记录属于历史设计，本报告取代其中关于旧 `config.json.VersionList` 自动迁移与通知的约定。
