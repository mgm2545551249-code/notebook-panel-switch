param([switch]$Remove)
$ErrorActionPreference='Stop'
$taskRoot=Join-Path $env:ProgramData 'PanelSwitch'
New-Item -ItemType Directory -Force -Path $taskRoot | Out-Null
Start-Transcript -Path (Join-Path $taskRoot '虚拟显示驱动安装.log') -Append | Out-Null
try {
    $identity=[Security.Principal.WindowsIdentity]::GetCurrent()
    if(-not ([Security.Principal.WindowsPrincipal]::new($identity)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){throw '需要管理员权限安装显示驱动。'}
    if($Remove){
        $devices=@(Get-PnpDevice -PresentOnly | Where-Object {$_.InstanceId -like 'ROOT\DISPLAY\*' -or $_.InstanceId -like 'ROOT\MTTVDD\*'})
        foreach($device in $devices){$ids=(Get-PnpDeviceProperty -InstanceId $device.InstanceId -KeyName DEVPKEY_Device_HardwareIds -ErrorAction SilentlyContinue).Data;if($ids -contains 'Root\MttVDD'){& pnputil.exe /remove-device $device.InstanceId;if($LASTEXITCODE -ne 0){throw '移除虚拟显示设备失败。'}}}
        Write-Output '虚拟显示设备已移除。驱动包保留在驱动仓库，不改动其他显示驱动。'
        exit 0
    }
    $package=Join-Path $PSScriptRoot 'VirtualDisplayDriver'
    $expected=@{'mttvdd.cat'='08A0093FC9B2E32B287A6F8A77CA4DE0A31830D29FC33D2B13A918DC859468F6';'MttVDD.dll'='C9CA837F57A98FBD43BC416A7F535A95843626E7759EAF85CF0CD7CE334DBB05';'MttVDD.inf'='550D211FE481E74DFE3F9D724ED78BE48B3A9113405965D683D9373E8D672F5D'}
    foreach($name in $expected.Keys){if((Get-FileHash -LiteralPath (Join-Path $package $name)).Hash -ne $expected[$name]){throw "驱动文件校验失败：$name"}}
    $signature=Get-AuthenticodeSignature -LiteralPath (Join-Path $package 'mttvdd.cat')
    if($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Thumbprint -ne '3CF8CF26D8BA266C3A483AB7D26D4A818E317D76'){throw '驱动签名校验未通过。'}
    $configDir=Join-Path $env:SystemDrive 'VirtualDisplayDriver'
    New-Item -ItemType Directory -Force -Path $configDir | Out-Null
    $configPath=Join-Path $configDir 'vdd_settings.xml'
    if(-not (Test-Path -LiteralPath $configPath)){Copy-Item -LiteralPath (Join-Path $package 'vdd_settings.xml') -Destination $configPath}
    $store=[Security.Cryptography.X509Certificates.X509Store]::new('TrustedPublisher','LocalMachine')
    $store.Open('ReadWrite');try{$store.Add($signature.SignerCertificate)}finally{$store.Close()}
    & pnputil.exe /add-driver (Join-Path $package 'MttVDD.inf') /install
    if($LASTEXITCODE -ne 0 -and $LASTEXITCODE -ne 3010){throw "驱动入库失败，退出码：$LASTEXITCODE"}
    Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
public static class PanelVirtualDevice {
 [StructLayout(LayoutKind.Sequential)] struct Info {public uint Size;public Guid Class;public uint DevInst;public IntPtr Reserved;}
 [DllImport("setupapi.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern IntPtr SetupDiCreateDeviceInfoList(ref Guid cls,IntPtr hwnd);
 [DllImport("setupapi.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool SetupDiCreateDeviceInfo(IntPtr list,string name,ref Guid cls,string description,IntPtr hwnd,uint flags,ref Info info);
 [DllImport("setupapi.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool SetupDiSetDeviceRegistryProperty(IntPtr list,ref Info info,uint property,byte[] data,uint length);
 [DllImport("setupapi.dll",SetLastError=true)] static extern bool SetupDiCallClassInstaller(uint function,IntPtr list,ref Info info);
 [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr list);
 [DllImport("newdev.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool UpdateDriverForPlugAndPlayDevices(IntPtr hwnd,string id,string inf,uint flags,out bool reboot);
 public static void Create(string inf) {
   Guid cls=new Guid("4d36e968-e325-11ce-bfc1-08002be10318");IntPtr list=SetupDiCreateDeviceInfoList(ref cls,IntPtr.Zero);
   if(list==new IntPtr(-1))throw new Win32Exception();
   var info=new Info{Size=(uint)Marshal.SizeOf(typeof(Info))};bool registered=false;
   try {
     if(!SetupDiCreateDeviceInfo(list,"Display",ref cls,"内屏开关虚拟显示",IntPtr.Zero,1,ref info))throw new Win32Exception();
     byte[] id=System.Text.Encoding.Unicode.GetBytes("Root\\MttVDD\0\0");
     if(!SetupDiSetDeviceRegistryProperty(list,ref info,1,id,(uint)id.Length))throw new Win32Exception();
     if(!SetupDiCallClassInstaller(0x19,list,ref info))throw new Win32Exception();registered=true;
     bool reboot;if(!UpdateDriverForPlugAndPlayDevices(IntPtr.Zero,"Root\\MttVDD",inf,1,out reboot))throw new Win32Exception();
     registered=false;
   } finally {if(registered)SetupDiCallClassInstaller(5,list,ref info);SetupDiDestroyDeviceInfoList(list);}
 }
}
'@
    $existing=@(Get-PnpDevice -PresentOnly | Where-Object {$_.FriendlyName -eq 'Virtual Display Driver'})
    if($existing.Count -eq 0){[PanelVirtualDevice]::Create((Join-Path $package 'MttVDD.inf'))}
    Get-PnpDevice -PresentOnly -Class Display | Format-List Status,FriendlyName,InstanceId
    Write-Output '虚拟显示驱动安装流程已完成。'
    exit 0
} catch {Write-Output $_.Exception.ToString();exit 1} finally {Stop-Transcript | Out-Null}
