using System.Collections.Concurrent;
using System.Diagnostics;

namespace BedrockServerManager.Helpers;

public sealed class BedrockProcessReader
{
    public ConcurrentQueue<string> OutputQueue { get; } = new();
    public ConcurrentQueue<string> ErrorQueue  { get; } = new();

    public void Attach(Process p)
    {
        p.OutputDataReceived += (_, e) => { if (e.Data != null) OutputQueue.Enqueue(e.Data); };
        p.ErrorDataReceived  += (_, e) => { if (e.Data != null) ErrorQueue.Enqueue(e.Data);  };
    }
}
