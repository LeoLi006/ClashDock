using System.Diagnostics;
using System.Runtime.InteropServices;
internal static class BrowserParent
{
    [StructLayout(LayoutKind.Sequential)] struct Basic { public IntPtr Reserved1, Peb, Reserved2a, Reserved2b, Id, Parent; }
    [DllImport("ntdll.dll")] static extern int NtQueryInformationProcess(IntPtr handle,int kind,ref Basic info,int size,out int length);
    internal static bool IsEdge()
    {
        try {
            using var self=Process.GetCurrentProcess(); var info=new Basic();
            if(NtQueryInformationProcess(self.Handle,0,ref info,Marshal.SizeOf<Basic>(),out _)!=0)return false;
            using var parent=Process.GetProcessById(info.Parent.ToInt32());
            // Chromium may launch native hosts through cmd.exe; inspect only this bounded ancestry.
            if(parent.ProcessName.Equals("cmd",StringComparison.OrdinalIgnoreCase)) {
                var next=new Basic();if(NtQueryInformationProcess(parent.Handle,0,ref next,Marshal.SizeOf<Basic>(),out _)!=0)return false;
                using var browser=Process.GetProcessById(next.Parent.ToInt32());return KnownEdge(browser);
            }
            return KnownEdge(parent);
        } catch { return false; }
    }
    static bool KnownEdge(Process p) => p.ProcessName.Equals("msedge",StringComparison.OrdinalIgnoreCase)
        && (p.MainModule?.FileName??"").Contains(@"\Microsoft\Edge\Application\",StringComparison.OrdinalIgnoreCase);
}
