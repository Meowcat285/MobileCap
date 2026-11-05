using MobileCap.Core.Ar;
using MobileCap.Core.Export;

namespace MobileCap.Core.Capture;

/// <param name="PredictedBlurPx">As of the latest AR frame.</param>
/// <param name="Busy">A still is being taken or written.</param>
public readonly record struct CaptureStatus(
    bool Ready,
    HoldReason Hold,
    double? PredictedBlurPx,
    int Shots,
    int Gaps,
    bool Busy,
    bool AutoCapture);

/// <summary>
/// Runs one Scene mode capture on a live AR session: feeds every frame to
/// <see cref="SceneAutoCapture"/>, takes the stills it asks for (or the shutter does), and writes
/// kept shots into the capture folder.
/// </summary>
/// <remarks>
/// ARKit raises frames on its delegate queue and completes stills on another thread, so every
/// call into <see cref="SceneAutoCapture"/> holds one lock, and only one still is in flight at a
/// time. Events are raised on whichever of those threads caused them.
/// </remarks>
public sealed class CaptureController : IAsyncDisposable
{
    // Frames arrive at 60 Hz; the HUD needs the blur figure a few times a second, not every frame.
    private static readonly TimeSpan StatusInterval = TimeSpan.FromMilliseconds(200);

    private readonly IArSession _ar;
    private readonly IFrameSource _stills;
    private readonly CaptureFolder _folder;
    private readonly CaptureSettings _settings;
    private readonly Func<GeoLocation?>? _location;
    private readonly TimeProvider _time;
    private readonly CancellationTokenSource _stop = new();
    private readonly CancellationTokenSource _startStop = new();
    private readonly HashSet<int> _unwritten = [];
    private readonly Lock _gate = new();
    private readonly Lock _publishGate = new();

    private DeviceInfo _device;
    private SceneAutoCapture? _auto;
    private Task? _inFlight, _starting;
    private bool _finishing, _disposed;
    private bool _autoCapture = true;
    private HoldReason _hold = HoldReason.Warmup;
    private double? _predicted;
    private long _statusSequence, _publishedSequence = -1;
    private CaptureStatus? _published;
    private TimeSpan? _publishedAt;

    /// <param name="location">Asked once per still, and only when GPS is on in the settings.</param>
    public CaptureController(IArSession ar, IFrameSource stills, CaptureFolder folder, CaptureSettings settings,
        DeviceInfo device, DateTimeOffset startedAt, Func<GeoLocation?>? location = null, TimeProvider? time = null)
    {
        _ar = ar;
        _stills = stills;
        _folder = folder;
        _settings = settings;
        _device = device;
        _location = settings.WriteGpsToExif ? location : null;
        _time = time ?? TimeProvider.System;
        StartedAt = startedAt;
        _ar.FrameUpdated += OnFrame;
    }

    public DateTimeOffset StartedAt { get; }

    public string Name => Path.GetFileName(Path.TrimEndingDirectorySeparator(_folder.Root));

    public CaptureStatus Status
    {
        get { lock (_gate) return Snapshot(); }
    }

    public bool AutoCapture
    {
        get { lock (_gate) return _autoCapture; }
        set
        {
            (CaptureStatus, long) status;
            lock (_gate)
            {
                _autoCapture = value;
                status = Next();
            }
            Publish(status, force: true);
        }
    }

    public event EventHandler<CaptureStatus>? StatusChanged;

    /// <summary>One per kept shot, in file order, after its image is on disk.</summary>
    public event EventHandler<KeptShot>? ShotKept;

    public event EventHandler<Exception>? StillFailed;

    /// <summary>Starts AR and learns the still size; auto capture and the shutter wait for it.</summary>
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_finishing) throw new InvalidOperationException("The capture has ended.");
            return _starting ??= StartCoreAsync(cancellationToken);
        }
    }

    private async Task StartCoreAsync(CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _startStop.Token);
        await _ar.StartAsync(linked.Token).ConfigureAwait(false);
        var format = await _stills.GetStillFormatAsync(linked.Token).ConfigureAwait(false);
        (CaptureStatus, long) status;
        lock (_gate)
        {
            if (_finishing) return;
            if (format.FocalLengthMm is { } mm) _device = _device with { FocalLengthMm = mm };
            _auto = new SceneAutoCapture(_settings, format.Width, format.Height);
            status = Next();
        }
        Publish(status, force: true);
    }

    /// <summary>Takes a still now, skipping the motion and spacing checks. False while one is in flight.</summary>
    public bool Shutter()
    {
        (CaptureStatus, long) status;
        lock (_gate)
        {
            if (_auto is null || _inFlight is not null || _finishing) return false;
            _inFlight = Task.Run(() => TakeStillAsync(predictedBlurPx: null));
            status = Next();
        }
        Publish(status, force: true);
        return true;
    }

    /// <summary>
    /// Stops AR, keeps the sharpest of any open run of tries and writes the sidecars. A capture
    /// with no shots leaves no folder behind.
    /// </summary>
    public async Task<CaptureRecord> FinishAsync()
    {
        Task? pending, starting;
        lock (_gate)
        {
            _finishing = true;
            pending = _inFlight;
            starting = _starting;
        }
        await _startStop.CancelAsync().ConfigureAwait(false);
        await Quietly(starting).ConfigureAwait(false);
        if (pending is not null) await pending.ConfigureAwait(false);
        _ar.FrameUpdated -= OnFrame;
        await _ar.StopAsync().ConfigureAwait(false);

        IReadOnlyList<KeptShot> last;
        lock (_gate) last = _auto?.Finish() ?? [];
        Write(last);

        CaptureRecord record;
        lock (_gate)
            record = _auto?.ToRecord(Name, StartedAt, _device)
                     ?? new CaptureRecord(Name, StartedAt, _device, _settings, [], []);
        if (_unwritten.Count > 0)
            record = record with
            {
                Shots = record.Shots.Where(s => !_unwritten.Contains(s.Index)).ToArray(),
                Gaps = record.Gaps.Where(g => !_unwritten.Contains(g.Shot)).ToArray(),
            };
        if (record.Shots.Count > 0) _folder.WriteSidecars(record);
        else if (Directory.Exists(_folder.Root)) Directory.Delete(_folder.Root, recursive: true);
        return record;
    }

    /// <summary>Abandons a capture that was not finished; the AR session is the caller's to stop.</summary>
    public async ValueTask DisposeAsync()
    {
        _ar.FrameUpdated -= OnFrame;
        Task? pending, starting;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = _finishing = true;
            pending = _inFlight;
            starting = _starting;
        }
        await _startStop.CancelAsync().ConfigureAwait(false);
        await _stop.CancelAsync().ConfigureAwait(false);
        await Quietly(starting).ConfigureAwait(false);
        if (pending is not null) await pending.ConfigureAwait(false);
        _stop.Dispose();
        _startStop.Dispose();
    }

    // Whoever called StartAsync sees its failure; finishing only needs it to be over.
    private static async Task Quietly(Task? task)
    {
        if (task is null) return;
        try
        {
            await task.ConfigureAwait(false);
        }
        catch
        {
        }
    }

    private void OnFrame(object? sender, ArFrame frame)
    {
        (CaptureStatus, long) status;
        lock (_gate)
        {
            if (_auto is null || _finishing) return;
            var advice = _auto.OnFrame(frame);
            _hold = advice.Hold;
            _predicted = advice.PredictedBlurPx;
            if (advice.Fire && _autoCapture && _inFlight is null)
                _inFlight = Task.Run(() => TakeStillAsync(advice.PredictedBlurPx));
            status = Next();
        }
        Publish(status, force: false, at: frame.Timestamp);
    }

    private async Task TakeStillAsync(double? predictedBlurPx)
    {
        try
        {
            var still = await _stills.CaptureStillAsync(_stop.Token).ConfigureAwait(false);
            var location = _location?.Invoke();
            StillResult result;
            lock (_gate) result = _auto!.OnStill(still, _time.GetLocalNow(), location, predictedBlurPx);
            Write(result.Kept);
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
        }
        catch (Exception e)
        {
            StillFailed?.Invoke(this, e);
        }
        finally
        {
            (CaptureStatus, long) status;
            lock (_gate)
            {
                _inFlight = null;
                status = Next();
            }
            Publish(status, force: true);
        }
    }

    // Runs only while a still is in flight or after FinishAsync has waited for it, so image
    // writes never overlap and land in file order. A shot whose image could not be written is
    // left out of the sidecars, which must not name a missing file.
    private void Write(IReadOnlyList<KeptShot> kept)
    {
        foreach (var k in kept)
        {
            try
            {
                _folder.WriteImage(k.Shot, k.Jpeg.Span, _device, _settings);
            }
            catch (Exception e)
            {
                _unwritten.Add(k.Shot.Index);
                StillFailed?.Invoke(this, e);
                continue;
            }
            ShotKept?.Invoke(this, k);
        }
    }

    private CaptureStatus Snapshot() => new(
        _auto is not null,
        _hold,
        _predicted,
        _auto?.Shots.Count ?? 0,
        _auto?.Gaps.Count ?? 0,
        _inFlight is not null,
        _autoCapture);

    private (CaptureStatus, long) Next() => (Snapshot(), ++_statusSequence);

    // Statuses are taken under _gate but raised outside it, from two threads; the sequence
    // number drops one that lost the race to a newer one.
    private void Publish((CaptureStatus Status, long Sequence) next, bool force, TimeSpan? at = null)
    {
        lock (_publishGate)
        {
            if (next.Sequence <= _publishedSequence) return;
            var changed = _published is not { } last ||
                          last with { PredictedBlurPx = null } != next.Status with { PredictedBlurPx = null };
            if (!force && !changed && at - _publishedAt < StatusInterval) return;
            _published = next.Status;
            _publishedSequence = next.Sequence;
            if (at is not null) _publishedAt = at;
            StatusChanged?.Invoke(this, next.Status);
        }
    }
}
