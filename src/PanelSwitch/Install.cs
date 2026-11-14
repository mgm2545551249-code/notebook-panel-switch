using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
namespace PanelSwitch;
internal static class Install {
    internal static string Target=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs","内屏开关");
    internal static void SetStartup(bool enabled) {
        using var key=Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        if(enabled)key.SetValue("PanelSwitch","\""+Environment.ProcessPath+"\" --background");else key.DeleteValue("PanelSwitch",false);
    }
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
        Process.Start(new ProcessStartInfo(exe){UseShellExecute=true});return 0;
    }
    internal static void StopRunning(){var window=Native.FindWindow(null,"内屏开关");if(window==IntPtr.Zero)return;Native.GetWindowThreadProcessId(window,out uint pid);using var process=Process.GetProcessById((int)pid);Native.PostMessage(window,Native.ExitMessage,IntPtr.Zero,IntPtr.Zero);if(!process.WaitForExit(15000))throw new Exception("常驻程序尚未退出，请先从托盘退出再安装或卸载。");}
}
