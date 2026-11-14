using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace PanelSwitch;

[StructLayout(LayoutKind.Sequential)] public struct Luid { public uint Low; public int High; public override string ToString()=>$"{High:X8}:{Low:X8}"; }
[StructLayout(LayoutKind.Sequential)] public struct Ratio { public uint Numerator, Denominator; }
[StructLayout(LayoutKind.Sequential)] public struct PathSource { public Luid Adapter; public uint Id, ModeIndex, Status; }
[StructLayout(LayoutKind.Sequential)] public struct PathTarget {
    public Luid Adapter; public uint Id, ModeIndex; public uint Technology, Rotation, Scaling; public Ratio Refresh;
    public uint Scanline; public int Available; public uint Status;
}
[StructLayout(LayoutKind.Sequential)] public struct DisplayPath { public PathSource Source; public PathTarget Target; public uint Flags; }
[StructLayout(LayoutKind.Sequential)] public struct Point { public int X,Y; }
[StructLayout(LayoutKind.Sequential)] public struct SourceMode { public uint Width, Height, PixelFormat; public Point Position; }
[StructLayout(LayoutKind.Explicit, Size=64)] public struct DisplayMode {
    [FieldOffset(0)] public uint Type;
    [FieldOffset(4)] public uint Id;
    [FieldOffset(8)] public Luid Adapter;
    [FieldOffset(16)] public SourceMode Source;
    [FieldOffset(16)] public ulong Data0;
    [FieldOffset(24)] public ulong Data1;
    [FieldOffset(32)] public ulong Data2;
    [FieldOffset(40)] public ulong Data3;
    [FieldOffset(48)] public ulong Data4;
    [FieldOffset(56)] public ulong Data5;
}
[StructLayout(LayoutKind.Sequential)] internal struct DeviceHeader { public uint Type,Size; public Luid Adapter; public uint Id; }
[StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] internal struct TargetName {
    public DeviceHeader Header; public uint Flags, Technology; public ushort Manufacturer, Product; public uint Connector;
    [MarshalAs(UnmanagedType.ByValTStr,SizeConst=64)] public string FriendlyName;
    [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)] public string DevicePath;
}
[StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] internal struct SourceName {
    public DeviceHeader Header;
    [MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)] public string Name;
}
[StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] internal struct MonitorInfo {
    public uint Size; public Rect Monitor,Work; public uint Flags;
    [MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)] public string Device;
}
[StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left,Top,Right,Bottom; }
[StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] internal struct PhysicalMonitor {
    public IntPtr Handle;
    [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)] public string Description;
}
public sealed record DisplayEndpoint(string Key,string Name,string DevicePath,string GdiName,uint Technology,bool Internal,bool Available,bool Active);
public sealed class DisplaySnapshot {
    public DisplayPath[] Paths {get;set;}=Array.Empty<DisplayPath>();
    public DisplayMode[] Modes {get;set;}=Array.Empty<DisplayMode>();
}
public sealed record DdcCapability(string Device,string Description,bool PowerReadable,uint CurrentPower,int Error,string Capabilities);

internal static class DisplayNative {
    internal delegate bool MonitorCallback(IntPtr monitor,IntPtr dc,ref Rect rect,IntPtr data);
    [DllImport("user32.dll")] static extern int GetDisplayConfigBufferSizes(uint flags,out uint paths,out uint modes);
    [DllImport("user32.dll")] static extern int QueryDisplayConfig(uint flags,ref uint paths,[Out] DisplayPath[] path,ref uint modes,[Out] DisplayMode[] mode,IntPtr topology);
    [DllImport("user32.dll")] static extern int SetDisplayConfig(uint count,DisplayPath[]? paths,uint modes,DisplayMode[]? mode,uint flags);
    [DllImport("user32.dll",EntryPoint="DisplayConfigGetDeviceInfo")] static extern int GetTargetName(ref TargetName name);
    [DllImport("user32.dll",EntryPoint="DisplayConfigGetDeviceInfo")] static extern int GetSourceName(ref SourceName name);
    [DllImport("user32.dll")] static extern bool EnumDisplayMonitors(IntPtr dc,IntPtr clip,MonitorCallback callback,IntPtr data);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern bool GetMonitorInfo(IntPtr monitor,ref MonitorInfo info);
    [DllImport("dxva2.dll",SetLastError=true)] static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr monitor,out uint count);
    [DllImport("dxva2.dll",SetLastError=true)] static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr monitor,uint count,[Out] PhysicalMonitor[] physical);
    [DllImport("dxva2.dll")] static extern bool DestroyPhysicalMonitors(uint count,PhysicalMonitor[] physical);
    [DllImport("dxva2.dll",SetLastError=true)] static extern bool GetVCPFeatureAndVCPFeatureReply(IntPtr handle,byte code,out uint type,out uint current,out uint max);
    [DllImport("dxva2.dll",SetLastError=true)] static extern bool SetVCPFeature(IntPtr handle,byte code,uint value);
    [DllImport("dxva2.dll",SetLastError=true)] static extern bool GetCapabilitiesStringLength(IntPtr handle,out uint length);
    [DllImport("dxva2.dll",SetLastError=true,CharSet=CharSet.Ansi)] static extern bool CapabilitiesRequestAndCapabilitiesReply(IntPtr handle,StringBuilder text,uint length);

    public static DisplaySnapshot Query(bool all=false) {
        uint flags=all?1u:2u;
        for(int attempt=0;attempt<5;attempt++) {
            int rc=GetDisplayConfigBufferSizes(flags,out uint pc,out uint mc); Check(rc,"读取显示配置长度");
            var paths=new DisplayPath[pc]; var modes=new DisplayMode[mc];
            rc=QueryDisplayConfig(flags,ref pc,paths,ref mc,modes,IntPtr.Zero);
            if(rc==122)continue;
            Check(rc,"读取显示配置");
            return new(){Paths=paths.Take((int)pc).ToArray(),Modes=modes.Take((int)mc).ToArray()};
        }
        throw new InvalidOperationException("显示设备正在变化，请稍后重试。");
    }
    public static string Key(DisplayPath path)=>$"{path.Target.Adapter}/{path.Target.Id}";
    public static bool IsInternal(uint technology)=>technology==11||technology==6||technology==0x80000000;
    public static DisplayEndpoint Describe(DisplayPath p) {
        var t=new TargetName{Header=new(){Type=2,Size=(uint)Marshal.SizeOf<TargetName>(),Adapter=p.Target.Adapter,Id=p.Target.Id}};
        int rc=GetTargetName(ref t);
        var s=new SourceName{Header=new(){Type=1,Size=(uint)Marshal.SizeOf<SourceName>(),Adapter=p.Source.Adapter,Id=p.Source.Id}};
        GetSourceName(ref s);
        return new(Key(p),rc==0?t.FriendlyName:"未知显示器",rc==0?t.DevicePath:"",s.Name??"",p.Target.Technology,IsInternal(p.Target.Technology),p.Target.Available!=0,(p.Flags&1)!=0);
    }
    public static DisplaySnapshot ExternalOnly(DisplaySnapshot original) {
        var paths=original.Paths.Where(p=>(p.Flags&1)!=0&&!IsInternal(p.Target.Technology)).ToArray();
        if(paths.Length==0)throw new InvalidOperationException("当前没有可用外接屏，禁止停用唯一显示路径。");
        // 删除内屏后，将剩余桌面整体平移，保留外接屏之间的位置关系。
        var modes=(DisplayMode[])original.Modes.Clone();
        var indices=paths.Select(p=>p.Source.ModeIndex).Where(i=>i<modes.Length).Distinct().ToArray();
        int minX=indices.Length>0?indices.Min(i=>modes[i].Source.Position.X):0;
        int minY=indices.Length>0?indices.Min(i=>modes[i].Source.Position.Y):0;
        foreach(uint i in indices){var m=modes[i];m.Source.Position.X-=minX;m.Source.Position.Y-=minY;modes[i]=m;}
        return new(){Paths=paths,Modes=modes};
    }
    public static void Validate(DisplaySnapshot s)=>Check(SetDisplayConfig((uint)s.Paths.Length,s.Paths,(uint)s.Modes.Length,s.Modes,0x20|0x40|0x400),"验证显示布局");
    public static void Apply(DisplaySnapshot s) {
        if(s.Paths.Length==0)throw new InvalidOperationException("拒绝空显示布局。");
        Validate(s);
        Check(SetDisplayConfig((uint)s.Paths.Length,s.Paths,(uint)s.Modes.Length,s.Modes,0x20|0x80|0x400),"应用显示布局");
    }
    public static void EmergencyInternal()=>Check(SetDisplayConfig(0,null,0,null,0x80|1),"恢复内屏显示布局");
    public static void InternalOnly(){var current=Query();var paths=current.Paths.Where(p=>IsInternal(p.Target.Technology)).ToArray();if(paths.Length==0){EmergencyInternal();return;}Apply(new(){Paths=paths,Modes=current.Modes});}
    public static List<DdcCapability> ProbeDdc(string gdi) {
        var result=new List<DdcCapability>();
        Visit(gdi,(info,p)=>{
            bool ok=GetVCPFeatureAndVCPFeatureReply(p.Handle,0xd6,out _,out uint current,out _);
            int error=ok?0:Marshal.GetLastWin32Error(); string cap="";
            if(ok&&GetCapabilitiesStringLength(p.Handle,out uint len)&&len>0&&len<65536){var sb=new StringBuilder((int)len);if(CapabilitiesRequestAndCapabilitiesReply(p.Handle,sb,len))cap=sb.ToString();}
            result.Add(new(info.Device,p.Description,ok,current,error,cap));
        });
        return result;
    }
    public static void DdcPower(string gdi,uint value) {
        int found=0;
        Visit(gdi,(_,p)=>{found++;if(!SetVCPFeature(p.Handle,0xd6,value))throw new Win32Exception(Marshal.GetLastWin32Error(),"设置内屏电源失败");});
        if(found!=1)throw new InvalidOperationException("内屏物理监视器映射不唯一。");
    }
    static void Visit(string gdi,Action<MonitorInfo,PhysicalMonitor> visitor) {
        Exception? failure=null;
        EnumDisplayMonitors(IntPtr.Zero,IntPtr.Zero,(IntPtr h,IntPtr dc,ref Rect r,IntPtr d)=>{
            try {
                var info=new MonitorInfo{Size=(uint)Marshal.SizeOf<MonitorInfo>()};
                if(!GetMonitorInfo(h,ref info)||!string.Equals(info.Device,gdi,StringComparison.OrdinalIgnoreCase))return true;
                if(!GetNumberOfPhysicalMonitorsFromHMONITOR(h,out uint count)||count==0||count>16)return true;
                var monitors=new PhysicalMonitor[count];if(!GetPhysicalMonitorsFromHMONITOR(h,count,monitors))return true;
                try {foreach(var p in monitors)visitor(info,p);} finally{DestroyPhysicalMonitors(count,monitors);}
            }catch(Exception e){failure=e;return false;} return true;
        },IntPtr.Zero);
        if(failure!=null)throw failure;
    }
    static void Check(int rc,string operation){if(rc!=0)throw new Win32Exception(rc,operation);}
}
