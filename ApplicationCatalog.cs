using Microsoft.Win32;
using System.Diagnostics;
using System.Runtime.InteropServices;
namespace ClashDock;

public sealed record DiscoveredApp(string Name, string Path, string Source);
internal static class ApplicationCatalog
{
    internal static DiscoveredApp[] Normalize(IEnumerable<DiscoveredApp> source) => source
        .Where(x => !string.IsNullOrWhiteSpace(x.Name) && x.Path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        .GroupBy(x => x.Path, StringComparer.OrdinalIgnoreCase).Select(g => g.First())
        .OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    internal static Task<DiscoveredApp[]> ScanAsync(CancellationToken token)
    {
        var completion = new TaskCompletionSource<DiscoveredApp[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() => { try { completion.TrySetResult(Scan(token)); } catch (OperationCanceledException) { completion.TrySetCanceled(token); } catch (Exception e) { completion.TrySetException(e); } }) { IsBackground = true, Name = "ClashDock app discovery" };
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); return completion.Task;
    }
    private static DiscoveredApp[] Scan(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var apps = new List<DiscoveredApp>();
        void Add(string name, string path, string source)
        {
            path = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
            if (path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(path) && !new[] { "uninstall", "卸载", "unins" }.Any(v => name.Contains(v, StringComparison.OrdinalIgnoreCase)))
                apps.Add(new(name, System.IO.Path.GetFullPath(path), source));
        }
        object? shell = null;
        try
        {
            var type = Type.GetTypeFromProgID("WScript.Shell"); if (type != null) shell = Activator.CreateInstance(type);
            foreach (var root in new[] { Environment.SpecialFolder.Programs, Environment.SpecialFolder.CommonPrograms, Environment.SpecialFolder.DesktopDirectory, Environment.SpecialFolder.CommonDesktopDirectory }.Select(Environment.GetFolderPath).Distinct())
            {
                if (!Directory.Exists(root)) continue;
                var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
                try
                {
                    foreach (var file in Directory.EnumerateFiles(root, "*.lnk", options))
                    {
                        token.ThrowIfCancellationRequested(); object? shortcut = null;
                        try { if (shell != null) { shortcut = ((dynamic)shell).CreateShortcut(file); Add(System.IO.Path.GetFileNameWithoutExtension(file), (string)((dynamic)shortcut).TargetPath, "已安装应用"); } }
                        catch (Exception e) when (e is not OperationCanceledException) { }
                        finally { if (shortcut != null && Marshal.IsComObject(shortcut)) Marshal.FinalReleaseComObject(shortcut); }
                    }
                }
                catch (IOException) { }
            }
        }
        finally { if (shell != null && Marshal.IsComObject(shell)) Marshal.FinalReleaseComObject(shell); }
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    using var registry = RegistryKey.OpenBaseKey(hive, view);
                    using var paths = registry.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths");
                    if (paths == null) continue;
                    foreach (var name in paths.GetSubKeyNames()) { token.ThrowIfCancellationRequested(); using var key = paths.OpenSubKey(name); if (key?.GetValue("") is string path) Add(System.IO.Path.GetFileNameWithoutExtension(name), path, "系统注册应用"); }
                }
                catch (System.Security.SecurityException) { } catch (UnauthorizedAccessException) { } catch (IOException) { }
            }
        using var self = Process.GetCurrentProcess();
        int session = self.SessionId;
        foreach (var process in Process.GetProcesses())
            using (process)
            {
                token.ThrowIfCancellationRequested();
                try { var path = process.MainModule?.FileName; if (path != null && process.SessionId == session && process.MainWindowHandle != IntPtr.Zero) Add(process.ProcessName, path, "正在运行"); }
                catch (System.ComponentModel.Win32Exception) { } catch (InvalidOperationException) { } catch (NotSupportedException) { }
            }
        return Normalize(apps);
    }
}
