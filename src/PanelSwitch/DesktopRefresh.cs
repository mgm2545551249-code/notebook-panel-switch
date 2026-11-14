using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace PanelSwitch;

internal static class DesktopRefresh {
    delegate bool EnumWindowProc(IntPtr hwnd,IntPtr parameter);
    [DllImport("user32.dll")] static extern IntPtr GetShellWindow();
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowProc callback,IntPtr parameter);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd,StringBuilder text,int max);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr parent,IntPtr after,string? cls,string? title);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint pid);
    [DllImport("user32.dll",SetLastError=true)] static extern bool RedrawWindow(IntPtr hwnd,IntPtr rectangle,IntPtr region,uint flags);

    internal static IntPtr[] IconViews() {
        var shell=GetShellWindow();if(shell==IntPtr.Zero)return Array.Empty<IntPtr>();
        GetWindowThreadProcessId(shell,out uint shellPid);
        var views=new HashSet<IntPtr>();
        EnumWindows((hwnd,_)=>{
            GetWindowThreadProcessId(hwnd,out uint pid);if(pid!=shellPid)return true;
            var name=new StringBuilder(256);GetClassName(hwnd,name,name.Capacity);
            if(name.ToString()!="Progman"&&name.ToString()!="WorkerW")return true;
            var view=FindWindowEx(hwnd,IntPtr.Zero,"SHELLDLL_DefView",null);
            if(view!=IntPtr.Zero)views.Add(view);
            return true;
        },IntPtr.Zero);
        var result=new IntPtr[views.Count];views.CopyTo(result);return result;
    }

    internal static int RepaintOnce() {
        int count=0;
        foreach(var view in IconViews()) {
            // 只重绘图标视图，不刷新所有应用，也不让资源管理器重新排列图标。
            if(RedrawWindow(view,IntPtr.Zero,IntPtr.Zero,0x1|0x2|0x4|0x80))count++;
        }
        return count;
    }

    internal static void AfterLayoutRestored() {
        // 重建显示路径和桌面合成存在短暂延迟；每轮重新定位视图，不缓存旧窗口句柄。
        // 同步完成有限次请求，确保独立恢复进程退出前也执行完毕。
        try {
            int count=0;
            foreach(int delay in new[]{0,250,500}){if(delay>0)Thread.Sleep(delay);count=Math.Max(count,RepaintOnce());}
            Storage.Log("显示恢复后桌面重绘",new{图标视图数=count,重绘轮次=3});
        }catch(Exception e){Storage.Log("桌面重绘请求失败",e.Message);}
    }
}
