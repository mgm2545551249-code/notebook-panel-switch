using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Interop;

namespace PanelSwitch;
internal static class Native {
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] internal static extern IntPtr FindWindow(string? cls,string title);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern uint RegisterWindowMessage(string name);
    internal static readonly uint ExitMessage=RegisterWindowMessage("PanelSwitch.ExitAndRestore.1");
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint pid);
    [DllImport("user32.dll",SetLastError=true)] internal static extern bool RegisterHotKey(IntPtr hwnd,int id,uint modifiers,uint key);
    [DllImport("user32.dll",SetLastError=true)] internal static extern bool UnregisterHotKey(IntPtr hwnd,int id);
    [DllImport("user32.dll",SetLastError=true)] static extern IntPtr SendMessageTimeout(IntPtr hwnd,uint msg,IntPtr w,IntPtr l,uint flags,uint timeout,out IntPtr result);
    [DllImport("kernel32.dll",SetLastError=true)] internal static extern uint SetThreadExecutionState(uint flags);
    [DllImport("user32.dll",SetLastError=true)] internal static extern IntPtr RegisterPowerSettingNotification(IntPtr recipient,ref Guid guid,uint flags);
    [DllImport("user32.dll")] internal static extern bool UnregisterPowerSettingNotification(IntPtr handle);
    [DllImport("user32.dll")] internal static extern bool PostMessage(IntPtr hwnd,uint msg,IntPtr w,IntPtr l);
    internal static void MonitorPower(int value) {
        if(SendMessageTimeout(new IntPtr(0xffff),0x112,new IntPtr(0xf170),new IntPtr(value),2,2000,out _)==IntPtr.Zero)
            Storage.Log("常规显示电源命令返回",new{value,错误=Marshal.GetLastWin32Error()});
    }
    internal static void Awake(bool enabled) {
        if(SetThreadExecutionState(enabled?0x80000001u:0x80000000u)==0)throw new Win32Exception(Marshal.GetLastWin32Error());
    }
}

public sealed class WindowPlacement {
    public long Handle {get;set;}
    public int ProcessId {get;set;}
    public long ProcessStarted {get;set;}
    public Placement Position {get;set;}
    [StructLayout(LayoutKind.Sequential)] public struct Placement {
        public uint Length, Flags, ShowCommand; public Point Min,Max; public Rect Normal;
    }
    delegate bool EnumWindowsProc(IntPtr hwnd,IntPtr l);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc callback,IntPtr l);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool GetWindowPlacement(IntPtr hwnd,ref Placement placement);
    [DllImport("user32.dll")] static extern bool SetWindowPlacement(IntPtr hwnd,ref Placement placement);
    [DllImport("user32.dll")] static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint pid);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd,StringBuilder text,int max);
    internal static bool IsShellSurface(string name)=>name=="Progman"||name=="WorkerW"||name=="Shell_TrayWnd"||name=="Shell_SecondaryTrayWnd";
    internal static bool IsShellSurface(IntPtr hwnd){var name=new StringBuilder(256);GetClassName(hwnd,name,name.Capacity);return IsShellSurface(name.ToString());}
    public static WindowPlacement[] Capture() {
        var previous=SetThreadDpiAwarenessContext(new IntPtr(-4));
        try{
        var result=new List<WindowPlacement>();
        EnumWindows((h,_)=>{try{
            if(!IsWindowVisible(h)||IsShellSurface(h))return true;
            var p=new Placement{Length=(uint)Marshal.SizeOf<Placement>()};
            GetWindowThreadProcessId(h,out uint pid);using var process=Process.GetProcessById((int)pid);
            if(GetWindowPlacement(h,ref p))result.Add(new(){Handle=h.ToInt64(),ProcessId=(int)pid,ProcessStarted=process.StartTime.ToUniversalTime().Ticks,Position=p});
        }catch{}return true;},IntPtr.Zero);return result.ToArray();
        }finally{if(previous!=IntPtr.Zero)SetThreadDpiAwarenessContext(previous);}
    }
    public static void RestoreAll(WindowPlacement[] windows) {
        var previous=SetThreadDpiAwarenessContext(new IntPtr(-4));
        try{
        foreach(var w in windows)try{
            var h=new IntPtr(w.Handle);if(IsShellSurface(h))continue;GetWindowThreadProcessId(h,out uint pid);if(pid!=w.ProcessId)continue;
            using var process=Process.GetProcessById((int)pid);if(process.StartTime.ToUniversalTime().Ticks!=w.ProcessStarted)continue;
            var p=w.Position;p.Length=(uint)Marshal.SizeOf<Placement>();
            var current=new Placement{Length=(uint)Marshal.SizeOf<Placement>()};
            if(GetWindowPlacement(h,ref current)&&current.ShowCommand==p.ShowCommand&&
                current.Normal.Left==p.Normal.Left&&current.Normal.Top==p.Normal.Top&&
                current.Normal.Right==p.Normal.Right&&current.Normal.Bottom==p.Normal.Bottom)continue;
            // 避免恢复普通窗口时抢夺用户当前焦点。
            if(p.ShowCommand==1)p.ShowCommand=4;
            if(!SetWindowPlacement(h,ref p))Storage.Log("窗口位置恢复失败",new{w.Handle,错误=Marshal.GetLastWin32Error()});
        }catch{}
        }finally{if(previous!=IntPtr.Zero)SetThreadDpiAwarenessContext(previous);}
    }
}

internal sealed class Hotkey:IDisposable {
    readonly IntPtr hwnd;uint modifiers,key;bool registered;
    internal Hotkey(IntPtr hwnd){this.hwnd=hwnd;}
    internal void Set(uint newModifiers,uint newKey) {
        if(newModifiers==3&&newKey==0x7A)throw new ArgumentException("此组合键保留给紧急恢复内屏。");
        if(newKey<0x08||newKey>0xfe||newModifiers==0||newModifiers>15)throw new ArgumentException("请选择带修饰键的有效快捷键。");
        if(registered&&newModifiers==modifiers&&newKey==key)return;
        if(!Native.RegisterHotKey(hwnd,19,newModifiers|0x4000,newKey))throw new Win32Exception(Marshal.GetLastWin32Error(),"快捷键已被占用，请换一个组合。");
        Native.UnregisterHotKey(hwnd,19);
        bool had=registered;uint oldM=modifiers,oldK=key;
        if(registered)Native.UnregisterHotKey(hwnd,18);
        registered=Native.RegisterHotKey(hwnd,18,newModifiers|0x4000,newKey);
        if(!registered){if(had)registered=Native.RegisterHotKey(hwnd,18,oldM|0x4000,oldK);throw new Win32Exception(Marshal.GetLastWin32Error(),"注册快捷键失败，已尝试保留原设置。");}
        modifiers=newModifiers;key=newKey;
    }
    public void Dispose(){if(registered)Native.UnregisterHotKey(hwnd,18);}
}
