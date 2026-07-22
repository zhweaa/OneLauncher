# OneLauncher

**OneLauncher** 是一个以快速为功能核心以傻瓜式为设计准则的轻量化 Minecraft 启动器

快速安装（Windosw PowerShell）：

```powershell
Invoke-Expression ((New-Object System.Net.WebClient).DownloadString('https://raw.githubusercontent.com/zhweaa/OneLauncher/master/OneLauncher.Desktop/install.ps1'))
```

## 系统要求

- **操作系统**：
  - 对于Windows，需为Windows10-x64及以上
  - 对于MacOS，仅支持Arm平台
  
- **运行环境**：
  - 在依赖框架的构建中需要.NET10（或更高版本 可以在[这里](https://dotnet.microsoft.com/zh-cn/download/dotnet/9.0)下载）    
    *注意：部分较新的Windows可能自带此依赖，无需安装*
    
  - Java 环境  
    *OneLauncher支持自动下载并使用合适的Java，如果你的系统无Java运行时，请在下载时启动此选项*
    *；如果自动程序不起作用，我们推荐[Eclipse Adoptium](https://adoptium.net/zh-CN/download/)*
- **特定平台功能限制**
  - **联机** 仅限Windows
  - **Web Accoount Manger登入方式** 仅限Windows
    
## 支持的额外功能列表
- 命令行模式
- 多下载源下载游戏
- 导入PCL2版本
- 自定义/优化启动参数
- 查看新闻
- 服务器书签
- 内存优化
- 模组加载器
  - Fabric
  - Neoforge
  - Quilt
  - Forge（有限支持）
- 一键开服
- 管理本地模组
- 导入Modpack包
- 微软登入验证
- 外置登入验证
- 离线登入验证
- 联机
- 下载模组
  - Modrinth

## 跨平台安装与构建指南

### 通过下载源代码并构建的方式使用

1. 下载源代码
2. 使用[Visual Studio](https://visualstudio.microsoft.com/)或[Rider](https://www.jetbrains.com/rider/)打开[OneLauncher.sln](https://github.com/abbcccbba/OneLauncher/blob/master/OneLauncher.sln)
3. 将[OneLauncher.Desktop](https://github.com/abbcccbba/OneLauncher/blob/master/OneLauncher.Desktop/OneLauncher.Desktop.csproj)设为启动项目
4. 运行，便可以看到窗口。构建为可执行文件请参考[这里](https://www.google.com/)

### 对于Windows AOT编译

请在启动项目（即OneLauncher.Desktop）内添加以下配置

``` XML
<PropertyGroup>
  <!-- 启用AOT编译，通用，无论Win还是Mac对于AOT都需要开启 -->
	<PublishAot>true</PublishAot>
</PropertyGroup>
<ItemGroup Label="ImportLib">
  <!-- 针对Windows的发布不生成单文件解决访问，静态链接，对于Mac可以删去，因为最终还是要打包到App包且这些静态库仅适用于Windows -->
  <!-- 项目配置中不会包含这些静态库，请到 https://github.com/abbcccbba/OneLauncher/releases/tag/v0.1.4AOTv1.2.0/ 下载并放入libs文件夹-->
	<DirectPInvoke Include="libHarfBuzzSharp" />
	<NativeLibrary Include="libs\libHarfBuzzSharp.lib" />
	<DirectPInvoke Include="libSkiaSharp" />
	<NativeLibrary Include="libs\libSkiaSharp.lib" />
	<DirectPInvoke Include="av_libglesv2" />
	<NativeLibrary Include="libs\av_libglesv2.lib" />
</ItemGroup>  
```

## 开源与贡献

此项目使用 Apache 2.0 许可证开源  

OneLauncher 当前处于早期开发阶段，许多功能尚未完成。我们非常欢迎开发者共同参与完善： 

如果你有任何问题或请求可以在[这里](https://github.com/abbcccbba/OneLauncher/issues)发起提问  

### 开源项目或需注明署名使用及贡献人员名单

**省略.NET与Microsoft.,Windows.等基础框架**

- 使用项目[Avalonia](https://github.com/AvaloniaUI/Avalonia)
- 引用项目[AsyncImageLoader.Avalonia](https://github.com/AvaloniaUtils/AsyncImageLoader.Avalonia)
- 借鉴项目[ProjBobcat](https://github.com/Corona-Studio/ProjBobcat/)
- 使用图标[ICONS8](https://icons8.com/icons/)

### 此项目Mojang（Microsoft）的关系

此项目是由[Lnetfabe](https://github.com/abbcccbba/)（化名）开发的以个人名义发布的开源软件 ，并由社区驱动更新 ，任何第三方服务器的接入均为改善体验而存。

本软件作者（Lnetface）均与Mojang（Microsoft）无从属关系。  
此项目仅为个人学习项目，任何人、组织、公司或国家（等个体或集体）在使用本软件时导致的任何事情或发生的任何事情均与作者无关。 

**本软件提供的离线登入功能仅对中国大陆用户开放，对于国外用户请自觉登入正版账号并不使用这个功能，对于使用者的任何侵权行为均与原作者无关**

### 开发中功能

- 高级Java管理
- 下载自定义版本的模组加载器

## 更多

[服务条款](https://github.com/abbcccbba/OneLauncher/blob/master/Terms_of_Service.md)
[隐私声明](https://github.com/abbcccbba/OneLauncher/blob/master/Privacy_Policy.md)
