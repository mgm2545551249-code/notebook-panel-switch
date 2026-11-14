using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;

namespace PanelSwitch;
// 仅处理真正的原始输入事件；显示器电源通知不被解释成实体电源键。
internal sealed class PowerButton:IDisposable {
    [StructLayout(LayoutKind.Sequential)] struct RawDevice {public IntPtr Device;public uint Type;}
    [StructLayout(LayoutKind.Sequential)] struct Registration {public ushort Page,Usage;public uint Flags;public IntPtr Window;}
    [StructLayout(LayoutKind.Sequential)] struct Header {public uint Type,Size;public IntPtr Device,WParam;}
    [DllImport("user32.dll",SetLastError=true)] static extern uint GetRawInputDeviceList([Out] RawDevice[]? list,ref uint count,uint size);
    [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern uint GetRawInputDeviceInfo(IntPtr device,uint command,IntPtr data,ref uint size);
    [DllImport("user32.dll",SetLastError=true)] static extern bool RegisterRawInputDevices(Registration[] list,uint count,uint size);
    [DllImport("user32.dll",SetLastError=true)] static extern uint GetRawInputData(IntPtr input,uint command,IntPtr data,ref uint size,uint headerSize);
    [DllImport("hid.dll")] static extern int HidP_GetUsages(int type,ushort page,ushort collection,[Out] ushort[] usages,ref uint count,IntPtr preparsed,byte[] report,uint length);
    public event Action? Pressed;
    readonly HashSet<IntPtr> held=new();
    bool registered;
    public int ObservedPresses{get;private set;}
    public static string[] Inventory() {
        uint count=0;GetRawInputDeviceList(null,ref count,(uint)Marshal.SizeOf<RawDevice>());
        var list=new RawDevice[count];if(GetRawInputDeviceList(list,ref count,(uint)Marshal.SizeOf<RawDevice>())==uint.MaxValue)return Array.Empty<string>();
        var result=new List<string>();
        foreach(var d in list.Take((int)count)) {
            uint n=32;var p=Marshal.AllocHGlobal(32);
            try{Marshal.WriteInt32(p,32);if(GetRawInputDeviceInfo(d.Device,0x2000000b,p,ref n)!=uint.MaxValue&&d.Type==2){uint page=(uint)(ushort)Marshal.ReadInt16(p,20),usage=(uint)(ushort)Marshal.ReadInt16(p,22);result.Add($"输入设备类型={d.Type}，用途页={page:X4}，用途={usage:X4}");}}
            finally{Marshal.FreeHGlobal(p);}
        }return result.ToArray();
    }
    public PowerButton(IntPtr hwnd) {
        var devices=new[]{new Registration{Page=1,Usage=0x80,Flags=0x100,Window=hwnd}};
        registered=RegisterRawInputDevices(devices,1,(uint)Marshal.SizeOf<Registration>());
        if(!registered)Storage.Log("系统控制原始输入注册失败",Marshal.GetLastWin32Error());
    }
    public void Input(IntPtr input) {
        uint size=0,hs=(uint)Marshal.SizeOf<Header>();GetRawInputData(input,0x10000003,IntPtr.Zero,ref size,hs);
        if(size<hs||size>65536)return;
        IntPtr data=Marshal.AllocHGlobal((int)size);
        try {
            if(GetRawInputData(input,0x10000003,data,ref size,hs)==uint.MaxValue)return;
            var header=Marshal.PtrToStructure<Header>(data);if(header.Type!=2||size<hs+8)return;
            uint length=(uint)Marshal.ReadInt32(data,(int)hs),count=(uint)Marshal.ReadInt32(data,(int)hs+4);
            if(length==0||count>64||(ulong)length*count+hs+8>size)return;
            uint prepSize=0;GetRawInputDeviceInfo(header.Device,0x20000005,IntPtr.Zero,ref prepSize);if(prepSize==0||prepSize>65536)return;
            IntPtr preparsed=Marshal.AllocHGlobal((int)prepSize);
            try {
                if(GetRawInputDeviceInfo(header.Device,0x20000005,preparsed,ref prepSize)==uint.MaxValue)return;
                for(int i=0;i<count;i++){
                    var report=new byte[length];Marshal.Copy(IntPtr.Add(data,(int)(hs+8+i*length)),report,0,(int)length);
                    var usages=new ushort[64];uint usageCount=64;int rc=HidP_GetUsages(0,1,0,usages,ref usageCount,preparsed,report,length);
                    if(rc!=0x110000)continue;
                    bool down=usages.Take((int)usageCount).Contains((ushort)0x81);
                    if(down&&held.Add(header.Device)){ObservedPresses++;Storage.Log("观察到真实系统电源键输入",new{序号=ObservedPresses});Pressed?.Invoke();}
                    if(!down)held.Remove(header.Device);
                }
            }finally{Marshal.FreeHGlobal(preparsed);}
        }catch(Exception e){Storage.Log("解析系统控制输入失败",e.Message);}finally{Marshal.FreeHGlobal(data);}
    }
    public void Dispose(){if(registered)RegisterRawInputDevices(new[]{new Registration{Page=1,Usage=0x80,Flags=1}},1,(uint)Marshal.SizeOf<Registration>());}
}
