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

iOS needs a Mac with Xcode and the iOS workload (`dotnet workload install ios`). To run on an
iPhone connected to the Mac:

```sh
dotnet build MobileCap.iOS -t:Run -f net10.0-ios -p:RuntimeIdentifier=ios-arm64
```

Signing uses your Apple developer account's automatic provisioning; set
`CodesignKey` and `CodesignProvision` if Xcode can't pick them.

Android needs the Android workload (`dotnet workload install android`) and a JDK.

## License

MIT
