$ErrorActionPreference='Stop'
$repoRoot=Split-Path $PSScriptRoot -Parent
$stage=Join-Path $repoRoot ('work/轻量版构建-'+[Guid]::NewGuid().ToString('N'))
$target=Join-Path $repoRoot 'artifacts/轻量版'
& dotnet publish (Join-Path $repoRoot 'src/PanelSwitch/PanelSwitch.csproj') -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=false -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o $stage --nologo
if($LASTEXITCODE -ne 0){throw '轻量版构建失败。'}
Copy-Item -LiteralPath (Join-Path $repoRoot 'docs/第三方许可') -Destination (Join-Path $stage '许可') -Recurse
Copy-Item -LiteralPath (Join-Path $repoRoot 'docs/轻量版使用说明.md') -Destination (Join-Path $stage '使用说明.md')
foreach($entry in @(@('安装.cmd','--install'),@('恢复显示.cmd','--recover'),@('卸载.cmd','--uninstall'))){
    $command='@echo off'+"`r`n"+'"%~dp0PanelSwitch.exe" '+$entry[1]+"`r`n"+'exit /b %errorlevel%'+"`r`n"
    [IO.File]::WriteAllText((Join-Path $stage $entry[0]),$command,[Text.Encoding]::ASCII)
}
New-Item -ItemType Directory -Force -Path $target | Out-Null
$zip=Join-Path $target 'PanelSwitch-lite-win-x64.zip'
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -Force
Copy-Item -LiteralPath (Join-Path $stage 'PanelSwitch.exe') -Destination (Join-Path $target 'PanelSwitch-lite.exe') -Force
$hashes=foreach($name in @('PanelSwitch-lite.exe','PanelSwitch-lite-win-x64.zip')){(Get-FileHash -LiteralPath (Join-Path $target $name)).Hash.ToLower()+'  '+$name}
[IO.File]::WriteAllLines((Join-Path $target 'SHA256SUMS.txt'),$hashes,[Text.Encoding]::ASCII)
Write-Output "轻量版完成：$target"
