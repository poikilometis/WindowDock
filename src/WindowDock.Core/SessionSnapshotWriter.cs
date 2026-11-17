namespace WindowDock;

// One immutable snapshot queue per session. Disk work never touches UI-owned models.
// A synchronous final save and queued writes share a commit gate and sequence, so an
// older worker cannot overwrite a newer Save/Close, even when I/O completes late.
internal sealed class SessionSnapshotWriter(string configPath, string groupsPath)
{
    internal sealed record Snapshot(long Sequence, byte[] Config, byte[] Groups);
    private readonly object queueGate = new(), commitGate = new();
    private Snapshot? pending;
    private bool running;
    private long sequence, committed, attempted;
    private byte[]? lastConfig;
    private DateTime configStamp;
    private Exception? error;
    internal Snapshot Capture(AppConfig config, GroupState groups) => new(Interlocked.Increment(ref sequence), Store.Serialize(config), Store.Serialize(groups));
    internal void Enqueue(Snapshot snapshot)
    {
        lock (queueGate)
        {
            if (pending is null || snapshot.Sequence > pending.Sequence) pending = snapshot;
            if (running) return;
            running = true; _ = Task.Run(Run);
        }
    }
    private void Run()
    {
        while (true)
        {
            Snapshot next;
            lock (queueGate)
            {
                if (pending is null) { running = false; return; }
                next = pending; pending = null;
            }
            try { WriteNow(next); } catch (Exception exception) { Interlocked.Exchange(ref error, exception); }
        }
    }
    internal Exception? TakeError() => Interlocked.Exchange(ref error, null);
    internal void WriteNow(Snapshot snapshot)
    {
        lock (commitGate)
        {
            if (snapshot.Sequence <= committed || snapshot.Sequence < attempted) return;
            attempted = snapshot.Sequence;
            if (lastConfig is null || !snapshot.Config.AsSpan().SequenceEqual(lastConfig) || !File.Exists(configPath) || File.GetLastWriteTimeUtc(configPath) != configStamp)
            {
                Store.WriteBytes(configPath, snapshot.Config); lastConfig = snapshot.Config; configStamp = File.GetLastWriteTimeUtc(configPath);
            }
            GroupPersistence.SaveBytes(groupsPath, snapshot.Groups); committed = snapshot.Sequence;
        }
    }
}