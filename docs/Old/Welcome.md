# 项目基本架构
- 项目 OneLancher ： UI层
- 项目 OneLancher.Core ： 核心层（后端）
- 项目 OneLancher.Core.Net ： 核心层网络层
- 项目 OneLancher.Desktop ： 启动层（Main函数所在），包含一些编译配置
依赖注入的配置地点是 MainWindow.axaml.cs

# 项目数据存储方法
此项目长期数据保存一律使用Json，其数据模型有：
- AccountManger ： 账号管理器，保存所有账号信息
- DBManger ： 基本信息管理器，保存如偏好和设置之类的信息
- GameDataManger ： 游戏实例数据管理器，保存所有游戏数据
数据管理器全局单例，UI层使用时在构造函数声明，然后依赖注入到其中。
添加数据时，在对应管理器上方的数据模型添加属性即可。
添加数据管理器时，写一个类继承自BasicDataManger，然后配置依赖注入即可。
一个数据管理器对应一个实际存在的Json文件。

# UI基本方法
UI 层的基础方法、样式体系、复用控件、MVVM 边界和新增页面规范已迁移到 [docs/UI_FI.md](docs/UI_FI.md)。

进行任何 UI 开发或评审前，请先打开并阅读该文件；`Welcome.md` 不再重复维护 UI 规则。

# 游戏启动模型
对于UI组件:Game.EasyGameLauncher是核心入口，若启动失败会抛出一个LaunchException，其中包含详细信息。
UI层启动组件管理UI层职责，包括显示启动状态、输出日志等。
UI层启动组件内部使用GameLauncher类，其实现了 IDisposable 接口，使用using语句块来管理其生命周期。
GameLauncher类内部使用Process类来管理游戏进程和额外的文件系统工作。关键的Java路径和参数设置由LaunchCommandBuilder承担，参数获取成功后返回一个LaunchCommand。
