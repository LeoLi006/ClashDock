using System.Buffers.Binary;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
namespace ClashDock;
public sealed class BridgeRequest
{
    public int Version { get; set; }
    public string Id { get; set; } = "";
    public string Op { get; set; } = "";
    public string Domain { get; set; } = "";
    public string Mode { get; set; } = "";
    public string Client { get; set; } = "";
    public long Sequence { get; set; }
    public string Session { get; set; } = "";
    public long BaseRevision { get; set; }
    public long ExpiresAt { get; set; }
    public string Template { get; set; } = "";
    public string[] Domains { get; set; } = [];
}
public sealed record BridgeEnvelope(string Origin,BridgeRequest Request);
public sealed record BridgeReply(int Version,string Id,bool Ok,string Code,string Message,JsonElement? Data=null);
public static class BridgeProtocol
{
    public const string ExtensionId="opbdfefdnighegepeiffmioofdkkphnk";
    public const string Origin="chrome-extension://"+ExtensionId+"/";
    public const string Host="com.clashdock.bridge";
    public const int Limit=65536;
    public static readonly JsonSerializerOptions Json=new(){PropertyNamingPolicy=JsonNamingPolicy.CamelCase,UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow,MaxDepth=12};
    public static string PipeName(string directory)=>"ClashDock-bridge-"+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Environment.UserName+Path.GetFullPath(directory).TrimEnd('\\','/').ToLowerInvariant())))[..20];
    static bool Token(string? s)=>s!=null&&Regex.IsMatch(s,"^[a-f0-9]{32}$",RegexOptions.CultureInvariant);
    public static bool Mutation(string op)=>op is "setSite" or "applyTemplate" or "retry";
    public static string? Validate(BridgeRequest r,long now)
    {
        if(r.Version!=1)return "version_mismatch";
        if(!Token(r.Id)||!Token(r.Client))return "bad_request_id";
        if(r.Op is not("state" or "setSite" or "templates" or "applyTemplate" or "retry" or "check" or "start"))return "unknown_operation";
        if(r.ExpiresAt<now || r.ExpiresAt>now+30000)return "expired";
        if(r.Domain==null||r.Domain.Length>512||r.Domain.Any(char.IsControl))return "invalid_domain";
        if(r.Op is "state" or "setSite" or "applyTemplate" or "retry" or "check" && r.Domain.Length==0)return "invalid_domain";
        if(r.Domains==null||r.Domains.Length>16||r.Domains.Any(x=>x==null||x.Length>253||x.Any(char.IsControl)))return "invalid_domains";
        if(r.Mode is not("" or "自动" or "直连" or "代理")||r.Template==null||r.Template.Length>32)return "invalid_parameter";
        if(Mutation(r.Op)&&(!Token(r.Session)||r.Sequence<1||r.Sequence>1000000||r.BaseRevision<0||r.Mode==""&&r.Op!="retry"))return "invalid_mutation";
        return null;
    }
    public static async Task<byte[]?> ReadAsync(Stream stream,CancellationToken ct)
    {
        var header=new byte[4];int got=await stream.ReadAsync(header.AsMemory(0,1),ct);if(got==0)return null;
        await stream.ReadExactlyAsync(header.AsMemory(1,3),ct);int length=BinaryPrimitives.ReadInt32LittleEndian(header);
        if(length<2||length>Limit)throw new InvalidDataException("Invalid message length");var bytes=new byte[length];await stream.ReadExactlyAsync(bytes,ct);return bytes;
    }
    public static async Task WriteAsync(Stream stream,byte[] bytes,CancellationToken ct)
    {
        if(bytes.Length<2||bytes.Length>Limit)throw new InvalidDataException("Invalid message length");var header=new byte[4];BinaryPrimitives.WriteInt32LittleEndian(header,bytes.Length);
        await stream.WriteAsync(header,ct);await stream.WriteAsync(bytes,ct);await stream.FlushAsync(ct);
    }
    public static BridgeReply Error(string id,string code)=>new(1,id,false,code,code switch {
        "desktop_offline"=>"ClashDock 未运行，请点击启动。","controller_offline"=>"控制器未连接，请先检查 Verge。","expired"=>"请求已超时；先刷新状态，不自动重复修改。","conflict"=>"配置已变化，请刷新后重试。","session_changed"=>"桌面端已重启，请刷新后重新选择。","stale_sequence"=>"已忽略过期选择，请刷新状态。","invalid_domain"=>"此地址不支持网站分流。","unauthorized_origin"=>"扩展未获准。","duplicate_conflict"=>"请求编号与原内容不一致。",_=>"请求未执行（"+code+"），请检查桌面端。"});
}
