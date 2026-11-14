using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace PanelSwitch;
public enum PanelState { 开启,正在关闭,关闭,正在恢复,故障 }
internal interface IPanelBackend {
    string Name{get;}
    RecoveryLease Prepare();
    void Off(RecoveryLease lease);
    void On(RecoveryLease lease);
    bool IsOff(RecoveryLease lease);
}
internal sealed class TopologyBackend:IPanelBackend {
    public string Name=>"布局";
    public RecoveryLease Prepare(){var snapshot=DisplayNative.Query();DisplayNative.Validate(DisplayNative.ExternalOnly(snapshot));return new(){Method=Name,Original=snapshot,Windows=WindowPlacement.Capture()};}
    public void Off(RecoveryLease l)=>DisplayNative.Apply(DisplayNative.ExternalOnly(l.Original));
    public void On(RecoveryLease l)=>Recovery.Restore(l);
    public bool IsOff(RecoveryLease l)=>!DisplayNative.Query().Paths.Any(p=>DisplayNative.IsInternal(p.Target.Technology));
}
internal sealed class DdcBackend:IPanelBackend {
    readonly DisplayEndpoint panel;public DdcBackend(DisplayEndpoint panel){this.panel=panel;}
    public string Name=>"内屏电源";
    public RecoveryLease Prepare()=>new(){Method=Name,Original=DisplayNative.Query(),DevicePath=panel.DevicePath};
    public void Off(RecoveryLease l)=>DisplayNative.DdcPower(panel.GdiName,4);
    public void On(RecoveryLease l)=>Recovery.Restore(l);
    public bool IsOff(RecoveryLease l){var c=DisplayNative.ProbeDdc(panel.GdiName);return c.Count==1&&c[0].PowerReadable&&c[0].CurrentPower==4;}
}
internal sealed class BaselineBackend:IPanelBackend {
    public string Name=>"常规诊断";
    public RecoveryLease Prepare()=>new(){Method=Name,Original=DisplayNative.Query()};
    public void Off(RecoveryLease l)=>Native.MonitorPower(2);
    public void On(RecoveryLease l)=>Native.MonitorPower(-1);
    public bool IsOff(RecoveryLease l)=>true;
}
internal sealed class Controller {
    public PanelState State{get;private set;}=PanelState.开启;
    public string Message{get;private set;}="正在检测内屏控制能力。";
    public event Action? Changed;
    IPanelBackend? backend;RecoveryLease? lease;bool busy;
    public bool IsBusy=>busy;
    public bool HasRecovery=>lease!=null;
    readonly Settings settings;
    public Controller(Settings settings){this.settings=settings;}
    public void Refresh() {
        if(lease!=null||busy)return;
        var endpoints=DisplayNative.Query().Paths.Select(DisplayNative.Describe).ToArray();
        var panels=endpoints.Where(x=>x.Internal).ToArray();
        backend=null;
        if(panels.Length!=1){Message="未找到唯一内屏，请检查显示设备。";Changed?.Invoke();return;}
        var physicalExternal=endpoints.Any(x=>!x.Internal&&!IsVirtual(x));
        bool virtualExternal=settings.AllowVirtualDisplay&&endpoints.Any(x=>!x.Internal);
        if(physicalExternal||virtualExternal){backend=new TopologyBackend();Message=physicalExternal?"关闭内屏后，外接屏和键鼠继续工作。":"虚拟显示器已就绪，可独立关闭笔记本内屏。";}
        else if(settings.ConfirmedDdcDevice==panels[0].DevicePath&&settings.ConfirmedDdcDriver==Diagnostics.DriverFingerprint()){
            var cap=DisplayNative.ProbeDdc(panels[0].GdiName);
            if(cap.Count==1&&cap[0].PowerReadable){backend=new DdcBackend(panels[0]);Message="使用已现场验证的内屏电源接口。";}
        }
        if(backend==null)Message="当前仅有内屏，独立电源接口尚未通过验证。连接外接屏后可使用布局方式；单内屏模式需要可用的独立接口或虚拟显示驱动。";
        Changed?.Invoke();
    }
    internal static bool IsVirtual(DisplayEndpoint x)=>x.Technology==16||x.Technology==17||x.Name.Contains("VDD",StringComparison.OrdinalIgnoreCase)||x.DevicePath.Contains("MTT",StringComparison.OrdinalIgnoreCase)||x.Name.Contains("Virtual",StringComparison.OrdinalIgnoreCase);
    public async Task Toggle(int? seconds=null,IPanelBackend? testBackend=null) {
        if(busy)return;busy=true;
        try {
            if(lease!=null){await Restore();return;}
            RefreshForToggle();var selected=testBackend??backend;
            if(selected==null)throw new InvalidOperationException(Message);
            State=PanelState.正在关闭;Changed?.Invoke();
            var candidate=selected.Prepare();
            using(var process=Process.GetCurrentProcess()){candidate.OwnerPid=process.Id;candidate.OwnerStarted=process.StartTime.ToUniversalTime().Ticks;}
            candidate.BootId=Recovery.BootId;
            candidate.Deadline=seconds.HasValue?DateTimeOffset.Now.AddSeconds(seconds.Value):null;
            candidate.Heartbeat=Path.Combine(Storage.Root,candidate.Id+".heartbeat");candidate.Ready=Path.Combine(Storage.Root,candidate.Id+".ready");
            File.WriteAllText(candidate.Heartbeat,"存活");Storage.Save(Storage.LeasePath,candidate);
            lease=candidate;backend=selected;
            using var guard=Storage.Launch("--watchdog",Storage.LeasePath,candidate.Id);
            var watch=Stopwatch.StartNew();
            while(!File.Exists(candidate.Ready)&&watch.Elapsed<TimeSpan.FromSeconds(8))await Task.Delay(100);
            if(!File.Exists(candidate.Ready))throw new InvalidOperationException("独立恢复进程尚未就绪，已取消关屏。");
            if(settings.KeepAwake)Native.Awake(true);
            selected.Off(candidate);
            if(!selected.IsOff(candidate))throw new InvalidOperationException("关屏命令后状态校验未通过，正在恢复。");
            State=PanelState.关闭;Message=seconds.HasValue?$"内屏测试中，最多 {seconds} 秒后恢复。":"内屏已关闭，使用快捷键恢复。";Storage.Log("关屏成功",new{candidate.Method,candidate.Id});
        }catch(Exception e){Storage.Log("切换失败",e.ToString());try{await Restore();}catch(Exception recovery){Storage.Log("恢复失败",recovery.ToString());}State=PanelState.故障;Message=e.Message;}
        finally{busy=false;Changed?.Invoke();}
    }
    void RefreshForToggle(){if(backend is BaselineBackend||backend is DdcBackend)backend=null;bool previous=busy;busy=false;try{Refresh();}finally{busy=previous;}}
    public async Task Restore() {
        if(lease==null)return;
        State=PanelState.正在恢复;Changed?.Invoke();var current=lease;
        Recovery.Complete(current,Storage.LeasePath);
        Native.Awake(false);lease=null;State=PanelState.开启;Message="内屏已恢复。";
        foreach(var p in new[]{current.Heartbeat,current.Ready})try{File.Delete(p);}catch{}
        Changed?.Invoke();await Task.CompletedTask;
    }
    public async Task Tick() {
        if(lease==null||busy)return;
        if(State==PanelState.故障){await Restore();return;}
        if(!File.Exists(Storage.LeasePath)){await Restore();return;}
        File.WriteAllText(lease.Heartbeat,"存活");
        if(lease.Deadline.HasValue&&DateTimeOffset.Now>=lease.Deadline){await Restore();return;}
        if(lease.Method=="布局"){
            var active=DisplayNative.Query().Paths;
            if(!active.Any(p=>!DisplayNative.IsInternal(p.Target.Technology))){await Restore();}
        }
    }
    public async Task EnvironmentChanged() {
        if(busy)return;
        if(lease!=null){if(lease.Method=="布局"&&!backend!.IsOff(lease))await Restore();else await Tick();}
        else Refresh();
    }
}
