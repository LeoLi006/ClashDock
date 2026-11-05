using System.Text.Json;
namespace ClashDock;
public sealed class PinStore
{
    readonly string? file;readonly HashSet<string> paths=new(StringComparer.Ordinal);
    public PinStore(string? file=null,bool memory=false){this.file=memory?null:file??Path.Combine(AppContext.BaseDirectory,"ui-preferences.json");try{if(this.file!=null&&File.Exists(this.file)&&new FileInfo(this.file).Length<=1024*1024)foreach(var path in JsonSerializer.Deserialize<string[]>(File.ReadAllText(this.file))??[])if(paths.Count<1024&&ConnectionMatch.PathId(path).Length>0)paths.Add(ConnectionMatch.PathId(path));}catch(Exception e)when(e is IOException or UnauthorizedAccessException or JsonException){} }
    public bool IsPinned(string path)=>paths.Contains(ConnectionMatch.PathId(path));
    public void Toggle(string path){string key=ConnectionMatch.PathId(path);if(key.Length==0)throw new ArgumentException("置顶需要应用的完整路径，请先补充程序路径。");bool was=paths.Remove(key);if(!was){if(paths.Count>=1024)throw new InvalidOperationException("置顶数量已达上限。");paths.Add(key);}try{if(file!=null){File.WriteAllText(file+".tmp",JsonSerializer.Serialize(paths.Order().ToArray()));File.Move(file+".tmp",file,true);}}catch{if(was)paths.Add(key);else paths.Remove(key);throw;}}
}
public static class WebsiteTemplates
{
    public sealed record Template(string Id,string Name,string[] Domains);
    public static readonly Template[] All=[new("bilibili","哔哩哔哩",["bilibili.com","bilivideo.com","hdslb.com","biliapi.net","biliapi.com"]),new("github","GitHub",["github.com","githubusercontent.com","githubassets.com"]),new("youtube","YouTube",["youtube.com","googlevideo.com","ytimg.com"]),new("google","Google",["google.com"]),new("chatgpt","ChatGPT",["chatgpt.com"]),new("gemini","Gemini",["gemini.google.com"])];
}
