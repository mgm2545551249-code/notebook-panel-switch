using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
namespace PanelSwitch;
internal static class SelfTests {
    internal static int Run(string output) {
        var tests=new List<object>();int failures=0;
        void Test(string name,Action action){try{action();tests.Add(new{项目=name,结果="通过"});}catch(Exception e){failures++;tests.Add(new{项目=name,结果="失败",原因=e.Message});}}
        void Assert(bool ok,string reason){if(!ok)throw new Exception(reason);}
        Test("显示结构与原生接口尺寸一致",()=>{Assert(Marshal.SizeOf<DisplayPath>()==72,"路径结构长度错误");Assert(Marshal.SizeOf<DisplayMode>()==64,"模式结构长度错误");});
        var snapshot=DisplayNative.Query();
        Test("当前显示布局只验证而不应用",()=>DisplayNative.Validate(snapshot));
        Test("恢复记录序列化保留全部模式字节",()=>{var copy=JsonSerializer.Deserialize<DisplaySnapshot>(JsonSerializer.Serialize(snapshot,Storage.Json),Storage.Json)!;Assert(Bytes(snapshot.Modes).SequenceEqual(Bytes(copy.Modes)),"模式字节变化");Assert(Bytes(snapshot.Paths).SequenceEqual(Bytes(copy.Paths)),"路径字节变化");});
        Test("唯一内屏禁止停用",()=>{var single=new DisplaySnapshot{Paths=new[]{new DisplayPath{Flags=1,Target=new(){Technology=11}}}};try{DisplayNative.ExternalOnly(single);}catch(InvalidOperationException){return;}throw new Exception("错误地允许停用唯一内屏");});
        Test("外接屏布局转换保留原始快照",()=>{
            var s=new DisplaySnapshot{Paths=new[]{new DisplayPath{Flags=1,Source=new(){ModeIndex=0},Target=new(){Technology=11}},new DisplayPath{Flags=1,Source=new(){ModeIndex=1},Target=new(){Technology=5}}},Modes=new[]{new DisplayMode{Type=1,Source=new(){Position=new(){X=0,Y=0},Width=2560,Height=1600}},new DisplayMode{Type=1,Source=new(){Position=new(){X=2560,Y=0},Width=1920,Height=1080}}}};
            var external=DisplayNative.ExternalOnly(s);Assert(external.Paths.Length==1,"外接路径数错误");Assert(external.Modes[1].Source.Position.X==0,"主屏原点错误");Assert(s.Modes[1].Source.Position.X==2560,"原始布局被修改");
        });
        Test("电源配置冲突保留用户新值",()=>{Assert(PowerPolicy.ShouldRestore(0,0),"未识别接管值");Assert(!PowerPolicy.ShouldRestore(3,0),"将覆盖用户修改");});
        Test("读取电源配置不修改系统",()=>{var p=PowerPolicy.Read();Assert(p.Ac<=4&&p.Dc<=4,"策略值异常");});
        Test("五十次布局恢复数据往返",()=>{for(int i=0;i<50;i++){var copy=JsonSerializer.Deserialize<DisplaySnapshot>(JsonSerializer.Serialize(snapshot,Storage.Json),Storage.Json)!;Assert(Bytes(snapshot.Paths).SequenceEqual(Bytes(copy.Paths)),"第"+i+"轮恢复数据变化");}});
        Test("原始输入设备枚举",()=>PowerButton.Inventory());
        Test("实体电源键事件与盖子睡眠事件分离",()=>{Assert(PowerAgent.IsPowerEvent(24,0,@"ACPI\PNP0C0C\实例",1),"遗漏电源键");Assert(PowerAgent.IsPowerEvent(25,0,@"ACPI\FIXEDBUTTON\实例",1),"遗漏固定功能按钮完成事件");Assert(!PowerAgent.IsPowerEvent(24,0,@"ACPI\PNP0C0D\实例",4),"盖子事件被误认");Assert(!PowerAgent.IsPowerEvent(24,1,@"ACPI\PNP0C0C\实例",1),"未知版本被误认");});
        Test("ACPI事件解析拒绝截断数据",()=>{try{PowerAgent.DecodeButton(25,new byte[]{128,0,0,0});}catch(FormatException){return;}throw new Exception("未拒绝截断事件");});
        Test("本机真实固件按钮路径回归",()=>{Assert(PowerAgent.IsPowerEvent(24,0,@"\_SB.PWRB",1),"本机固件按钮被过滤");Assert(PowerAgent.IsPowerEvent(25,0,@"\_SB.PWRB",1),"本机完成事件被过滤");Assert(!PowerAgent.IsPowerEvent(24,0,@"\_SB.LID_",4),"盖子事件错误触发");});
        Test("内屏具有稳定设备标识",()=>Assert(snapshot.Paths.Select(DisplayNative.Describe).Any(x=>x.Internal&&!string.IsNullOrEmpty(x.DevicePath)),"无内屏标识"));
        Storage.Save(output,new{时间=DateTimeOffset.Now,通过=tests.Count-failures,失败=failures,测试=tests,说明="五十次测试为布局数据往返，不是实机开关五十次。"});return failures==0?0:1;
    }
    static byte[] Bytes<T>(T[] values) where T:struct {int size=Marshal.SizeOf<T>();var bytes=new byte[size*values.Length];IntPtr p=Marshal.AllocHGlobal(size);try{for(int i=0;i<values.Length;i++){Marshal.StructureToPtr(values[i],p,false);Marshal.Copy(p,bytes,i*size,size);}}finally{Marshal.FreeHGlobal(p);}return bytes;}
}
