# DaocUiForge.Core

The UI-free core of DAoC UI Forge: loading, inspecting, editing and rendering
Dark Age of Camelot user-interface packages (the XML windows, templates,
textures and fonts of a UI skin). It depends only on the .NET base library and
SkiaSharp, so it runs headless.

- `Ingest` - reads a package from a folder or ZIP
- `Model` - the loaded package
- `Editing` - changes that go straight onto the XML tree, and saving it back
- `Render` - draws a window the way the game does
- `Formats` - DDS, TGA and TTF metrics
- `Inspection` - reports on a package's include chain and problems
- `Reference` - sample values, colours, events and chat buffers
- `Localization` - interface strings (English is the key; `de` ships with it)
- `Settings`, `Diagnostics` - persisted settings and an in-memory log

## Build

Requires the .NET 10 SDK.

```
dotnet build -c Release
dotnet test
```

## Use as a git submodule

```
git submodule add https://github.com/tokajer/daoc-ui-forge-core external/daoc-ui-forge-core
```

```xml
<ProjectReference Include="..\..\external\daoc-ui-forge-core\src\DaocUiForge.Core\DaocUiForge.Core.csproj" />
```

The repository's own `Directory.Build.props` sets the target framework and
language options for Core, and keeps the consuming repository's
`Directory.Build.props` from applying to it.

## Credits

The reference data in `src/DaocUiForge.Core/Reference/*.json` comes from
DAoCEd by Brad Townsend.

## License

GPL-3.0, see [LICENSE](LICENSE).
