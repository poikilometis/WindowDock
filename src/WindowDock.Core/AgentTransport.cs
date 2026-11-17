using System.IO.Pipes;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;

namespace WindowDock;

internal static class AgentBridge
{
    public const int Signature = 0x57444153;
    public const int MaximumBytes = 16384;
    public static string PipeName { get; } = "WindowDock." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar).ToUpperInvariant())))[..24];
    public static bool ValidStatus(string? value) => value is "working" or "waiting" or "done" or "error" or "clear" or "probe";
    public static byte[] Encode(AgentSignal signal)
    {
        using var stream = new MemoryStream();
        ContractJson<AgentSignal>.Serializer.WriteObject(stream, signal); return stream.ToArray();
    }
    public static AgentSignal? Decode(byte[] bytes)
    {
        if (bytes.Length is < 2 or > MaximumBytes) return null;
        using var stream = new MemoryStream(bytes);
        return ContractJson<AgentSignal>.Serializer.ReadObject(stream) as AgentSignal;
    }
    public static async Task<int> SendSignalAsync(AgentSignal signal, CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(1800));
        try
        {
            using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await pipe.ConnectAsync(timeout.Token);
            var bytes = Encode(signal); if (bytes.Length > MaximumBytes) return 2;
            await pipe.WriteAsync(BitConverter.GetBytes(bytes.Length), timeout.Token);
            await pipe.WriteAsync(bytes, timeout.Token); await pipe.FlushAsync(timeout.Token);
            byte[] answer = new byte[1]; await pipe.ReadExactlyAsync(answer, timeout.Token);
            return answer[0] == 1 ? 0 : 4;
        }
        catch (OperationCanceledException) { return 3; }
        catch (IOException) { return 3; }
    }
    public static int SendSignal(AgentSignal signal) => SendSignalAsync(signal).GetAwaiter().GetResult();
    public static async Task<int> SendAsync(string[] arguments)
    {
        if (arguments.Length < 2 || !ValidStatus(arguments[1])) return 2;
        var signal = new AgentSignal { Status = arguments[1], Message = "", AgentId = Environment.GetEnvironmentVariable("WINDOWDOCK_AGENT_ID") };
        for (var index = 2; index < arguments.Length; index++)
        {
            if (index + 1 >= arguments.Length) return 2;
            var key = arguments[index++]; var value = arguments[index];
            switch (key)
            {
                case "--hwnd": if (!long.TryParse(value, out signal.Handle)) return 2; break;
                case "--agent-id": signal.AgentId = value; break;
                case "--message": signal.Message = value; break;
                default: return 2;
            }
        }
        if (signal.Handle == 0 && string.IsNullOrWhiteSpace(signal.AgentId)) return 2;
        signal.Message = signal.Message[..Math.Min(2000, signal.Message.Length)];
        return await SendSignalAsync(signal);
    }
}

internal sealed class AgentSignalServer : IAsyncDisposable
{
    private readonly CancellationTokenSource cancellation = new();
    private readonly Task worker;
    public AgentSignalServer(Func<AgentSignal, Task<bool>> receive) => worker = Task.WhenAll(Enumerable.Range(0, 4).Select(_ => RunAsync(receive)));
    private async Task RunAsync(Func<AgentSignal, Task<bool>> receive)
    {
        while (!cancellation.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(AgentBridge.PipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(cancellation.Token);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token);
                timeout.CancelAfter(TimeSpan.FromMilliseconds(1400));
                byte[] header = new byte[4]; await pipe.ReadExactlyAsync(header, timeout.Token);
                var length = BitConverter.ToInt32(header); if (length is < 2 or > AgentBridge.MaximumBytes) continue;
                byte[] body = new byte[length]; await pipe.ReadExactlyAsync(body, timeout.Token);
                var signal = AgentBridge.Decode(body);
                var accepted = signal is not null && AgentBridge.ValidStatus(signal.Status) && await receive(signal).WaitAsync(timeout.Token);
                await pipe.WriteAsync(new byte[] { accepted ? (byte)1 : (byte)0 }, timeout.Token);
            }
            catch (OperationCanceledException) { }
            catch (Exception exception) { Store.Log("Agent IPC: " + exception.Message); }
        }
    }
    public async ValueTask DisposeAsync()
    {
        cancellation.Cancel(); await worker.ConfigureAwait(false); cancellation.Dispose();
    }
}