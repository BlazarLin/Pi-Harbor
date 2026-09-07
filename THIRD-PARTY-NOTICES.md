# Third-party notices

Pi Harbor source code is distributed under the MIT license in `LICENSE`.

## Bundled runtime

Self-contained Windows downloads include Microsoft .NET and Windows Desktop Runtime files. The build copies their license and available third-party notice files from the exact restored packages into `licenses/` in both the ZIP and installed application.

- [.NET runtime license and notices](https://github.com/dotnet/runtime)
- [WPF license and notices](https://github.com/dotnet/wpf)

## External dependency

[Pi](https://github.com/earendil-works/pi) is an independent coding-agent project, installed and configured by the user. Pi Harbor starts Pi using its RPC interface. Pi, Node.js, model credentials and provider SDKs are not bundled with Pi Harbor.

## Installer tooling

The Windows installer is generated with [Inno Setup](https://jrsoftware.org/isinfo.php). Inno Setup's compiler is not included in the portable application. Its installer engine is subject to the [upstream license](https://github.com/jrsoftware/issrc/blob/main/license.txt).

Pi Harbor is an independent community desktop client and is not an official Pi, Microsoft, or OpenAI product.
