using System;
using System.Runtime.InteropServices;
namespace PanelSwitch;

// 此设备信息类型未由微软公开保证，仅用于可回退的本机虚拟屏校准。
internal static class DisplayDpi {
    [StructLayout(LayoutKind.Sequential)] struct QueryPacket {public DeviceHeader Header;public int Min,Current,Max;}
    [StructLayout(LayoutKind.Sequential)] struct SetPacket {public DeviceHeader Header;public int Relative;}
    [DllImport("user32.dll",EntryPoint="DisplayConfigGetDeviceInfo")] static extern int Get(ref QueryPacket packet);
    [DllImport("user32.dll",EntryPoint="DisplayConfigSetDeviceInfo")] static extern int Set(ref SetPacket packet);
    static readonly int[] Scales={100,125,150,175,200,225,250,300,350,400,450,500};
    static QueryPacket Read(DisplayPath path){
        var p=new QueryPacket{Header=new(){Type=unchecked((uint)-3),Size=(uint)Marshal.SizeOf<QueryPacket>(),Adapter=path.Source.Adapter,Id=path.Source.Id}};
        int rc=Get(ref p);if(rc!=0)throw new InvalidOperationException($"读取显示缩放失败：{rc}");return p;
    }
    internal static int Percent(DisplayPath path){var p=Read(path);int i=p.Current-p.Min;if(i<0||i>=Scales.Length)throw new InvalidOperationException("当前缩放不在已验证的映射范围。");return Scales[i];}
    internal static void ApplyVirtual(DisplayPath path,int percent){
        if(!Controller.IsVirtual(DisplayNative.Describe(path)))throw new InvalidOperationException("缩放校准仅允许虚拟屏。");
        var q=Read(path);int index=Array.IndexOf(Scales,percent);int rel=index+q.Min;
        if(index<0||rel<q.Min||rel>q.Max)throw new InvalidOperationException("虚拟屏不支持请求的缩放比例。");
        var p=new SetPacket{Header=new(){Type=unchecked((uint)-4),Size=(uint)Marshal.SizeOf<SetPacket>(),Adapter=path.Source.Adapter,Id=path.Source.Id},Relative=rel};
        int rc=Set(ref p);if(rc!=0)throw new InvalidOperationException($"设置虚拟屏缩放失败：{rc}");
        if(Percent(path)!=percent)throw new InvalidOperationException("虚拟屏缩放回读不一致。");
    }
}
