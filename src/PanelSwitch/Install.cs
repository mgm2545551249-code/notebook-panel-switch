using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
namespace PanelSwitch;
internal static class Install {
    const string RunKey=@"Software\Microsoft\Windows\CurrentVersion\Run";
    internal static string Target=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs","内屏开关");
    internal static string StartupExecutable=>File.Exists(Path.Combine(Target,"PanelSwitch.exe"))?Path.Combine(Target,"PanelSwitch.exe"):Environment.ProcessPath!;
    internal static string StartupCommand(string executable){if(!Path.IsPathFullyQualified(executable)||executable.Contains('"')||!executable.EndsWith(".exe",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("启动程序路径无效。");return "\""+executable+"\" --background";}
    internal static string? ReadStartupCommand(){using var key=Registry.CurrentUser.OpenSubKey(RunKey);return key?.GetValue("PanelSwitch") as string;}
    internal static bool StartupDisabled(){using var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run");return key?.GetValue("PanelSwitch") is byte[] bytes&&bytes.Length>0&&(bytes[0]==3||bytes[0]==7);}
    internal static bool StartupRegistered()=>string.Equals(ReadStartupCommand(),StartupCommand(StartupExecutable),StringComparison.OrdinalIgnoreCase);
    internal static bool StartupEnabled()=>StartupRegistered()&&!StartupDisabled();
    internal static void SetStartup(bool enabled) {
        if(enabled&&StartupDisabled())throw new InvalidOperationException("Windows已禁用此启动项，请在系统设置的“应用→启动”中启用“PanelSwitch”，再重试。");
        using var key=Registry.CurrentUser.CreateSubKey(RunKey);
        if(enabled)key.SetValue("PanelSwitch",StartupCommand(StartupExecutable));else key.DeleteValue("PanelSwitch",false);
        if(enabled?!StartupEnabled():ReadStartupCommand()!=null)throw new InvalidOperationException("启动项保存后校验失败，请重试。");
        Storage.Log("登录启动设置已校验",new{启用=enabled,启动命令=ReadStartupCommand()});
    }
    internal static void SynchronizeStartup(){var settings=Settings.Read();if(settings.StartWithWindows&&!StartupRegistered()&&!StartupDisabled())SetStartup(true);bool actual=StartupEnabled();if(settings.StartWithWindows!=actual||!File.Exists(Storage.SettingsPath)){settings.StartWithWindows=actual;Storage.Save(Storage.SettingsPath,settings);}Storage.Log("登录启动状态核对",new{启用=actual,系统禁用=StartupDisabled(),启动命令=ReadStartupCommand()});}
    internal static int Run(bool uninstall=false) {
        StopRunning();
        if(uninstall){
            SetStartup(false);Recovery.RestorePending(true);string shortcut=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),"Programs","内屏开关.lnk");if(File.Exists(shortcut))File.Delete(shortcut);
            if(Directory.Exists(Target)&&!AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar).Equals(Target,StringComparison.OrdinalIgnoreCase))Directory.Delete(Target,true);
            return 0;
        }
        Directory.CreateDirectory(Target);string source=AppContext.BaseDirectory;
        if(!Path.GetFullPath(source).TrimEnd(Path.DirectorySeparatorChar).Equals(Target,StringComparison.OrdinalIgnoreCase))foreach(var file in Directory.GetFiles(source,"*",SearchOption.AllDirectories)){
            string destination=Path.Combine(Target,Path.GetRelativePath(source,file));Directory.CreateDirectory(Path.GetDirectoryName(destination)!);File.Copy(file,destination,true);
        }
        string exe=Path.Combine(Target,"PanelSwitch.exe");Type type=Type.GetTypeFromProgID("WScript.Shell")!;dynamic shell=Activator.CreateInstance(type)!;
        dynamic link=shell.CreateShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),"Programs","内屏开关.lnk"));link.TargetPath=exe;link.IconLocation=exe+",0";link.WorkingDirectory=Target;link.Save();
        System.Runtime.InteropServices.Marshal.FinalReleaseComObject(link);System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
        if(Settings.Read().StartWithWindows)SetStartup(true);
        Process.Start(new ProcessStartInfo(exe){UseShellExecute=true});return 0;
    }
    internal static void StopRunning(){var window=Native.FindWindow(null,"内屏开关");if(window==IntPtr.Zero)return;Native.GetWindowThreadProcessId(window,out uint pid);using var process=Process.GetProcessById((int)pid);Native.PostMessage(window,Native.ExitMessage,IntPtr.Zero,IntPtr.Zero);if(!process.WaitForExit(15000))throw new Exception("常驻程序尚未退出，请先从托盘退出再安装或卸载。");}
}
