using System;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Threading.Tasks;
namespace PanelSwitch;
internal static class VddInstaller {
    internal static async Task LaunchInstall() {
        if(!File.Exists(Path.Combine(AppContext.BaseDirectory,"驱动","安装虚拟显示.ps1")))throw new FileNotFoundException("缺少虚拟显示驱动安装组件。");
        using var process=Process.Start(new ProcessStartInfo(Environment.ProcessPath!,"--install-vdd"){UseShellExecute=true,Verb="runas"})??throw new InvalidOperationException("管理员安装程序未启动。");
        await process.WaitForExitAsync();if(process.ExitCode!=0)throw new InvalidOperationException("驱动安装未完成，请查看驱动安装日志。退出码："+process.ExitCode);
    }
    internal static async Task<int> InstallElevated(bool remove=false) {
        if(!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))return 5;
        if(remove)Recovery.RestorePending(true);
        string script=Path.Combine(AppContext.BaseDirectory,"驱动","安装虚拟显示.ps1");
        string result=remove?await Diagnostics.RunCommand("powershell.exe","-NoProfile","-ExecutionPolicy","Bypass","-File",script,"-Remove"):await Diagnostics.RunCommand("powershell.exe","-NoProfile","-ExecutionPolicy","Bypass","-File",script);
        File.WriteAllText(Path.Combine(Storage.Root,"驱动安装日志.txt"),result);return result.TrimEnd().EndsWith("退出码：0",StringComparison.Ordinal)?0:1;
    }
}
