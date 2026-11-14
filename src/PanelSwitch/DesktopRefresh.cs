using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.IO;
using System.Linq;
using System.Diagnostics;

namespace PanelSwitch;

internal static class DesktopRefresh {
    delegate bool EnumWindowProc(IntPtr hwnd,IntPtr parameter);
    [DllImport("user32.dll")] static extern IntPtr GetShellWindow();
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowProc callback,IntPtr parameter);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd,StringBuilder text,int max);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr parent,IntPtr after,string? cls,string? title);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint pid);
    [DllImport("user32.dll",SetLastError=true)] static extern bool RedrawWindow(IntPtr hwnd,IntPtr rectangle,IntPtr region,uint flags);
    [DllImport("user32.dll",SetLastError=true)] static extern IntPtr SendMessageTimeout(IntPtr hwnd,uint message,IntPtr w,IntPtr l,uint flags,uint timeout,out IntPtr result);
    [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr hwnd,out Rect rectangle);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd,out Rect rectangle);
    [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr hwnd,EnumWindowProc callback,IntPtr parameter);
    [DllImport("user32.dll",SetLastError=true)] static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int width,int height,uint flags);
    internal static object[] WindowInventory(){
        var result=new List<object>();EnumWindows((hwnd,_)=>{
            if(!IsWindowVisible(hwnd))return true;
            GetWindowThreadProcessId(hwnd,out uint pid);var cls=new StringBuilder(256);GetClassName(hwnd,cls,cls.Capacity);GetWindowRect(hwnd,out var rect);
            string process="未知";try{using var p=Process.GetProcessById((int)pid);process=p.ProcessName;}catch{}
            var children=new List<object>();EnumChildWindows(hwnd,(child,unused)=>{var c=new StringBuilder(256);GetClassName(child,c,c.Capacity);if(c.ToString().Contains("ListView",StringComparison.OrdinalIgnoreCase)||c.ToString().Contains("DefView",StringComparison.OrdinalIgnoreCase)){int count=-1;if(SendMessageTimeout(child,0x1004,IntPtr.Zero,IntPtr.Zero,2,100,out IntPtr n)!=IntPtr.Zero)count=n.ToInt32();GetWindowRect(child,out var r);children.Add(new{句柄=child.ToInt64(),类=c.ToString(),数量=count,区域=r});}return true;},IntPtr.Zero);
            result.Add(new{句柄=hwnd.ToInt64(),进程=process,进程号=pid,类=cls.ToString(),区域=rect,子视图=children});return true;
        },IntPtr.Zero);return result.ToArray();
    }
    internal static object Describe() {
        var views=new List<object>();foreach(var view in IconViews()){
            var name=new StringBuilder(256);GetClassName(view,name,name.Capacity);
            var list=name.ToString()=="SysListView32"?view:FindWindowEx(view,IntPtr.Zero,"SysListView32",null);GetClientRect(view,out var rect);
            int count=-1;if(list!=IntPtr.Zero&&SendMessageTimeout(list,0x1004,IntPtr.Zero,IntPtr.Zero,2,500,out IntPtr n)!=IntPtr.Zero)count=n.ToInt32();
            views.Add(new{视图=view.ToInt64(),图标列表=list.ToInt64(),宽=rect.Right-rect.Left,高=rect.Bottom-rect.Top,图标数=count});
        }return new{时间=DateTimeOffset.Now,视图=views,活动显示=DisplayNative.Query().Paths.Select(DisplayNative.Describe).ToArray(),窗口结构=WindowInventory()};
    }

    internal static IntPtr[] IconViews() {
        var shell=GetShellWindow();if(shell==IntPtr.Zero)return Array.Empty<IntPtr>();
        GetWindowThreadProcessId(shell,out uint shellPid);
        var views=new HashSet<IntPtr>();
        EnumWindows((hwnd,_)=>{
            GetWindowThreadProcessId(hwnd,out uint pid);if(pid!=shellPid)return true;
            var name=new StringBuilder(256);GetClassName(hwnd,name,name.Capacity);
            if(name.ToString()!="Progman"&&name.ToString()!="WorkerW")return true;
            EnumChildWindows(hwnd,(child,unused)=>{
                var cls=new StringBuilder(256);GetClassName(child,cls,cls.Capacity);
                // 桌面整理软件可能使用自绘子窗口，须包含整个桌面子树。
                views.Add(child);
                return true;
            },IntPtr.Zero);
            return true;
        },IntPtr.Zero);
        var result=new IntPtr[views.Count];views.CopyTo(result);return result;
    }

    internal static int RepaintOnce() {
        int count=0;
        foreach(var view in IconViews()) {
            // 只重绘图标视图，不刷新所有应用，也不让资源管理器重新排列图标。
            var name=new StringBuilder(256);GetClassName(view,name,name.Capacity);
            var list=name.ToString()=="SysListView32"?view:FindWindowEx(view,IntPtr.Zero,"SysListView32",null);
            if(list!=IntPtr.Zero){
                // 重新计算显示切换后可能残留的裁剪区域，不改变尺寸、位置、层次或焦点。
                SetWindowPos(list,IntPtr.Zero,0,0,0,0,0x1|0x2|0x4|0x10|0x20|0x200);
                if(SendMessageTimeout(list,0x1004,IntPtr.Zero,IntPtr.Zero,2,500,out IntPtr n)!=IntPtr.Zero&&n.ToInt64()>0)
                    SendMessageTimeout(list,0x1015,IntPtr.Zero,new IntPtr(n.ToInt64()-1),2,500,out _);
                RedrawWindow(list,IntPtr.Zero,IntPtr.Zero,0x1|0x4|0x100);
            }
            // 同步完成绘制，包含自绘子窗口及非客户区。
            if(RedrawWindow(view,IntPtr.Zero,IntPtr.Zero,0x1|0x4|0x80|0x100|0x400))count++;
        }
        return count;
    }

    internal static void AfterLayoutRestored() {
        try {
            int count=RepaintOnce();
            string id=Guid.NewGuid().ToString("N");File.WriteAllText(Path.Combine(Storage.Root,"桌面刷新批次.txt"),id);
            // 常驻进程先补绘，避免等待独立进程启动；独立进程仍保留后续补救。
            ThreadPool.QueueUserWorkItem(_=>{
                try{RunSchedule(id,new[]{80,120,200});}
                catch(Exception e){Storage.Log("桌面早期重绘失败",e.Message);}
            });
            using var worker=Storage.Launch("--desktop-refresh-worker",id);
            Storage.Log("桌面刷新已调度",new{图标视图数=count,批次=id});
        }catch(Exception e){Storage.Log("桌面重绘请求失败",e.Message);}
    }
    internal static int RunWorker(string id)=>RunSchedule(id,new[]{100,150,250,500,1500,3000});
    static int RunSchedule(string id,int[] delays) {
        var elapsed=Stopwatch.StartNew();
        foreach(int delay in delays) {
            Thread.Sleep(delay);
            if(!File.Exists(Path.Combine(Storage.Root,"桌面刷新批次.txt"))||File.ReadAllText(Path.Combine(Storage.Root,"桌面刷新批次.txt"))!=id)return 0;
            if(!DisplayNative.Query().Paths.Any(p=>DisplayNative.IsInternal(p.Target.Technology)))return 0;
            if(File.Exists(Storage.LeasePath))return 0;
            int count=RepaintOnce();
            Storage.Log("桌面子树同步重绘",new{批次=id,图标视图数=count,调度后毫秒=elapsed.ElapsedMilliseconds});
        }return 0;
    }
}
