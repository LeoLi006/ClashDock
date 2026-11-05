using System.Runtime.InteropServices;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
namespace ClashDock;
internal sealed class SingleInstance : IDisposable
{
    readonly Mutex mutex;
    readonly string key;
    readonly CancellationTokenSource life = new();
    public bool Primary { get; }
    public SingleInstance()
    {
        key = "ClashDock-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Environment.UserName + AppContext.BaseDirectory.ToLowerInvariant())))[..20];
        mutex = new Mutex(true, @"Local\" + key, out bool created); Primary = created;
    }
    public void Listen(Action open) => _ = Task.Run(async () => {
        while (!life.IsCancellationRequested) try {
            using var pipe = new NamedPipeServerStream(key, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await pipe.WaitForConnectionAsync(life.Token); var bytes = new byte[8]; if (await pipe.ReadAsync(bytes, life.Token) > 0) open();
        } catch (OperationCanceledException) { break; } catch (IOException) { }
    });
    [DllImport("user32.dll")] static extern bool AllowSetForegroundWindow(int processId);
    public void Activate() { AllowSetForegroundWindow(-1); using var pipe = new NamedPipeClientStream(".", key, PipeDirection.Out); pipe.Connect(4000); pipe.Write(new byte[] { 1 }); }
    public void Dispose() { life.Cancel(); if (Primary) mutex.ReleaseMutex(); mutex.Dispose(); life.Dispose(); }
}
