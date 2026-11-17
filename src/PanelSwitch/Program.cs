using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
namespace PanelSwitch;
static class Program {
    [STAThread] static int Main(string[] args) {
        try {
            Directory.CreateDirectory(Storage.Root);
            if(args.Contains("--environment-check")){Storage.Save(args[1],EnvironmentCheck.Collect());return 0;}
            if(args.Contains("--calibrate-virtual-dpi"))return IntegrationTests.Run(args[1],2,false,true);
            if(args.Contains("--display-parameters")){Storage.Save(args[1],DisplayNative.Parameters());return 0;}
            if(args.Contains("--desktop-status")){Storage.Save(args[1],DesktopRefresh.Describe());return 0;}
            if(args.Contains("--desktop-refresh-worker"))return DesktopRefresh.RunWorker(args[1]);
            if(args.Contains("--desktop-refresh-test")){var before=DesktopRefresh.Describe();int count=DesktopRefresh.RepaintOnce();Storage.Save(args[1],new{刷新前=before,重绘视图数=count,刷新后=DesktopRefresh.Describe()});return count>0?0:1;}
            if(args.Contains("--enable-startup")){Install.SetStartup(true);var settings=Settings.Read();settings.StartWithWindows=true;Storage.Save(Storage.SettingsPath,settings);return 0;}
            if(args.Contains("--stop")){Install.StopRunning();return 0;}
            if(args.Contains("--desktop-refresh")){DesktopRefresh.AfterLayoutRestored();return 0;}
            if(args.Contains("--power-agent"))return PowerAgent.Run(args[1],int.Parse(args[2]),long.Parse(args[3]));
            if(args.Contains("--power-agent-test"))return IntegrationTests.TestPowerAgent(args[1]).GetAwaiter().GetResult();
            if(args.Contains("--watchdog"))return Recovery.Watch(args[1],args[2]);
            if(args.Contains("--diagnose")){Storage.Save(args.Length>1?args[1]:Path.Combine(Storage.Root,"诊断.json"),Diagnostics.Collect().GetAwaiter().GetResult());return 0;}
            if(args.Contains("--self-test"))return SelfTests.Run(args.Length>1?args[1]:Path.Combine(Storage.Root,"自动测试.json"));
            if(args.Contains("--integration-test"))return IntegrationTests.Run(args[1],args.Length>2?int.Parse(args[2]):1);
            if(args.Contains("--baseline-test"))return IntegrationTests.Run(args[1],1,true);
            if(args.Contains("--crash-test"))return IntegrationTests.Crash(args[1]);
            if(args.Contains("--timeout-test"))return IntegrationTests.Timeout(args[1]);
            if(args.Contains("--internal-only")){DisplayNative.InternalOnly();return 0;}
            if(args.Contains("--install-vdd"))return VddInstaller.InstallElevated().GetAwaiter().GetResult();
            if(args.Contains("--remove-vdd"))return VddInstaller.InstallElevated(true).GetAwaiter().GetResult();
            if(args.Contains("--install"))return Install.Run();
            if(args.Contains("--uninstall"))return Install.Run(true);
            if(args.Contains("--recover")){Recovery.RestorePending(true);PowerAgent.RestoreStale();Native.MonitorPower(-1);return 0;}
            using var mutex=new Mutex(true,"Local\\PanelSwitch.Main",out bool first);if(!first){if(!args.Contains("--background"))MessageBox.Show("内屏开关已在托盘运行。","内屏开关");return 0;}
            Recovery.RestorePending();PowerAgent.RestoreStale();
            try{Install.SynchronizeStartup();}catch(Exception e){Storage.Log("登录启动状态核对失败",e.ToString());}
            Storage.Log("主程序启动",new{后台模式=args.Contains("--background"),路径=Environment.ProcessPath,登录启动=Install.StartupEnabled()});
            var app=new System.Windows.Application();
            app.DispatcherUnhandledException+=(_,e)=>{Storage.Log("界面异常",e.Exception.ToString());try{Recovery.RestorePending(true);}catch{}e.Handled=true;MessageBox.Show(e.Exception.Message,"内屏开关");};
            AppDomain.CurrentDomain.UnhandledException+=(_,e)=>Storage.Log("进程异常",e.ExceptionObject.ToString());
            app.Run(new MainWindow(args.Contains("--smoke-test"),args.Contains("--background")));return 0;
        } catch(Exception e){Storage.Log("启动失败",e.ToString());if(!args.Any(x=>x.StartsWith("--",StringComparison.Ordinal)))MessageBox.Show(e.Message,"内屏开关启动失败");return 1;}
    }
}
