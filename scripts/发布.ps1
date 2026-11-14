param([switch]$IncludeVirtualDriver)
$ErrorActionPreference='Stop'
$repoRoot=Split-Path $PSScriptRoot -Parent
$target=Join-Path $repoRoot 'artifacts/发行版'
& dotnet publish (Join-Path $repoRoot 'src/PanelSwitch/PanelSwitch.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $target --nologo
if($LASTEXITCODE -ne 0){throw '发布失败。'}
Copy-Item -LiteralPath (Join-Path $repoRoot 'docs/第三方许可') -Destination (Join-Path $target '许可') -Recurse -Force
foreach($entry in @(@('安装.cmd','--install'),@('恢复显示.cmd','--recover'),@('卸载.cmd','--uninstall'))){
    $command='@echo off'+"`r`n"+'"%~dp0PanelSwitch.exe" '+$entry[1]+"`r`n"+'exit /b %errorlevel%'+"`r`n"
    [IO.File]::WriteAllText((Join-Path $target $entry[0]),$command,[Text.Encoding]::ASCII)
}
if($IncludeVirtualDriver){
    $driverRoot=Join-Path $target '驱动'
    New-Item -ItemType Directory -Force -Path $driverRoot | Out-Null
    $zip=Join-Path $driverRoot '虚拟显示驱动.zip'
    Invoke-WebRequest 'https://github.com/VirtualDrivers/Virtual-Display-Driver/releases/download/25.7.23/VirtualDisplayDriver-x86.Driver.Only.zip' -OutFile $zip
    Expand-Archive -LiteralPath $zip -DestinationPath $driverRoot -Force
    $expected=@{'mttvdd.cat'='08A0093FC9B2E32B287A6F8A77CA4DE0A31830D29FC33D2B13A918DC859468F6';'MttVDD.dll'='C9CA837F57A98FBD43BC416A7F535A95843626E7759EAF85CF0CD7CE334DBB05';'MttVDD.inf'='550D211FE481E74DFE3F9D724ED78BE48B3A9113405965D683D9373E8D672F5D'}
    foreach($name in $expected.Keys){if((Get-FileHash -LiteralPath (Join-Path $driverRoot "VirtualDisplayDriver/$name")).Hash -ne $expected[$name]){throw "驱动文件哈希不一致：$name"}}
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot '安装虚拟显示.ps1') -Destination (Join-Path $driverRoot '安装虚拟显示.ps1') -Force
    $remove='@echo off'+"`r`n"+'powershell.exe -NoProfile -Command "Start-Process -FilePath ''%~dp0PanelSwitch.exe'' -ArgumentList ''--remove-vdd'' -Verb RunAs -WindowStyle Hidden -Wait"'+"`r`n"+'exit /b %errorlevel%'+"`r`n"
    [IO.File]::WriteAllText((Join-Path $target '卸载虚拟显示驱动.cmd'),$remove,[Text.Encoding]::ASCII)
}
Write-Output "发布完成：$target"
