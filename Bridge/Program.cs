using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading.Channels;
using ClashDock;
internal static class Program
{
    static async Task<int> Main(string[] args)
    {
        if(args.Length<1||args[0]!=BridgeProtocol.Origin)return 3;
        using var input=Console.OpenStandardInput();using var output=Console.OpenStandardOutput();using var life=new CancellationTokenSource();
        var requests=Channel.CreateBounded<byte[]>(new BoundedChannelOptions(16){SingleReader=true,SingleWriter=true,FullMode=BoundedChannelFullMode.Wait});
        var reading=ReadRequests(input,requests.Writer,life);
        try {
            await foreach(var bytes in requests.Reader.ReadAllAsync(life.Token)) {
                using var timeout=CancellationTokenSource.CreateLinkedTokenSource(life.Token);timeout.CancelAfter(TimeSpan.FromSeconds(18));
                BridgeRequest? request=null;BridgeReply reply;
                try {
                    request=JsonSerializer.Deserialize<BridgeRequest>(bytes,BridgeProtocol.Json)??throw new JsonException();
                    string? error=BridgeProtocol.Validate(request,DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                    if(error!=null)reply=BridgeProtocol.Error(request.Id,error);
                    else if(request.Op=="start") {
                        // No supplied path/arguments: only this fixed adjacent distribution application.
                        string exe=Path.Combine(AppContext.BaseDirectory,"ClashDock.exe");
                        if(!File.Exists(exe))reply=BridgeProtocol.Error(request.Id,"desktop_missing");
                        else {life.Token.ThrowIfCancellationRequested();Process.Start(new ProcessStartInfo(exe,"--silent"){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden});reply=new(1,request.Id,true,"starting","正在启动 ClashDock；启动完成后刷新状态。");}
                    } else reply=await ForwardAsync(request,timeout.Token);
                } catch(JsonException){reply=BridgeProtocol.Error(request?.Id??"","invalid_message");}
                catch(Exception e)when(e is IOException or OperationCanceledException or InvalidOperationException or System.ComponentModel.Win32Exception){if(life.IsCancellationRequested)break;reply=BridgeProtocol.Error(request?.Id??"","bridge_failed");}
                await BridgeProtocol.WriteAsync(output,JsonSerializer.SerializeToUtf8Bytes(reply,BridgeProtocol.Json),timeout.Token);
            }
        } catch(Exception e)when(e is IOException or OperationCanceledException){ }
        finally{life.Cancel();await reading;}
        return 0;
    }
    static async Task ReadRequests(Stream input,ChannelWriter<byte[]> writer,CancellationTokenSource life)
    {
        try{while(!life.IsCancellationRequested){using var idle=CancellationTokenSource.CreateLinkedTokenSource(life.Token);idle.CancelAfter(TimeSpan.FromSeconds(30));var bytes=await BridgeProtocol.ReadAsync(input,idle.Token);if(bytes==null)break;await writer.WriteAsync(bytes,life.Token);}}
        catch(Exception e)when(e is IOException or OperationCanceledException){ }
        finally{writer.TryComplete();life.Cancel();}
    }
    static async Task<BridgeReply> ForwardAsync(BridgeRequest request,CancellationToken ct)
    {
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(TimeSpan.FromSeconds(15));
        using var pipe=new NamedPipeClientStream(".",BridgeProtocol.PipeName(AppContext.BaseDirectory),PipeDirection.InOut,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
        try{await pipe.ConnectAsync(1000,timeout.Token);}catch(Exception e)when(e is IOException or TimeoutException or OperationCanceledException){return BridgeProtocol.Error(request.Id,"desktop_offline");}
        try{
            await BridgeProtocol.WriteAsync(pipe,JsonSerializer.SerializeToUtf8Bytes(new BridgeEnvelope(BridgeProtocol.Origin,request),BridgeProtocol.Json),timeout.Token);
            var data=await BridgeProtocol.ReadAsync(pipe,timeout.Token);if(data==null)return BridgeProtocol.Error(request.Id,"disconnected");
            var reply=JsonSerializer.Deserialize<BridgeReply>(data,BridgeProtocol.Json);
            if(reply==null||reply.Id!=request.Id||reply.Version!=1)return BridgeProtocol.Error(request.Id,"stale_response");return reply;
        }catch(OperationCanceledException){return BridgeProtocol.Error(request.Id,"expired");}
        catch(IOException){return BridgeProtocol.Error(request.Id,"disconnected");}
    }
}
