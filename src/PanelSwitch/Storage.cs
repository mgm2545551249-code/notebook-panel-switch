using System;
using System.Diagnostics;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using System.Threading;

namespace PanelSwitch;
internal static class Storage {
    internal static string Root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"内屏开关");
    internal static readonly JsonSerializerOptions Json=new(){WriteIndented=true,IncludeFields=true,Encoder=JavaScriptEncoder.Create(UnicodeRanges.All)};
    internal static string SettingsPath=>Path.Combine(Root,"设置.json");
    internal static string LeasePath=>Path.Combine(Root,"恢复记录.json");
    internal static string LogPath=>Path.Combine(Root,"运行日志.jsonl");
    static readonly object LogLock=new();
    public static void Save<T>(string path,T value) {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try {File.WriteAllText(temp,JsonSerializer.Serialize(value,Json));File.Move(temp,path,true);}
        finally{if(File.Exists(temp))File.Delete(temp);}
    }
    public static T? Load<T>(string path){try{return JsonSerializer.Deserialize<T>(File.ReadAllText(path),Json);}catch(FileNotFoundException){return default;}catch(DirectoryNotFoundException){return default;}}
    public static void Log(string action,object? details=null) {
        lock(LogLock){try{Directory.CreateDirectory(Root);if(File.Exists(LogPath)&&new FileInfo(LogPath).Length>4*1024*1024)File.Move(LogPath,LogPath+".previous",true);
        File.AppendAllText(LogPath,JsonSerializer.Serialize(new{时间=DateTimeOffset.Now,进程=Environment.ProcessId,事件=action,数据=details},new JsonSerializerOptions{Encoder=Json.Encoder})+Environment.NewLine);}catch{}}
    }
    public static Process Launch(params string[] args) {
        var start=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,CreateNoWindow=true};
        foreach(string a in args)start.ArgumentList.Add(a);
        return Process.Start(start)??throw new InvalidOperationException("启动恢复进程失败。");
    }
}
public sealed class Settings {
    public uint Modifiers {get;set;}=3;
    public uint VirtualKey {get;set;}=0x79;
    public bool StartWithWindows {get;set;}
    public bool KeepAwake {get;set;}=true;
    public bool AllowVirtualDisplay {get;set;}=true;
    public bool EnablePowerButton {get;set;}=true;
    public string? ConfirmedDdcDevice {get;set;}
    public string? ConfirmedDdcDriver {get;set;}
    public static Settings Read(){try{return Storage.Load<Settings>(Storage.SettingsPath)??new();}catch(Exception e){Storage.Log("配置读取失败",e.Message);return new();}}
}
public sealed class RecoveryLease {
    public string Id {get;set;}=Guid.NewGuid().ToString("N");
    public int OwnerPid {get;set;}
    public long OwnerStarted {get;set;}
    public string BootId {get;set;}="";
    public string Method {get;set;}="";
    public string DevicePath {get;set;}="";
    public DisplaySnapshot Original {get;set;}=new();
    public WindowPlacement[] Windows {get;set;}=Array.Empty<WindowPlacement>();
    public DateTimeOffset? Deadline {get;set;}
    public string Heartbeat {get;set;}="";
    public string Ready {get;set;}="";
    public PowerPolicySnapshot? PowerPolicy {get;set;}
}
internal static class Recovery {
    internal static string BootId=>((DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()-Environment.TickCount64)/10000).ToString();
    public static bool Alive(RecoveryLease lease) {
        try {using var p=Process.GetProcessById(lease.OwnerPid);return !p.HasExited&&p.StartTime.ToUniversalTime().Ticks==lease.OwnerStarted;}catch{return false;}
    }
    public static void Restore(RecoveryLease lease) {
        // 显示器热插拔或重启后，旧适配器标识可能失效，改用当前内屏布局恢复。
        if(lease.Method=="布局") {
            try{DisplayNative.Apply(lease.Original);}catch{DisplayNative.EmergencyInternal();}
            WindowPlacement.RestoreAll(lease.Windows);
            DesktopRefresh.AfterLayoutRestored();
        } else if(lease.Method=="内屏电源") {
            var target=System.Linq.Enumerable.FirstOrDefault(System.Linq.Enumerable.Select(DisplayNative.Query().Paths,DisplayNative.Describe),p=>string.Equals(p.DevicePath,lease.DevicePath,StringComparison.OrdinalIgnoreCase));
            if(target!=null)DisplayNative.DdcPower(target.GdiName,1);else DisplayNative.EmergencyInternal();
        } else if(lease.Method=="常规诊断") Native.MonitorPower(-1);
        if(lease.PowerPolicy!=null)PowerPolicy.Restore(lease.PowerPolicy);
        Storage.Log("恢复成功",new{lease.Id,lease.Method});
    }
    public static bool RestorePending(bool force=false) {
        var lease=Storage.Load<RecoveryLease>(Storage.LeasePath);if(lease==null)return true;
        if(!force&&Alive(lease))return false;
        Complete(lease,Storage.LeasePath);return true;
    }
    public static void Complete(RecoveryLease lease,string path) {
        using var gate=new Mutex(false,"Local\\PanelSwitch.Recovery");bool held=false;
        try{try{held=gate.WaitOne(TimeSpan.FromSeconds(25));}catch(AbandonedMutexException){held=true;}
            if(!held)throw new TimeoutException("另一个恢复进程尚未完成。");
            if(Storage.Load<RecoveryLease>(path)?.Id!=lease.Id)return;
            Restore(lease);if(Storage.Load<RecoveryLease>(path)?.Id==lease.Id)File.Delete(path);
        }finally{if(held)gate.ReleaseMutex();}
    }
    public static int Watch(string leasePath,string id) {
        var lease=Storage.Load<RecoveryLease>(leasePath);
        if(lease==null||lease.Id!=id)return 2;
        File.WriteAllText(lease.Ready,"已就绪");
        while(true) {
            Thread.Sleep(500);
            var current=Storage.Load<RecoveryLease>(leasePath);
            if(current==null||current.Id!=id)return 0;
            bool stale=!File.Exists(lease.Heartbeat)||DateTime.UtcNow-File.GetLastWriteTimeUtc(lease.Heartbeat)>TimeSpan.FromSeconds(10);
            if(!Alive(lease)||stale||(lease.Deadline.HasValue&&DateTimeOffset.Now>=lease.Deadline)) {
                try{Complete(lease,leasePath);return 0;}
                catch(Exception e){Storage.Log("独立恢复重试",e.Message);Thread.Sleep(1000);}
            }
        }
    }
}
