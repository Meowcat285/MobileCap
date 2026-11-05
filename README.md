# MobileCap

An iOS (and later Android) app for capturing photos for 3D reconstruction in
[Spirula Studio](https://github.com/Meowcat285/spirula-studio). It tracks the phone with AR,
takes sharp photos automatically, and exports a folder Spirula can open directly.

Status: milestone 1 of 6 (project skeleton). The app launches and shows an empty capture list.

## Projects

| Project | Contents |
| --- | --- |
| `MobileCap.Core` | Capture logic and the `IArSession` / `IFrameSource` interfaces. No platform code. |
| `MobileCap.Core.Tests` | xUnit tests for Core, run on any desktop OS. |
| `MobileCap` | Shared Avalonia UI (MVVM with CommunityToolkit.Mvvm). |
| `MobileCap.iOS` | iOS head. ARKit lands here in milestone 3. Requires iOS 16. |
| `MobileCap.Android` | Android head. Builds in CI; ARCore support is a TODO. Requires API 26. |
| `MobileCap.Desktop` | Desktop head for working on the UI without a phone. |

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
dotnet build MobileCap.iOS -t:Run -f net10.0-ios -r ios-arm64
```

The `Run` target does not build first, so the two steps are separate.

A device build needs a provisioning profile for `io.github.meowcat285.mobilecap`. The simplest
way to get one is to create an empty iOS app in Xcode with that bundle identifier, let Xcode
manage signing for your team, and run it on the phone once. After that the .NET build finds the
profile on its own; set `CodesignKey` and `CodesignProvision` if it picks the wrong one.

Android needs the Android workload (`dotnet workload install android`) and a JDK.

## License

MIT
