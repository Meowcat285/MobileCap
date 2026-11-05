# MobileCap

An iOS (and later Android) app for capturing photos for 3D reconstruction in
[Spirula Studio](https://github.com/Meowcat285/spirula-studio). It tracks the phone with AR,
takes sharp photos automatically, and exports a folder Spirula can open directly.

Status: milestone 2 of 6 (core logic). The app launches and shows an empty capture list; the
capture rules and exporters below are built and tested on desktop, and reach the phone in
milestone 3.

## Projects

| Project | Contents |
| --- | --- |
| `MobileCap.Core` | Capture logic and the `IArSession` / `IFrameSource` interfaces. No platform code. |
| `MobileCap.Core.Tests` | xUnit tests for Core, run on any desktop OS. |
| `MobileCap` | Shared Avalonia UI (MVVM with CommunityToolkit.Mvvm). |
| `MobileCap.iOS` | iOS head. ARKit lands here in milestone 3. Requires iOS 16. |
| `MobileCap.Android` | Android head. Builds in CI; ARCore support is a TODO. Requires API 26. |
| `MobileCap.Desktop` | Desktop head for working on the UI without a phone. |

## How a capture works

`MobileCap.Core` decides when to shoot and what to keep (`Capture/SceneAutoCapture.cs`):

1. On every AR frame, fire only while tracking is normal, the predicted motion blur
   (focal x angular speed x exposure, plus translation over an assumed 1.5 m depth) is under
   0.7 px, and the phone is 0.3 m or 15 degrees from every kept shot.
2. Score the still with the same edge-width measure and 64-bit difference hash as Spirula
   (`app/FrameSharpness.h`, `app/FramePick.h`), against the capture's own sharpest shots.
3. Keep it if sharp; retry if blurry, keeping the sharpest of three and flagging it; drop it if
   it duplicates the last kept shot. A shot sharing under 60% of its view with every earlier one
   raises a gap warning. The app never suggests where to stand.

Each capture is a folder, shared as a .zip:

```
capture_2026-11-05_1432/
  images/000001.jpg      sensor orientation, EXIF Orientation, Make/Model, 35 mm focal; GPS only if enabled
  spirula_manifest.yaml  pinhole intrinsics from AR, one sequence in capture order
  metric_positions.txt   image_name X Y Z, AR camera centres in metres
  transforms.json        Nerfstudio layout with AR poses
  capture.json           MobileCap's record: settings, blur scores, tries, gap warnings
```

Spirula's GUI opens the folder's `images/` as photos. For the AR intrinsics and metric scale, run
from the capture folder:

```sh
spirula sfm auto images -o ws --manifest spirula_manifest.yaml --metric-positions metric_positions.txt
```

The thresholds are first guesses until milestone 5 tunes them on real captures.

## Building

Requires the .NET 10 SDK.

```sh
dotnet test --project MobileCap.Core.Tests
dotnet run --project MobileCap.Desktop
```

iOS needs a Mac with Xcode and the iOS workload (`dotnet workload install ios`). Each workload
version requires one Xcode version; if the build says so, install that Xcode or pin an older
workload (CI uses `dotnet workload install ios --version 10.0.401` with Xcode 26.6). To run on an
iPhone connected to the Mac:

```sh
dotnet build MobileCap.iOS -f net10.0-ios -r ios-arm64
xcrun xctrace list devices   # the phone's UDID is in brackets after its name
dotnet build MobileCap.iOS -t:Run -f net10.0-ios -r ios-arm64 -p:_DeviceName=<UDID>
```

The `Run` target does not build first, so the build is a separate step.

A device build needs a provisioning profile for `io.github.meowcat285.mobilecap`. The simplest
way to get one is to create an empty iOS app in Xcode with that bundle identifier, let Xcode
manage signing for your team, and run it on the phone once. After that the .NET build finds the
profile on its own; set `CodesignKey` and `CodesignProvision` if it picks the wrong one.

Android needs the Android workload (`dotnet workload install android`) and a JDK.

## License

MIT
