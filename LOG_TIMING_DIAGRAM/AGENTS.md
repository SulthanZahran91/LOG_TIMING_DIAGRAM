# Repository Guidelines

## Project Structure & Module Organization
- `App.xaml` and `MainWindow.xaml` define the WPF shell; view logic sits in `ViewModels/` with one class per screen.
- Domain entities describing log timing (`SignalData`, `ParseResult`, etc.) live in `Models/`; keep them serialization-friendly.
- Parser implementations (`PLCTabParser`, `PLCDebugParser`, etc.) belong in `Parsers/`, with registrations wired through `ParserRegistry`.
- Rendering primitives and timeline layout helpers live in `Rendering/`; update these when adjusting the canvas experience.
- Shared helpers such as math utilities and file abstractions are in `Utils/`.
- `python reference/` hosts the legacy PLC visualizer; treat it as an executable spec when aligning behavior.

## Build, Test, and Development Commands
- `dotnet restore` resolves NuGet dependencies for the WPF project.
- `dotnet build` compiles to `bin/<Configuration>/net8.0-windows/`; check warnings before continuing.
- `dotnet run --project LOG_TIMING_DIAGRAM.csproj` launches the desktop app for manual verification.
- `dotnet publish -c Release` produces a redistributable package for testers.
- `pytest python reference/plc_visualizer/tests -q` exercises the Python reference to confirm parsing parity.

## Coding Style & Naming Conventions
- Use 4-space indentation, `PascalCase` for types, `camelCase` for locals, and descriptive suffixes (`ViewModel`, `Parser`, `Renderer`).
- Keep nullable reference types enabled; prefer explicit null handling and guard clauses.
- Expose UI bindings via `INotifyPropertyChanged` properties, and favor `ObservableCollection<T>` for live lists.
- Run `dotnet format` or Visual Studio analyzers before committing to keep C# 12 features consistent.

## Testing Guidelines
- Add future .NET tests under a `Tests/` project using xUnit; name files `<Target>Tests.cs` to mirror subjects.
- Cover parser scenarios with real-world sample logs; document new fixtures in PR descriptions.
- Record manual verification steps (e.g., timeline renders, zoom interactions) alongside automated results.

## Commit & Pull Request Guidelines
- Write imperative, 50-character summaries (`Parser: support PLC debug timestamps`) with optional wrapped bodies.
- Reference affected areas (`Models`, `Rendering`) in the subject when clarity helps reviewers.
- PRs must list validation (`dotnet build`, `pytest …`), screenshots for UI changes, and linked issues or tasks.
- Highlight migration notes if schema files or default configurations change.

## Security & Configuration Tips
- Strip sensitive PLC identifiers from sample logs before committing; store sanitized copies only.
- Keep environment-specific config out of source control by using local `*.user` overrides.

## Reference
- in Python Reference folder, there is a PyQt project that is more mature. If deemed necessary, do not hesitate to use it as reference.
