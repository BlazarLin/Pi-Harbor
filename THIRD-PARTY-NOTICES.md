# Third-party notices

Pi Harbor source code is distributed under the MIT license in `LICENSE`.

## Bundled runtime

Self-contained Windows downloads include Microsoft .NET and Windows Desktop Runtime files. Self-contained Linux downloads include the Microsoft .NET runtime files. The build copies their license and available third-party notice files from the exact restored packages into `licenses/` in both the ZIP and installed application.

- [.NET runtime license and notices](https://github.com/dotnet/runtime)
- [WPF license and notices](https://github.com/dotnet/wpf)

Linux builds copy the runtime `LICENSE.TXT` and `THIRD-PARTY-NOTICES.TXT` into `licenses/microsoft.netcore.app.runtime.linux-x64/`.

## Linux UI stack

The Avalonia frontend bundles the following MIT-licensed libraries: [Avalonia UI](https://github.com/AvaloniaUI/Avalonia), [SkiaSharp](https://github.com/mono/SkiaSharp), [HarfBuzzSharp](https://github.com/mono/SkiaSharp), [Markdown.Avalonia](https://github.com/whistyun/Markdown.Avalonia) and [AvaloniaEdit](https://github.com/AvaloniaUI/AvaloniaEdit). The bundled [Inter](https://github.com/rsms/inter) font is distributed under the SIL Open Font License 1.1.

## Notification library

Windows toast notifications use [Microsoft.Toolkit.Uwp.Notifications 7.1.3](https://github.com/CommunityToolkit/WindowsCommunityToolkit), licensed under MIT. Its license is included in `licenses/microsoft.toolkit.uwp.notifications/License.md` in release downloads.

## External dependency

[Pi](https://github.com/earendil-works/pi) is an independent coding-agent project, installed and configured by the user. Pi Harbor starts Pi using its RPC interface. Pi, Node.js, model credentials and provider SDKs are not bundled with Pi Harbor.

## Installer tooling

The Windows installer is generated with [Inno Setup](https://jrsoftware.org/isinfo.php). Inno Setup's compiler is not included in the portable application. Its installer engine is subject to the [upstream license](https://github.com/jrsoftware/issrc/blob/main/license.txt).

Pi Harbor is an independent community desktop client and is not an official Pi, Microsoft, or OpenAI product.
