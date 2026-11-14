using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Threading;
namespace PanelSwitch;
internal static class IntegrationTests {
    public static async Task<int> TestPowerAgent(string output) {
        var before=PowerPolicy.Read();int count=0;string? error=null;bool ready=false;PowerPolicySnapshot? during=null;
        try{await using(var agent=await PowerAgentClient.Start()) {ready=agent.Ready;agent.Pressed+=()=>count++;during=PowerPolicy.Read();for(int i=0;i<3;i++){await agent.Pulse();await Task.Delay(500);}}}catch(Exception e){error=e.ToString();}
        var after=PowerPolicy.Read();bool restored=before.Scheme==after.Scheme&&before.Ac==after.Ac&&before.Dc==after.Dc;
        Storage.Save(output,new{时间=DateTimeOffset.Now,代理已就绪=ready,原始策略=before,接管期间=during,退出策略=after,还原一致=restored,观察到实体按压次数=count,错误=error});return ready&&restored&&during?.Ac==0&&during?.Dc==0?0:1;
    }
    [DllImport("user32.dll")] static extern void mouse_event(uint flags,int dx,int dy,uint data,UIntPtr extra);
    static bool Same(DisplaySnapshot a,DisplaySnapshot b)=>a.Paths.Select(DisplayNative.Key).OrderBy(x=>x).SequenceEqual(b.Paths.Select(DisplayNative.Key).OrderBy(x=>x));
    public static int Run(string output,int cycles,bool baseline=false) {
        int result=1;var app=new System.Windows.Application{ShutdownMode=System.Windows.ShutdownMode.OnExplicitShutdown};
        app.Startup+=async(_,_)=>{
            var initial=DisplayNative.Query();var logs=new List<object>();var settings=new Settings{AllowVirtualDisplay=true};var controller=new Controller(settings);
            try{
                for(int i=0;i<cycles;i++){
                    var watch=Stopwatch.StartNew();controller.Refresh();await controller.Toggle(15,baseline?new BaselineBackend():null);
                    if(controller.State!=PanelState.关闭)throw new Exception("关屏失败："+controller.Message);
                    var off=DisplayNative.Query();
                    if(!baseline&&off.Paths.Any(p=>DisplayNative.IsInternal(p.Target.Technology)))throw new Exception("内屏路径仍然启用");
                    // 仅发送相对鼠标移动，不输入字符、不点击任何控件。
                    mouse_event(1,2,0,0,UIntPtr.Zero);await Task.Delay(i==0?4000:100);mouse_event(1,-2,0,0,UIntPtr.Zero);
                    var afterInput=DisplayNative.Query();bool remained=baseline?true:!afterInput.Paths.Any(p=>DisplayNative.IsInternal(p.Target.Technology));
                    if(!remained)throw new Exception("普通鼠标输入后内屏重新启用");
                    await controller.Restore();var restored=DisplayNative.Query();if(!Same(initial,restored))throw new Exception("原始显示路径未恢复");
                    logs.Add(new{轮次=i+1,关闭后路径=off.Paths.Select(DisplayNative.Describe).ToArray(),鼠标输入后内屏路径仍停用=!afterInput.Paths.Any(p=>DisplayNative.IsInternal(p.Target.Technology)),恢复后路径一致=true,耗时毫秒=watch.ElapsedMilliseconds});
                    await Task.Delay(120);
                }
                result=0;
            }catch(Exception e){logs.Add(new{错误=e.ToString()});try{await controller.Restore();}catch{} }
            finally{Storage.Save(output,new{时间=DateTimeOffset.Now,类型=baseline?"常规关屏对照":"真实显示布局开关",请求轮次=cycles,完成轮次=logs.Count(x=>!x.ToString()!.Contains("错误")),退出码=result,日志=logs,最终路径=DisplayNative.Query().Paths.Select(DisplayNative.Describe),说明="路径停用与鼠标输入状态已由软件检查；面板是否停止发光仍需现场观察确认。"});app.Shutdown(result);}
        };app.Run();return result;
    }
    public static int Crash(string marker) {
        var app=new System.Windows.Application{ShutdownMode=System.Windows.ShutdownMode.OnExplicitShutdown};
        app.Startup+=async(_,_)=>{var c=new Controller(new Settings{AllowVirtualDisplay=true});c.Refresh();await c.Toggle(15);Storage.Save(marker,new{状态=c.State.ToString(),时间=DateTimeOffset.Now});Environment.Exit(c.State==PanelState.关闭?23:24);};app.Run();return 24;
    }
    public static int Timeout(string output){
        int code=1;var app=new System.Windows.Application{ShutdownMode=System.Windows.ShutdownMode.OnExplicitShutdown};
        app.Startup+=async(_,_)=>{var before=DisplayNative.Query();var c=new Controller(new Settings{AllowVirtualDisplay=true});try{c.Refresh();await c.Toggle(3);bool off=c.State==PanelState.关闭;var watch=Stopwatch.StartNew();while(File.Exists(Storage.LeasePath)&&watch.Elapsed<TimeSpan.FromSeconds(25))await Task.Delay(300);var after=DisplayNative.Query();bool restored=Same(before,after)&&!File.Exists(Storage.LeasePath);await c.Tick();code=off&&restored?0:1;Storage.Save(output,new{时间=DateTimeOffset.Now,确实关闭=off,独立进程自动恢复=restored,退出码=code});}finally{await c.Restore();app.Shutdown(code);}};app.Run();return code;
    }
}
