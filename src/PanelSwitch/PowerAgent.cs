using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Diagnostics.Tracing.Session;

namespace PanelSwitch;
internal sealed class PowerAgentClient:IAsyncDisposable {
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool GetNamedPipeClientProcessId(Microsoft.Win32.SafeHandles.SafePipeHandle pipe,out uint pid);
    readonly NamedPipeServerStream pipe;
    readonly Process process;
    StreamReader? reader;StreamWriter? writer;
    public event Action? Pressed;
    public bool Ready{get;private set;}
    PowerAgentClient(NamedPipeServerStream pipe,Process process){this.pipe=pipe;this.process=process;}
    public static async Task<PowerAgentClient> Start() {
        string name="PanelSwitch.Power."+Guid.NewGuid().ToString("N");
        var pipe=new NamedPipeServerStream(name,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
        using var parent=Process.GetCurrentProcess();
        var args=$"--power-agent {name} {parent.Id} {parent.StartTime.ToUniversalTime().Ticks}";
        Process? worker=null;
        try {
            worker=Process.Start(new ProcessStartInfo(Environment.ProcessPath!,args){UseShellExecute=true,Verb="runas",WindowStyle=ProcessWindowStyle.Hidden})??throw new Exception("电源键代理未启动。");
            await pipe.WaitForConnectionAsync().WaitAsync(TimeSpan.FromSeconds(15));
            if(!GetNamedPipeClientProcessId(pipe.SafePipeHandle,out uint connected)||connected!=worker.Id)throw new Exception("电源键代理身份校验失败。");
            var client=new PowerAgentClient(pipe,worker);
            client.reader=new StreamReader(pipe);client.writer=new StreamWriter(pipe){AutoFlush=true};
            string? ready=await client.reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10));
            if(ready!="已就绪")throw new Exception(ready??"电源键代理未就绪。");
            client.Ready=true;_=client.ReadEvents();return client;
        }catch{pipe.Dispose();worker?.Dispose();throw;}
    }
    async Task ReadEvents(){try{while(await reader!.ReadLineAsync() is string line){if(line=="电源键")Pressed?.Invoke();else Storage.Log("电源键代理消息",line);}}catch(Exception e){Storage.Log("电源键连接结束",e.Message);}finally{Ready=false;}}
    public async Task Pulse(){try{if(Ready&&writer!=null)await writer.WriteLineAsync("心跳");}catch{Ready=false;}}
    public async ValueTask DisposeAsync(){try{if(writer!=null)await writer.WriteLineAsync("退出");}catch{}pipe.Dispose();try{await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));}catch{}process.Dispose();Ready=false;}
}
internal static class PowerAgent {
    static readonly Guid Provider=new("c514638f-7723-485b-bcfc-96565d735d4a");
    internal static string SnapshotPath=>Path.Combine(Storage.Root,"电源键策略备份.json");
    internal static void RestoreStale(){var snapshot=Storage.Load<PowerPolicySnapshot>(SnapshotPath);if(snapshot==null)return;try{using var p=Process.GetProcessById(snapshot.OwnerPid);if(!p.HasExited&&p.StartTime.ToUniversalTime().Ticks==snapshot.OwnerStarted)return;}catch{}PowerPolicy.Restore(snapshot);File.Delete(SnapshotPath);}
    internal static bool IsPowerEvent(int id,int version,string instance,uint eventMask)=>(id==24||id==25)&&version==0&&(eventMask&1)!=0&&!string.IsNullOrWhiteSpace(instance)&&
        (instance.StartsWith("\\",StringComparison.Ordinal)||instance.Contains("PNP0C0C",StringComparison.OrdinalIgnoreCase)||instance.Contains("FIXEDBUTTON",StringComparison.OrdinalIgnoreCase));
    internal static (string Instance,uint Mask) DecodeButton(int id,byte[] payload){if(payload.Length<2)throw new FormatException("按钮事件长度不足");int length=BitConverter.ToUInt16(payload,0);int bytes=checked(length*2);int maskOffset=checked(2+bytes+(id==24?4:0));if(length>2048||maskOffset+4>payload.Length)throw new FormatException("按钮事件字段越界");return(Encoding.Unicode.GetString(payload,2,bytes).TrimEnd('\0'),BitConverter.ToUInt32(payload,maskOffset));}
    public static int Run(string pipeName,int parentPid,long parentStarted) {
        if(!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))return 5;
        using var mutex=new Mutex(true,"Local\\PanelSwitch.PowerAgent",out bool first);if(!first)return 183;
        PowerPolicySnapshot? snapshot=null;
        using var pipe=new NamedPipeClientStream(".",pipeName,PipeDirection.InOut,PipeOptions.Asynchronous);
        using var stop=new CancellationTokenSource();TraceEventSession? session=null;
        try {
            pipe.Connect(10000);using var reader=new StreamReader(pipe);using var writer=new StreamWriter(pipe){AutoFlush=true};object outputLock=new();
            RestoreStale();snapshot=PowerPolicy.Read();using(var current=Process.GetCurrentProcess()){snapshot.OwnerPid=current.Id;snapshot.OwnerStarted=current.StartTime.ToUniversalTime().Ticks;}Storage.Save(SnapshotPath,snapshot);
            session=new TraceEventSession("PanelSwitch.ACPI."+Environment.ProcessId);session.StopOnDispose=true;
            int samples=0;DateTime lastPress=DateTime.MinValue;
            session.Source.AllEvents+=data=>{
                if(data.ProviderGuid!=Provider)return;
                if(samples++<4)Storage.Log("已收到ACPI原始事件",new{事件=(int)data.ID,版本=data.Version,长度=data.EventDataLength});
                if(((int)data.ID!=24&&(int)data.ID!=25)||data.Version!=0)return;
                try {
                    var decoded=DecodeButton((int)data.ID,data.EventData());string instance=decoded.Instance;uint mask=decoded.Mask;
                    Storage.Log("收到ACPI按钮记录",new{事件=(int)data.ID,设备=instance,掩码=mask});
                    if(!IsPowerEvent((int)data.ID,data.Version,instance,mask))return;
                    if(DateTime.UtcNow-lastPress<TimeSpan.FromMilliseconds(500))return;lastPress=DateTime.UtcNow;
                    Storage.Log("真实ACPI电源键事件",new{事件=(int)data.ID,设备=instance,掩码=mask});
                    lock(outputLock)writer.WriteLine("电源键");
                }catch(Exception e){Storage.Log("ACPI事件解析失败",e.Message);}
            };
            session.EnableProvider(Provider,Microsoft.Diagnostics.Tracing.TraceEventLevel.Verbose,ulong.MaxValue);
            var processing=Task.Run(()=>session.Source.Process());
            PowerPolicy.Apply(snapshot);
            lock(outputLock)writer.WriteLine("已就绪");
            var commands=reader.ReadLineAsync();
            var heartbeat=Stopwatch.StartNew();
            while(!stop.IsCancellationRequested) {
                if(processing.IsFaulted)throw processing.Exception!;
                if(commands.IsCompleted){string? command=commands.GetAwaiter().GetResult();if(command==null||command=="退出")break;heartbeat.Restart();commands=reader.ReadLineAsync();}
                if(heartbeat.Elapsed>TimeSpan.FromSeconds(10))break;
                try{using var parent=Process.GetProcessById(parentPid);if(parent.HasExited||parent.StartTime.ToUniversalTime().Ticks!=parentStarted)break;}catch{break;}
                Thread.Sleep(300);
            }
            return 0;
        }catch(Exception e){Storage.Log("电源键代理错误",e.ToString());return 1;}
        finally{session?.Dispose();if(snapshot!=null)try{PowerPolicy.Restore(snapshot);File.Delete(SnapshotPath);}catch(Exception e){Storage.Log("电源键策略还原失败",e.ToString());}}
    }
}
