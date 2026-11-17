using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using SkiaSharp;

namespace WindowDock;

internal sealed class WallpaperView : Grid, IDisposable
{
    private readonly Border background = new();
    private readonly Image image = new() { Stretch = Stretch.UniformToFill, IsHitTestVisible = false };
    private readonly Border tint = new() { IsHitTestVisible = false };
    private readonly DispatcherTimer timer = new();
    private string key = "", contentKey = "";
    private WallpaperAsset? asset;
    private WriteableBitmap? bitmap;
    private static readonly SemaphoreSlim decodeSlots = new(2);
    private CancellationTokenSource? loadCancellation;
    private int version;
    private bool disposed, decoding, running = true;
    public string? Error { get; private set; }
    public event Action<string>? Failed;
    internal Task PendingLoad { get; private set; } = Task.CompletedTask;
    internal int FrameCount => asset?.FrameCount ?? 0;
    internal int FramesDecoded { get; private set; }
    internal int ImagesLoaded { get; private set; }
    internal uint FirstPixel
    {
        get { if (bitmap is null) return 0; using var locked = bitmap.Lock(); return unchecked((uint)System.Runtime.InteropServices.Marshal.ReadInt32(locked.Address)); }
    }
    internal long ResidentPixelBytes => (asset?.PixelBytes ?? 0) + (bitmap is null ? 0 : (long)bitmap.PixelSize.Width * bitmap.PixelSize.Height * 4);
    public WallpaperView()
    {
        IsHitTestVisible = false; ClipToBounds = true;
        Children.Add(background); Children.Add(image); Children.Add(tint); timer.Tick += (_, _) => _ = NextFrameAsync();
    }
    public void Configure(WorkspaceConfig config)
    {
        if (disposed) return;
        var next = $"{DockTheme.Id}|{config.BackgroundMode}|{config.BackgroundColor}|{config.BackgroundPath}";
        if (key == next) return; key = next;
        var defaultColor = System.Drawing.Color.FromArgb(19, 22, 27).ToArgb();
        background.Background = config.BackgroundMode == "transparent" ? Brushes.Transparent : config.BackgroundColor == defaultColor ? DockTheme.IsGlass ? DockTheme.Material : DockTheme.Ground : new SolidColorBrush(Color.FromUInt32(unchecked((uint)config.BackgroundColor)));
        image.Effect = DockTheme.IsGlass ? new BlurEffect { Radius = DockTheme.Id == "glass" ? 3 : 5 } : null;
        tint.Background = DockTheme.IsGlass && config.BackgroundMode == "image" ? new SolidColorBrush(Color.FromArgb(DockTheme.Id == "glass" ? (byte)24 : (byte)54, 0, 0, 0)) : Brushes.Transparent;
        var requested = config.BackgroundMode == "image" ? config.BackgroundPath ?? "" : "";
        if (contentKey == requested) return;
        contentKey = requested; ReleaseImage(); Error = null;
        if (string.IsNullOrWhiteSpace(requested)) return;
        var generation = version;
        var top = TopLevel.GetTopLevel(this);
        var screen = top is { Screens: { } screens } ? screens.ScreenFromTopLevel(top) ?? screens.Primary : null;
        var edge = Math.Clamp(screen is null ? 1920 : Math.Max(screen.Bounds.Width, screen.Bounds.Height), 1920, 4096);
        loadCancellation = new(); PendingLoad = LoadAsync(requested, edge, generation, loadCancellation.Token);
    }
    public void SetRunning(bool value)
    {
        running = value;
        if (!running) timer.Stop(); else ScheduleFrame();
    }
    private static async Task<T> DecodeAsync<T>(Func<T> decode, CancellationToken token)
    {
        await decodeSlots.WaitAsync(token).ConfigureAwait(false);
        try { return await Task.Run(decode, token).ConfigureAwait(false); }
        finally { decodeSlots.Release(); }
    }
    private async Task LoadAsync(string path, int edge, int generation, CancellationToken token)
    {
        WallpaperAsset? loaded = null;
        try
        {
            loaded = await DecodeAsync(() => WallpaperAsset.Load(path, edge), token);
            if (disposed || generation != version) { loaded.Release(); return; }
            asset = loaded;
            bitmap = new(new(loaded.Width, loaded.Height), new(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
            image.Source = bitmap; ImagesLoaded++; CommitFrame(loaded); ScheduleFrame();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { loaded?.Release(); }
        catch (Exception exception)
        {
            if (disposed || generation != version) { loaded?.Release(); return; }
            ReleaseImage(); Error = L10n.Format("WallpaperView.BackgroundCouldNotBeLoaded", exception.Message); Failed?.Invoke(Error);
        }
    }
    private async Task NextFrameAsync()
    {
        timer.Stop();
        if (asset is not { FrameCount: > 1 } current || !running || disposed || decoding) return;
        var generation = version; var token = loadCancellation?.Token ?? default; decoding = true; current.AddRef();
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            await DecodeAsync(() => { current.NextFrame(); return true; }, token);
            if (!disposed && generation == version && ReferenceEquals(asset, current)) CommitFrame(current);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception)
        {
            if (!disposed && generation == version) { ReleaseImage(); Error = L10n.Format("WallpaperView.BackgroundCouldNotBeLoaded", exception.Message); Failed?.Invoke(Error); }
        }
        finally
        {
            current.Release();
            if (generation == version && !disposed)
            { decoding = false; ScheduleFrame(System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds); }
        }
    }
    private void ScheduleFrame(double elapsed = 0)
    {
        if (disposed || !running || decoding || asset is not { FrameCount: > 1 } current) return;
        timer.Interval = TimeSpan.FromMilliseconds(Math.Clamp(current.Duration - elapsed, 20, 10000)); timer.Start();
    }
    private void CommitFrame(WallpaperAsset current)
    {
        if (bitmap is null) return;
        using var locked = bitmap.Lock(); using var pixels = current.Output.PeekPixels();
        if (!pixels.ReadPixels(current.Output.Info, locked.Address, locked.RowBytes)) throw new InvalidOperationException(L10n.Get("WallpaperView.CouldNotUpdateTheImage"));
        FramesDecoded++; image.InvalidateVisual();
    }
    private void ReleaseImage()
    {
        version++; timer.Stop(); decoding = false;
        loadCancellation?.Cancel(); loadCancellation?.Dispose(); loadCancellation = null;
        image.Source = null; bitmap?.Dispose(); bitmap = null;
        var previous = asset; asset = null; previous?.Release();
    }
    public void Dispose() { if (disposed) return; disposed = true; ReleaseImage(); }
}

// Reference-counted because switching a tab may outlive an in-flight native decode.
// Workers own decoding, the UI only copies a ready, bounded raster once.
internal sealed class WallpaperAsset
{
    private static int liveAssets;
    internal static int LiveAssets => Volatile.Read(ref liveAssets);
    private WallpaperAsset() => Interlocked.Increment(ref liveAssets);
    private SKData? data;
    private SKCodec? codec;
    private SKBitmap? source;
    private int references = 1, frame, previousFrame = -1;
    private SKCodecFrameInfo[] frames = [];
    public SKBitmap Output { get; private set; } = null!;
    public int Width => Output.Width;
    public int Height => Output.Height;
    public int FrameCount { get; private set; }
    public int Duration => frames.Length > frame && frames[frame].Duration > 0 ? frames[frame].Duration : 100;
    public long PixelBytes => (long)Output.ByteCount + (ReferenceEquals(source, Output) ? 0 : source?.ByteCount ?? 0);
    public static WallpaperAsset Load(string path, int edge)
    {
        var result = new WallpaperAsset();
        try
        {
            var file = new FileInfo(path); if (file.Length > 80 * 1024 * 1024) throw new InvalidOperationException(L10n.Get("WallpaperView.TheImageExceedsMB"));
            result.data = SKData.Create(file.FullName) ?? throw new InvalidOperationException(L10n.Get("WallpaperView.TheImageFileIsUnavailable"));
            result.codec = SKCodec.Create(result.data) ?? throw new InvalidOperationException(L10n.Get("WallpaperView.UnsupportedImageFormat"));
            var original = result.codec.Info;
            if (original.Width <= 0 || original.Height <= 0 || (long)original.Width * original.Height > 50_000_000) throw new InvalidOperationException(L10n.Get("WallpaperView.TheImageResolutionIsTooLarge"));
            result.FrameCount = Math.Max(1, result.codec.FrameCount); result.frames = result.codec.FrameInfo;
            var limit = result.FrameCount > 1 ? Math.Min(1600, edge) : edge;
            var scale = Math.Min(1.0, limit / (double)Math.Max(original.Width, original.Height));
            var target = new SKImageInfo(Math.Max(1, (int)Math.Round(original.Width * scale)), Math.Max(1, (int)Math.Round(original.Height * scale)), SKColorType.Bgra8888, SKAlphaType.Premul);
            var nativeSize = result.codec.GetScaledDimensions((float)scale);
            // GIF codecs may require their full canvas; JPEG/WebP can decode directly smaller.
            result.source = new(new SKImageInfo(nativeSize.Width, nativeSize.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
            result.Output = nativeSize.Width == target.Width && nativeSize.Height == target.Height ? result.source : new(target);
            result.Decode();
            if (result.FrameCount == 1)
            {
                if (!ReferenceEquals(result.source, result.Output)) result.source.Dispose(); result.source = null;
                result.codec.Dispose(); result.codec = null; result.data.Dispose(); result.data = null;
            }
            return result;
        }
        catch { result.Release(); throw; }
    }
    public void NextFrame() { frame = (frame + 1) % FrameCount; Decode(); }
    private void Decode()
    {
        var result = codec!.GetPixels(source!.Info, source.GetPixels(), new SKCodecOptions(frame, frame == 0 ? -1 : previousFrame));
        if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput)) throw new InvalidOperationException(L10n.Get("WallpaperView.TheImageFrameIsDamaged"));
        previousFrame = frame;
        if (!ReferenceEquals(source, Output))
        {
            using var input = source.PeekPixels(); using var output = Output.PeekPixels();
            if (!input.ScalePixels(output, new SKSamplingOptions(SKFilterMode.Linear))) throw new InvalidOperationException(L10n.Get("WallpaperView.CouldNotResizeTheImage"));
        }
    }
    public void AddRef() => Interlocked.Increment(ref references);
    public void Release()
    {
        if (Interlocked.Decrement(ref references) != 0) return;
        Interlocked.Decrement(ref liveAssets);
        if (!ReferenceEquals(source, Output)) source?.Dispose(); Output?.Dispose(); codec?.Dispose(); data?.Dispose();
    }
}