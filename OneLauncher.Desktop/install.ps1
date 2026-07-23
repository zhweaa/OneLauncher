# OneLauncher Windows 安装脚本

$ErrorActionPreference = "Stop"
$OutputEncoding = [System.Text.Encoding]::UTF8

$repository = "zhweaa/OneLauncher"
$assetName = "OneLauncher.Desktop.exe"
$softwareFile = Join-Path (Get-Location) $assetName
$logFile = Join-Path $env:TEMP "OneLauncherInstall.log"

# 兼容 Windows PowerShell 5.1 访问 GitHub
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

function Test-Winget {
    return $null -ne (Get-Command winget -ErrorAction SilentlyContinue)
}

function Test-DotNetDesktopRuntime {
    if ($null -eq (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        return $false
    }

    $runtimes = & dotnet --list-runtimes 2>$null
    return ($runtimes | Where-Object {
        $_ -match '^Microsoft\.WindowsDesktop\.App\s+10\.'
    }).Count -gt 0
}

function Test-Java {
    return $null -ne (Get-Command java -ErrorAction SilentlyContinue)
}

function Get-OneLauncherAsset {
    $headers = @{
        "User-Agent" = "OneLauncher-Installer"
        "Accept"     = "application/vnd.github+json"
    }

    $apiUrl = "https://api.github.com/repos/$repository/releases?per_page=30"

    Write-Host "正在查询 OneLauncher 最新版本..."

    $releases = Invoke-RestMethod -Uri $apiUrl -Headers $headers

    foreach ($release in $releases) {
        if ($release.draft -or $release.prerelease) {
            continue
        }

        $asset = $release.assets |
            Where-Object { $_.name -eq $assetName } |
            Select-Object -First 1

        if ($null -ne $asset) {
            return $asset
        }
    }

    throw "没有找到包含 $assetName 的公开 Release。"
}

function Download-OneLauncher {
    $asset = Get-OneLauncherAsset
    $size = [Math]::Round($asset.size / 1MB, 1)

    Write-Host "正在下载 $($asset.name)（$size MB）..."

    Invoke-WebRequest `
        -Uri $asset.browser_download_url `
        -OutFile $softwareFile `
        -UseBasicParsing

    if (-not (Test-Path $softwareFile)) {
        throw "下载完成后未找到 $softwareFile。"
    }

    Write-Host "OneLauncher 下载成功：" -ForegroundColor Green
    Write-Host $softwareFile -ForegroundColor Cyan
}

function Install-DotNet {
    if (Test-DotNetDesktopRuntime) {
        Write-Host ".NET 10 Desktop Runtime 已安装。" -ForegroundColor Green
        return
    }

    if (-not (Test-Winget)) {
        throw "未找到 winget。请先安装 Windows 应用安装程序：https://aka.ms/winget-install"
    }

    Write-Host "正在安装 .NET 10 Desktop Runtime..."

    winget install `
        --exact `
        --id Microsoft.DotNet.DesktopRuntime.10 `
        --silent `
        --accept-source-agreements `
        --accept-package-agreements

    if ($LASTEXITCODE -ne 0) {
        throw "winget 安装 .NET 10 Desktop Runtime 失败，错误码：$LASTEXITCODE"
    }

    if (-not (Test-DotNetDesktopRuntime)) {
        throw "安装完成后仍未检测到 .NET 10 Desktop Runtime。"
    }

    Write-Host ".NET 10 Desktop Runtime 安装成功。" -ForegroundColor Green
}

function Install-Java {
    if (Test-Java) {
        Write-Host "Java 已安装，跳过安装。" -ForegroundColor Green
        return
    }

    if (-not (Test-Winget)) {
        throw "未找到 winget，无法自动安装 Java。"
    }

    Write-Host "正在安装 Temurin JDK 21..."

    winget install `
        --exact `
        --id EclipseAdoptium.Temurin.21.JDK `
        --silent `
        --accept-source-agreements `
        --accept-package-agreements

    if ($LASTEXITCODE -ne 0) {
        throw "winget 安装 Java 失败，错误码：$LASTEXITCODE"
    }

    Write-Host "Temurin JDK 21 安装成功。" -ForegroundColor Green
}

Start-Transcript -Path $logFile -Append | Out-Null

try {
    Write-Host ""
    Write-Host "=== OneLauncher 安装程序 ===" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "1. 仅下载 OneLauncher"
    Write-Host "2. 下载 OneLauncher，并安装 .NET 10 Desktop Runtime"
    Write-Host "3. 下载 OneLauncher，并安装 .NET 10 和 Java"
    Write-Host ""

    $choice = Read-Host "请选择一个选项（1-3）"

    if ($choice -notmatch '^[1-3]$') {
        throw "输入无效，请输入 1、2 或 3。"
    }

    Download-OneLauncher

    if ($choice -in @("2", "3")) {
        Install-DotNet
    }

    if ($choice -eq "3") {
        Install-Java
    }

    Write-Host ""
    Write-Host "OneLauncher 安装完成。" -ForegroundColor Green

    $runChoice = Read-Host "是否立即运行 OneLauncher？（Y/N）"

    if ($runChoice -match '^[Yy]$') {
        Start-Process -FilePath $softwareFile
    }
}
catch {
    Write-Host ""
    Write-Host "安装失败：$($_.Exception.Message)" -ForegroundColor Red
    throw
}
finally {
    Stop-Transcript | Out-Null
}
