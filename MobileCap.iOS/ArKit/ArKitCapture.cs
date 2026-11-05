using ARKit;
using Avalonia.Controls;
using CoreFoundation;
using CoreImage;
using CoreLocation;
using CoreGraphics;
using CoreVideo;
using Foundation;
using ImageIO;
using MobileCap.Core;
using MobileCap.Core.Ar;
using MobileCap.Core.Capture;
using MobileCap.Core.Scene;
using MobileCap.Platform;
using SceneKit;
using UIKit;

namespace MobileCap.iOS.ArKit;

/// <summary>
/// One capture's ARKit session: world tracking with focus locked, frames raised on a private
/// serial queue, full-resolution stills from <c>captureHighResolutionFrame</c>, and the marker
/// trail drawn into an <see cref="ARSCNView"/>.
/// </summary>
internal sealed class ArKitCapture : IArCapture, IArSession, IFrameSource
{
    private const double JpegQuality = 0.92;

    private readonly ARSession _session = new();
    private readonly ARSCNView _view;
    private readonly SessionDelegate _delegate;
    private readonly CIContext _ci = CIContext.Create();
    private readonly LocationWatcher? _location;
    private readonly TaskCompletionSource _firstFrame = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task<StillFormat>? _format;

    public ArKitCapture(CaptureSettings settings)
    {
        _delegate = new SessionDelegate(this);
        _session.Delegate = _delegate;
        _session.DelegateQueue = new DispatchQueue("io.github.meowcat285.mobilecap.ar");
        _view = new ARSCNView { Session = _session, Scene = new SCNScene(), AutomaticallyUpdatesLighting = true };
        View = new NativeViewHost(_view);
        if (settings.WriteGpsToExif)
        {
            _location = new LocationWatcher();
            Location = () => _location.Latest;
        }
    }

    public event EventHandler<ArFrame>? FrameUpdated;

    public IArSession Session => this;

    public IFrameSource Stills => this;

    public Control View { get; }

    public Func<GeoLocation?>? Location { get; }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        var config = new ARWorldTrackingConfiguration
        {
            WorldAlignment = ARWorldAlignment.Gravity,
            // The manifest gives every image one camera, which holds only while focus does not move.
            AutoFocusEnabled = false,
        };
        if (ARWorldTrackingConfiguration.GetSupportedVideoFormats()
                .FirstOrDefault(f => f.IsRecommendedForHighResolutionFrameCapturing) is { } format)
            config.VideoFormat = format;
        _session.Run(config, ARSessionRunOptions.ResetTracking | ARSessionRunOptions.RemoveExistingAnchors);
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        _session.Pause();
        return Task.CompletedTask;
    }

    public Task<StillFormat> GetStillFormatAsync(CancellationToken cancellationToken = default) =>
        _format ??= ProbeAsync(cancellationToken);

    public Task<CapturedStill> CaptureStillAsync(CancellationToken cancellationToken = default) =>
        CaptureAsync(ToStill, cancellationToken);

    public void ShowShot(CaptureShot shot, GapWarning? gap) =>
        _view.Scene.RootNode.AddChildNode(Markers.ForShot(shot, gap is not null));

    public ValueTask DisposeAsync()
    {
        _session.Pause();
        _session.Delegate = null!;
        _location?.Dispose();
        _delegate.Dispose();
        _ci.Dispose();
        return ValueTask.CompletedTask;
    }

    // ARKit has no way to ask the still size up front, so take one still and look.
    private async Task<StillFormat> ProbeAsync(CancellationToken cancellationToken)
    {
        await _firstFrame.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        return await CaptureAsync(frame =>
        {
            using var image = frame.CapturedImage;
            return new StillFormat((int)image.Width, (int)image.Height, FocalLengthMm(frame));
        }, cancellationToken).ConfigureAwait(false);
    }

    private Task<T> CaptureAsync<T>(Func<ARFrame, T> read, CancellationToken cancellationToken)
    {
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var registration = cancellationToken.Register(() => done.TrySetCanceled(cancellationToken));
        _session.CaptureHighResolutionFrame((frame, error) =>
        {
            if (frame is null)
            {
                registration.Dispose();
                done.TrySetException(new InvalidOperationException(error?.LocalizedDescription ?? "no frame"));
                return;
            }
            // Off ARKit's callback queue, which may be the main one: reading 12 MP there stalls the UI.
            Task.Run(() =>
            {
                try
                {
                    using (frame) done.TrySetResult(read(frame));
                }
                catch (Exception e)
                {
                    done.TrySetException(e);
                }
                finally
                {
                    registration.Dispose();
                }
            });
        });
        return done.Task;
    }

    private CapturedStill ToStill(ARFrame frame)
    {
        var meta = ArKitConvert.ToFrame(frame);
        using var image = frame.CapturedImage;
        var (width, height) = ((int)image.Width, (int)image.Height);
        return new CapturedStill(meta, width, height, Luma(image, width, height), Jpeg(image));
    }

    /// <summary>Plane 0 of ARKit's bi-planar YCbCr image is the grey the sharpness check reads.</summary>
    private static byte[] Luma(CVPixelBuffer image, int width, int height)
    {
        var grey = new byte[width * height];
        image.Lock(CVPixelBufferLock.ReadOnly);
        try
        {
            var stride = (int)image.GetBytesPerRowOfPlane(0);
            var source = image.GetBaseAddress(0);
            for (int y = 0; y < height; y++)
                System.Runtime.InteropServices.Marshal.Copy(source + y * stride, grey, y * width, width);
        }
        finally
        {
            image.Unlock(CVPixelBufferLock.ReadOnly);
        }
        return grey;
    }

    /// <summary>Kept in sensor orientation; the EXIF writer adds the Orientation tag.</summary>
    private byte[] Jpeg(CVPixelBuffer image)
    {
        using var ci = CIImage.FromImageBuffer(image);
        using var srgb = CGColorSpace.CreateSrgb() ?? throw new InvalidOperationException("no sRGB colour space");
        using var options = NSDictionary.FromObjectAndKey(
            NSNumber.FromDouble(JpegQuality), CGImageDestinationOptionsKeys.LossyCompressionQuality);
        using var data = _ci.GetJpegRepresentation(ci, srgb, options)
                         ?? throw new InvalidOperationException("JPEG encoding failed");
        return data.ToArray();
    }

    private static double? FocalLengthMm(ARFrame frame) =>
        frame.ExifData?[ImageIO.CGImageProperties.ExifFocalLength] is NSNumber mm ? mm.DoubleValue : null;

    private void OnFrame(ARFrame frame)
    {
        ArFrame converted;
        using (frame) converted = ArKitConvert.ToFrame(frame);
        _firstFrame.TrySetResult();
        FrameUpdated?.Invoke(this, converted);
    }

    private sealed class SessionDelegate(ArKitCapture owner) : ARSessionDelegate
    {
        // ARKit stalls once a few frames are retained, so each one is disposed in OnFrame.
        public override void DidUpdateFrame(ARSession session, ARFrame frame) => owner.OnFrame(frame);

        // A denied camera permission ends up here with no frame ever arriving.
        public override void DidFail(ARSession session, NSError error) =>
            owner._firstFrame.TrySetException(new InvalidOperationException(error.LocalizedDescription));

        // Keeps the world, and so the marker trail and the poses, continuous after an interruption.
        public override bool ShouldAttemptRelocalization(ARSession session) => true;
    }

    private sealed class NativeViewHost(UIView view) : NativeControlHost
    {
        protected override Avalonia.Platform.IPlatformHandle CreateNativeControlCore(
            Avalonia.Platform.IPlatformHandle parent) => new Avalonia.iOS.UIViewControlHandle(view);

        // The view lives as long as the capture, not the attachment.
        protected override void DestroyNativeControlCore(Avalonia.Platform.IPlatformHandle control) =>
            view.RemoveFromSuperview();
    }

    private sealed class LocationWatcher : CLLocationManagerDelegate
    {
        private readonly CLLocationManager _manager = new();
        private volatile GeoLocation? _latest;

        public LocationWatcher()
        {
            _manager.Delegate = this;
            _manager.DesiredAccuracy = CLLocation.AccuracyBest;
            _manager.RequestWhenInUseAuthorization();
            _manager.StartUpdatingLocation();
        }

        public GeoLocation? Latest => _latest;

        public override void LocationsUpdated(CLLocationManager manager, CLLocation[] locations)
        {
            if (locations.LastOrDefault() is not { HorizontalAccuracy: >= 0 } fix) return;
            _latest = new GeoLocation(fix.Coordinate.Latitude, fix.Coordinate.Longitude,
                fix.VerticalAccuracy >= 0 ? fix.Altitude : null, fix.HorizontalAccuracy);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _manager.StopUpdatingLocation();
                _manager.Delegate = null!;
                _manager.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
