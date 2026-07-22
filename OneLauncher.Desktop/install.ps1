# install.ps1

# 配置
$OutputEncoding = [System.Text.Encoding]::UTF8
$softwareUrl = "https://github.com/abbcccbba/OneLauncher/releases/download/v0.5.0.0/OneLauncher.Desktop.exe"
$softwareFile = Join-Path (Get-Location) "OneLauncher.Desktop.exe"
$logFile = "$env:TEMP\OneLauncherInstall.log"

# --- 辅助函数 ---

# 检查 winget 是否可用
function Test-Winget {
    if (-not (Get-Command "winget" -ErrorAction SilentlyContinue)) {
        Write-Host "检测到系统未安装 winget 包管理器。安装 .NET Desktop Runtime 和 Temurin JDK 需要 winget。" -ForegroundColor Red
        Write-Host "请前往 Microsoft Store 搜索并安装“应用安装程序”，或者访问此链接获取更多信息: https://aka.ms/winget-install" -ForegroundColor Cyan
        return $false
    }
    return $true
}

# 检查管理员权限 (仅用于判断是否需要提升，不自动提升)
function Test-Admin {
    return ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

# 检测 .NET 9 Desktop Runtime 是否已安装
function Test-DotNetDesktopRuntime {
    Write-Host "正在检测 .NET 9 Desktop Runtime..."
    try {
        # winget list 可以用来检查已安装的包
        $installed = winget list --id Microsoft.DotNet.DesktopRuntime.9 --exact -q
        if ($installed) {
            Write-Host ".NET 9 Desktop Runtime 已安装。" -ForegroundColor Green
            return $true
        } else {
            Write-Host ".NET 9 Desktop Runtime 未安装。" -ForegroundColor Yellow
            return $false
        }
    } catch {
        Write-Host "检测 .NET 9 Desktop Runtime 失败：$_" -ForegroundColor Red
        return $false
    }
}

# 检测 Temurin JDK 24 是否已安装
function Test-TemurinJDK {
    Write-Host "正在检测 Temurin JDK 24..."
    try {
        $installed = winget list --id EclipseAdoptium.Temurin.24.JDK --exact -q
        if ($installed) {
            Write-Host "Temurin JDK 24 已安装。" -ForegroundColor Green
            return $true
        } else {
            Write-Host "Temurin JDK 24 未安装。" -ForegroundColor Yellow
            return $false
        }
    } catch {
        Write-Host "检测 Temurin JDK 24 失败：$_" -ForegroundColor Red
        return $false
    }
}

# --- 主逻辑 ---

# 开始日志
Start-Transcript -Path $logFile -Append

# 依赖项检测并提醒用户
Write-Host "=== 依赖项检测 ==="
$dotnetInstalled = Test-DotNetDesktopRuntime
$jdkInstalled = Test-TemurinJDK

Write-Host "===================="
Write-Host "" # 空行增加可读性

# 显示菜单
Write-Host "=== OneLauncher 安装程序 ==="
Write-Host "OneLauncher 的运行可能需要 .NET 9 Desktop Runtime 和 Temurin JDK 24。" -ForegroundColor Yellow
Write-Host "如果您的电脑已经安装了这些依赖项，可以选择仅下载 OneLauncher (选项1) 以节省时间。" -ForegroundColor Yellow
Write-Host "如果不确定或需要安装依赖项，请选择选项2或3。" -ForegroundColor Yellow
Write-Host "" # 空行增加可读性

Write-Host "1. 仅下载 OneLauncher"
Write-Host "2. 下载 OneLauncher + 安装 .NET 9 Desktop Runtime"
Write-Host "3. 下载 OneLauncher + 安装 .NET 9 Desktop Runtime + 安装 Temurin JDK 24"
$choice = Read-Host "请选择一个选项 (1-3)"

# 增强用户输入验证
if ($choice -notmatch "^[1-3]$") {
    Write-Host "输入无效！请选择 1、2 或 3。" -ForegroundColor Red
    Stop-Transcript
    exit 1
}

# 下载 OneLauncher
function Download-OneLauncher {
    Write-Host "正在下载 OneLauncher 到 $(Get-Location)..."
    try {
        Invoke-WebRequest -Uri $softwareUrl -OutFile $softwareFile -ErrorAction Stop
        Write-Host "OneLauncher 下载成功！" -ForegroundColor Green
    } catch {
        Write-Host "下载 OneLauncher 失败：$_" -ForegroundColor Red
        Stop-Transcript
        exit 1
    }
}

# 安装 .NET 9 Desktop Runtime
function Install-DotNet {
    if ($dotnetInstalled) {
        Write-Host ".NET 9 Desktop Runtime 已安装，跳过安装。" -ForegroundColor Green
        return
    }

    if (-not (Test-Winget)) {
        Stop-Transcript
        exit 1 # Test-Winget 函数会打印错误信息
    }

    # 在这里请求管理员权限
    if (-not (Test-Admin)) {
        Write-Host "安装 .NET 9 Desktop Runtime 需要管理员权限，正在请求权限..." -ForegroundColor Yellow
        try {
            $command = "winget install -e --id Microsoft.DotNet.DesktopRuntime.9 --silent --accept-source-agreements --accept-package-agreements"
            Start-Process -FilePath "powershell" -ArgumentList "-Command $command" -Verb RunAs -Wait
            # 检查 Start-Process 是否成功，以及 winget 命令是否成功
            # 注意：Start-Process 的 $LASTEXITCODE 是指 powershell.exe 的退出码，而不是 winget 的
            # 更可靠的做法是让用户再次运行此脚本，或者在请求权限的子进程中记录更详细的日志
            Write-Host ".NET 9 Desktop Runtime 安装尝试完成。请检查是否成功安装。" -ForegroundColor Yellow
        } catch {
            Write-Host "无法提升权限或安装失败：$_" -ForegroundColor Red
            Stop-Transcript
            exit 1
        }
    } else {
        Write-Host "正在安装 .NET 9 Desktop Runtime..."
        try {
            winget install -e --id Microsoft.DotNet.DesktopRuntime.9 --silent --accept-source-agreements --accept-package-agreements | Out-Host
            if ($LASTEXITCODE -ne 0) {
                throw "winget 安装 .NET 9 Desktop Runtime 返回错误码: $LASTEXITCODE"
            }
            Write-Host ".NET 9 Desktop Runtime 安装成功！" -ForegroundColor Green
        } catch {
            Write-Host "安装 .NET 9 Desktop Runtime 失败：$_" -ForegroundColor Red
            Stop-Transcript
            exit 1
        }
    }
}

# 安装 Temurin JDK 24
function Install-OpenJDK {
    if ($jdkInstalled) {
        Write-Host "Temurin JDK 24 已安装，跳过安装。" -ForegroundColor Green
        return
    }

    if (-not (Test-Winget)) {
        Stop-Transcript
        exit 1 # Test-Winget 函数会打印错误信息
    }

    # 在这里请求管理员权限
    if (-not (Test-Admin)) {
        Write-Host "安装 Temurin JDK 24 需要管理员权限，正在请求权限..." -ForegroundColor Yellow
        try {
            $command = "winget install -e --id EclipseAdoptium.Temurin.24.JDK --silent --accept-source-agreements --accept-package-agreements"
            Start-Process -FilePath "powershell" -ArgumentList "-Command $command" -Verb RunAs -Wait
            Write-Host "Temurin JDK 24 安装尝试完成。请检查是否成功安装。" -ForegroundColor Yellow
        } catch {
            Write-Host "无法提升权限或安装失败：$_" -ForegroundColor Red
            Stop-Transcript
            exit 1
        }
    } else {
        Write-Host "正在安装 Temurin JDK 24..."
        try {
            winget install -e --id EclipseAdoptium.Temurin.24.JDK --silent --accept-source-agreements --accept-package-agreements | Out-Host
            if ($LASTEXITCODE -ne 0) {
                throw "winget 安装 Temurin JDK 24 返回错误码: $LASTEXITCODE"
            }
            Write-Host "Temurin JDK 24 安装成功！" -ForegroundColor Green
        } catch {
            Write-Host "安装 Temurin JDK 24 失败：$_" -ForegroundColor Red
            Stop-Transcript
            exit 1
        }
    }
}

# 根据用户选择执行
switch ($choice) {
    "1" {
        Download-OneLauncher
    }
    "2" {
        Download-OneLauncher
        Install-DotNet
    }
    "3" {
        Download-OneLauncher
        Install-DotNet
        Install-OpenJDK
    }
}

# 安装完成提示
Write-Host "安装完成！OneLauncher.Desktop.exe 已下载到 $(Get-Location)。" -ForegroundColor Green
$runChoice = Read-Host "是否立即运行 OneLauncher？(Y/N)"
if ($runChoice -eq "Y" -or $runChoice -eq "y") {
    Write-Host "正在启动 OneLauncher..."
    Start-Process -FilePath $softwareFile
}

Stop-Transcript
