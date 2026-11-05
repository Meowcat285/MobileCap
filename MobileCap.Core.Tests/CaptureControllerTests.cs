using System.Collections.Concurrent;
using System.Numerics;
using MobileCap.Core.Ar;
using MobileCap.Core.Capture;
using MobileCap.Core.Export;

namespace MobileCap.Core.Tests;

public sealed class CaptureControllerTests : IAsyncDisposable
{
    private const int W = 320, H = 240;
    private static readonly DateTimeOffset Started = new(2026, 11, 5, 14, 32, 0, TimeSpan.Zero);

    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "mobilecap-test-" + Guid.NewGuid().ToString("N"), "capture_2026-11-05_1432");
    private readonly FakeAr _ar = new();
    private readonly FakeStills _stills;
    private readonly CaptureController _controller;
    private double _clock;

    public CaptureControllerTests()
    {
        _stills = new FakeStills(_ar);
        _controller = new CaptureController(_ar, _stills, new CaptureFolder(_root), new CaptureSettings(),
            new DeviceInfo("Apple", "iPhone 15 Pro", "MobileCap"), Started);
    }

    public async ValueTask DisposeAsync()
    {
        await _controller.DisposeAsync();
        var top = Path.GetDirectoryName(_root)!;
        if (Directory.Exists(top)) Directory.Delete(top, recursive: true);
    }

    private void HoldStill(Vector3 at)
    {
        for (int i = 0; i < 8; i++)
        {
            _clock += 1 / 60.0;
            _ar.Raise(TestImages.Frame(_clock, TestImages.Pose(at)));
        }
    }

    [Fact]
    public async Task Frames_before_the_still_format_is_known_are_ignored()
    {
        _ar.Raise(TestImages.Frame(0, TestImages.Pose(Vector3.Zero)));
        Assert.False(_controller.Status.Ready);
        Assert.False(_controller.Shutter());

        await _controller.StartAsync(TestContext.Current.CancellationToken);

        Assert.True(_controller.Status.Ready);
        Assert.Equal(0, _stills.Taken);
    }

    [Fact]
    public async Task Auto_capture_takes_one_still_at_a_time_and_writes_it()
    {
        await _controller.StartAsync(TestContext.Current.CancellationToken);
        var kept = new List<KeptShot>();
        _controller.ShotKept += (_, k) => kept.Add(k);
        var hold = _stills.Hold();

        HoldStill(Vector3.Zero);
        HoldStill(new Vector3(1, 0, 0));

        Assert.True(_controller.Status.Busy);
        Assert.False(_controller.Shutter());
        hold.SetResult();
        await _stills.Idle(_controller);

        Assert.Equal(1, _stills.Taken);
        var shot = Assert.Single(kept).Shot;
        Assert.Equal(0, shot.PredictedBlurPx!.Value, 3);
        Assert.True(File.Exists(Path.Combine(_root, "images", "000001.jpg")));
    }

    [Fact]
    public async Task Turning_auto_off_leaves_the_shutter()
    {
        await _controller.StartAsync(TestContext.Current.CancellationToken);
        _controller.AutoCapture = false;

        HoldStill(Vector3.Zero);
        Assert.Equal(0, _stills.Taken);

        Assert.True(_controller.Shutter());
        await _stills.Idle(_controller);
        var record = await _controller.FinishAsync();

        var shot = Assert.Single(record.Shots);
        Assert.Null(shot.PredictedBlurPx);
        Assert.True(File.Exists(Path.Combine(_root, SpirulaManifest.FileName)));
        Assert.Equal("capture_2026-11-05_1432", record.Name);
        Assert.Equal(26, record.Device.FocalLengthMm);
    }

    [Fact]
    public async Task Finish_without_shots_leaves_no_folder()
    {
        await _controller.StartAsync(TestContext.Current.CancellationToken);
        Directory.CreateDirectory(_root);

        var record = await _controller.FinishAsync();

        Assert.Empty(record.Shots);
        Assert.False(Directory.Exists(_root));
        Assert.True(_ar.Stopped);
    }

    [Fact]
    public async Task Frames_and_stills_from_many_threads_keep_the_trail_consistent()
    {
        await _controller.StartAsync(TestContext.Current.CancellationToken);
        var kept = new ConcurrentBag<KeptShot>();
        _controller.ShotKept += (_, k) => kept.Add(k);

        // Each thread walks its own line, so every frame is far from the others' shots; the
        // clock is shared, as the AR clock is.
        var clock = 0L;
        var threads = Enumerable.Range(0, 4).Select(t => Task.Run(() =>
        {
            for (int i = 0; i < 400; i++)
            {
                var seconds = Interlocked.Increment(ref clock) / 600.0;
                var at = new Vector3(i / 40 * 0.5f, t * 3, 0);
                _ar.Raise(TestImages.Frame(seconds, TestImages.Pose(at)));
                if (i % 50 == 0) _controller.Shutter();
            }
        })).ToArray();
        await Task.WhenAll(threads);
        var record = await _controller.FinishAsync();

        Assert.NotEmpty(record.Shots);
        Assert.Equal(record.Shots.Count, kept.Count);
        Assert.Equal(Enumerable.Range(0, record.Shots.Count), record.Shots.Select(s => s.Index));
        foreach (var s in record.Shots) Assert.True(File.Exists(Path.Combine(_root, "images", s.FileName)));
    }

    [Fact]
    public async Task A_failed_still_is_reported_and_frees_the_shutter()
    {
        await _controller.StartAsync(TestContext.Current.CancellationToken);
        Exception? failure = null;
        _controller.StillFailed += (_, e) => failure = e;
        _stills.FailNext = true;

        Assert.True(_controller.Shutter());
        await _stills.Idle(_controller);

        Assert.IsType<InvalidOperationException>(failure);
        Assert.True(_controller.Shutter());
    }

    private sealed class FakeAr : IArSession
    {
        public event EventHandler<ArFrame>? FrameUpdated;

        public event EventHandler<Exception>? Failed { add { } remove { } }

        public ArFrame? Latest { get; private set; }

        public bool Stopped { get; private set; }

        public void Raise(ArFrame frame)
        {
            Latest = frame;
            FrameUpdated?.Invoke(this, frame);
        }

        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task StopAsync()
        {
            Stopped = true;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    /// <summary>Each still shows a different random scene, so none is a duplicate.</summary>
    private sealed class FakeStills(FakeAr ar) : IFrameSource
    {
        private int _taken;
        private TaskCompletionSource? _hold;

        public int Taken => _taken;

        public bool FailNext { get; set; }

        public TaskCompletionSource Hold() => _hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task Idle(CaptureController controller)
        {
            while (controller.Status.Busy) await Task.Delay(5);
        }

        public Task<StillFormat> GetStillFormatAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new StillFormat(W, H, FocalLengthMm: 26));

        public async Task<CapturedStill> CaptureStillAsync(CancellationToken cancellationToken = default)
        {
            if (_hold is { } hold) await hold.Task;
            if (FailNext)
            {
                FailNext = false;
                throw new InvalidOperationException("camera busy");
            }
            var n = Interlocked.Increment(ref _taken);
            var frame = ar.Latest ?? TestImages.Frame(0, Matrix4x4.Identity);
            return new CapturedStill(frame, W, H, TestImages.Blocks(W, H, 40, seed: n), TestImages.Jpeg());
        }
    }
}
