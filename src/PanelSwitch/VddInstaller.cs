using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Threading.Tasks;
namespace PanelSwitch;
internal static class VddInstaller {
    static string DriverRoot=>Path.Combine(AppContext.BaseDirectory,"驱动");
    static string Script=>Path.Combine(DriverRoot,"安装虚拟显示.ps1");
    static string LogFile=>Path.Combine(Storage.Root,"驱动安装日志.txt");
    internal const string ReadyMessage="虚拟显示驱动已安装且可用，无需重复安装。";
    internal static bool IsReady(){
        try{return DisplayNative.Query(true).Paths.Select(DisplayNative.Describe).Any(x=>x.Available&&DisplayNative.IsManagedVirtual(x));}
        catch(Exception e){Storage.Log("检查虚拟显示驱动失败",e.Message);return false;}
    }
    internal static bool HasPackage()=>File.Exists(Script)&&new[]{"mttvdd.cat","MttVDD.dll","MttVDD.inf","vdd_settings.xml"}.All(name=>File.Exists(Path.Combine(DriverRoot,"VirtualDisplayDriver",name)));
    internal static async Task<string> LaunchInstall() {
        if(IsReady())return ReadyMessage;
        if(!HasPackage())throw new FileNotFoundException("当前文件夹未包含驱动安装组件。请解压带驱动的完整版，再在其中运行本工具。");
        try{
            using var process=Process.Start(new ProcessStartInfo(Environment.ProcessPath!,"--install-vdd"){UseShellExecute=true,Verb="runas"})??throw new InvalidOperationException("管理员安装程序未启动。");
            await process.WaitForExitAsync();
            if(process.ExitCode==3010)return "驱动安装步骤已完成，重启电脑后再检测。";
            if(process.ExitCode!=0)throw new InvalidOperationException($"驱动安装未完成（退出码 {process.ExitCode}）。可在日志目录查看“驱动安装日志.txt”。");
            for(int i=0;i<8;i++){if(IsReady())return "虚拟显示驱动已就绪。";await Task.Delay(350);}
            return "安装程序已完成，但系统还未报告可用的虚拟显示器。请重新检测，必要时查看驱动安装日志。";
        }catch(Win32Exception e)when(e.NativeErrorCode==1223){return "已取消管理员授权，未安装驱动。";}
    }
    internal static async Task<int> InstallElevated(bool remove=false) {
        if(!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))return 5;
        if(remove)Recovery.RestorePending(true);
        try{
            if(!File.Exists(Script)||(!remove&&!HasPackage()))throw new FileNotFoundException("驱动安装文件不完整。");
            var start=new ProcessStartInfo("powershell.exe"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
            foreach(var arg in new[]{"-NoProfile","-ExecutionPolicy","Bypass","-File",Script})start.ArgumentList.Add(arg);
            if(remove)start.ArgumentList.Add("-Remove");
            using var process=Process.Start(start)??throw new InvalidOperationException("未启动驱动脚本。");
            var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            await File.WriteAllTextAsync(LogFile,await stdout+await stderr+"\n退出码："+process.ExitCode);
            return process.ExitCode;
        }catch(Exception e){File.WriteAllText(LogFile,e.Message);return 1;}
    }
}
