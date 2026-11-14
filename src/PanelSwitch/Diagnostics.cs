using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace PanelSwitch;
internal static class Diagnostics {
    internal static string DriverFingerprint() {
        var file=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"drivers","nvlddmkm.sys");
        try{return Environment.OSVersion.Version+"/"+FileVersionInfo.GetVersionInfo(file).FileVersion;}catch{return Environment.OSVersion.Version.ToString();}
    }
    internal static async Task<string> RunCommand(string file,params string[] args) {
        var si=new ProcessStartInfo(file){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};foreach(var a in args)si.ArgumentList.Add(a);
        using var p=Process.Start(si)??throw new InvalidOperationException("诊断进程启动失败");
        var stdout=p.StandardOutput.ReadToEndAsync();var stderr=p.StandardError.ReadToEndAsync();
        try{await p.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(25));}catch{try{p.Kill(true);}catch{}throw;}
        return await stdout+await stderr+"\n退出码："+p.ExitCode;
    }
    internal static async Task<object> Collect(bool includeWmi=true) {
        var active=DisplayNative.Query();var all=DisplayNative.Query(true);
        var panels=active.Paths.Select(DisplayNative.Describe).ToArray();
        var ddc=panels.Where(x=>x.Internal).SelectMany(x=>DisplayNative.ProbeDdc(x.GdiName)).ToArray();
        string wmi="未运行";
        if(includeWmi)try{wmi=await RunCommand("powershell.exe","-NoProfile","-Command","$ErrorActionPreference='Continue'; Get-CimInstance Win32_ComputerSystem | Select-Object Manufacturer,Model | Format-List; Get-CimInstance Win32_VideoController | Select-Object Name,DriverVersion | Format-List; Get-CimInstance -Namespace root/wmi WmiMonitorConnectionParams | Format-List InstanceName,Active,VideoOutputTechnology; Get-PnpDevice -PresentOnly | Where-Object {$_.FriendlyName -match 'ACPI.*按钮|ACPI.*button|Virtual Display'} | Format-List FriendlyName,InstanceId; Get-CimInstance -Namespace root/wmi LENOVO_PANEL_METHOD -ErrorAction Continue | Format-List Active,InstanceName; powercfg /qh SCHEME_CURRENT SUB_BUTTONS");}catch(Exception e){wmi=e.Message;}
        return new{时间=DateTimeOffset.Now,系统=Environment.OSVersion.VersionString,驱动指纹=DriverFingerprint(),结构长度=new{路径=Marshal.SizeOf<DisplayPath>(),模式=Marshal.SizeOf<DisplayMode>()},当前显示器=panels,全部显示路径=all.Paths.Select(DisplayNative.Describe).ToArray(),内屏电源能力=ddc,电源按钮策略=PowerPolicy.Read(),原始输入设备=PowerButton.Inventory(),系统诊断=wmi,
        验收=new{实体电源键="使用 ACPI 事件24、版本0、EventMask电源位，真实按压需现场验证",单内屏独立关闭=panels.Any(x=>Controller.IsVirtual(x))?"已安装虚拟显示器，可使用布局路径；发光状态仍需现场确认":ddc.Any(x=>x.PowerReadable)?"电源接口可读，仍需现场验证":"本机常规独立电源接口读取未通过",外接屏持续操作=panels.Any(x=>!x.Internal&&!Controller.IsVirtual(x))?"当前连接物理外接屏，可进入实测":"当前没有连接物理外接屏，尚未实测"}};
    }
}
