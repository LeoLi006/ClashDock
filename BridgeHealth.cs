using System.Diagnostics;
namespace ClashDock;
public static class BridgeHealth
{
    public static DateTimeOffset? LastBrowserRequest { get; private set; }
    public static void ObserveBrowserRequest() => LastBrowserRequest=DateTimeOffset.UtcNow;
    public static string BrowserSummary => LastBrowserRequest is {} time
        ? $"观察到 Edge 发起的只读桥请求：{time.ToLocalTime():yyyy-MM-dd HH:mm:ss}。仅为本次宿主会话历史记录，不代表当前连接仍正常。"
        : "本次宿主会话尚未观察到真实 Edge 桥请求；直接启动桥探测不算浏览器握手。";
    public static async Task<string> RunScript(string name,string directory,CancellationToken ct,bool asJson=false)
    {
        if(name is not ("Check-Bridge.ps1" or "Register-Bridge.ps1"))throw new ArgumentException();
        var script=Path.Combine(directory,"BridgeTools",name);
        var info=new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),@"WindowsPowerShell\v1.0\powershell.exe")) {
            UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=System.Text.Encoding.UTF8,StandardErrorEncoding=System.Text.Encoding.UTF8
        };
        // Fixed script with ArgumentList: no shell interpolation or arbitrary script/program path from extension.
        foreach(var arg in new[]{"-NoProfile","-NonInteractive","-File",script,"-InstallDirectory",directory,"-Utf8Output"})info.ArgumentList.Add(arg);
        if(asJson && name=="Check-Bridge.ps1")info.ArgumentList.Add("-AsJson");
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(TimeSpan.FromSeconds(15));
        using var process=Process.Start(info)??throw new IOException("脚本进程无法启动");
        try {
            var output=process.StandardOutput.ReadToEndAsync(timeout.Token);var error=process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);var text=await output;var failure=await error;
            if(process.ExitCode!=0)throw new IOException((failure.Length>1600?failure[..1600]:failure)+"；未修改浏览器策略，请检查脚本执行权限。");
            return text.Length>4000?text[..4000]:text;
        } finally { if(!process.HasExited)try{process.Kill(true);}catch{} }
    }
    public static string FormatInstallation(string json)
    {
        var checks=System.Text.Json.JsonSerializer.Deserialize<Dictionary<string,bool>>(json)??throw new IOException("检查脚本未返回有效状态");
        var fields=new (string Key,string Label)[]{("DesktopPresent","文件 · 桌面 EXE"),("BridgePresent","文件 · 通信桥 EXE"),("HostManifestPresent","清单 · 文件存在"),("HostManifestValid","清单 · JSON 可解析"),("HostNameMatches","清单 · 固定宿主名称"),("StdioTransport","清单 · 标准输入输出协议"),("HostPathMatches","清单 · 当前安装 EXE 路径"),("CurrentUserRegistered","注册 · HKCU 指向本安装"),("ExpectedOrigin","来源 · 仅允许固定扩展"),("ExtensionManifestPresent","扩展 · 清单存在"),("ExtensionIdMatches","扩展 · 公钥派生 ID 匹配")};
        return string.Join(Environment.NewLine,fields.Select(f=>$"{f.Label}：{(checks.GetValueOrDefault(f.Key)?"通过":"缺失／不匹配")}"))+Environment.NewLine+"上述安装检查不等于真实浏览器握手。"+Environment.NewLine;
    }
    internal static void Show(IWin32Window owner)
    {
        using var form=new Form{Text="Edge 通信桥 · 当前安装",ClientSize=new(760,510),MinimumSize=new(650,420),StartPosition=FormStartPosition.CenterParent,Padding=new(20)};UiStyle.Window(form);
        using var life=new CancellationTokenSource();
        var bar=new FlowLayoutPanel{Dock=DockStyle.Top,AutoSize=true};var check=new PillButton{Text="只读检查",AutoSize=true,Outline=true};var register=new PillButton{Text="注册／修复当前安装",AutoSize=true,Accent=true};bar.Controls.AddRange([check,register]);
        var text=new TextBox{Dock=DockStyle.Fill,Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,BackColor=Theme.Card,ForeColor=Theme.Text,BorderStyle=BorderStyle.None};form.Controls.Add(text);form.Controls.Add(bar);
        async Task Run(bool repair) {
            check.Enabled=register.Enabled=false;text.Text="正在检查当前安装…";
            try {
                if(repair){await RunScript("Register-Bridge.ps1",AppContext.BaseDirectory,life.Token);}
                var result=await RunScript("Check-Bridge.ps1",AppContext.BaseDirectory,life.Token,true);
                if(form.IsDisposed||life.IsCancellationRequested)return;
                text.Text=FormatInstallation(result)+Environment.NewLine+BrowserSummary+Environment.NewLine+"扩展需要在普通网页点击图标后才能验证浏览器请求。没有证据时不要关闭或重启 Edge。注册仅涉及本安装目录及 HKCU；不会安装扩展或修改安全策略。";
            }catch(OperationCanceledException){if(!form.IsDisposed)text.Text="检查已取消或超时；不据此认定注册或握手成功。";}
            catch(Exception e){if(!form.IsDisposed)text.Text="检查／注册未完成："+e.Message;}
            finally{if(!form.IsDisposed){check.Enabled=register.Enabled=true;}}
        }
        check.Click+=async(_,_)=>await Run(false);
        register.Click+=async(_,_)=>{if(MessageBox.Show(form,"仅注册当前安装目录的通信桥清单与 HKCU 项；不更改浏览器安全策略。是否继续？","注册当前安装",MessageBoxButtons.OKCancel)==DialogResult.OK)await Run(true);};
        form.Shown+=async(_,_)=>await Run(false);form.FormClosing+=(_,_)=>life.Cancel();form.KeyPreview=true;form.KeyDown+=(_,e)=>{if(e.KeyCode==Keys.Escape)form.Close();};form.ShowDialog(owner);
    }
}
