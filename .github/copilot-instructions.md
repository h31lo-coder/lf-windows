# AI Coding Instructions for lf-windows

## Project Context
**lf-windows** is a Windows-native file manager inspired by `lf` (Linux), built with **Avalonia UI** and **.NET 9**. It focuses on keyboard-centric navigation (Vim-style) and high performance.

## Architecture Overview
- **Framework**: Avalonia UI (Windows Desktop), MVVM pattern.
- **Core Logic**: `MainWindowViewModel` acts as the central hub, coordinating `FileListViewModel` (Miller Columns), `PreviewEngine`, and services.
- **Process Model**:
  - Main Process: UI and business logic.
  - `lf-watcher`: Separate background process for file system monitoring to prevent UI freezes.
- **Dependency Injection**: `Microsoft.Extensions.DependencyInjection` configured in `App.axaml.cs`.

## Key Components & Patterns
- **Navigation**: Miller Columns implementation using three dynamic `FileListViewModel` instances (Parent, Current, Preview).
- **Input System**: `KeyBindingService` implements a state machine for Vim-like modes (Normal, Visual, Command, Filter, Jump).
  - *Pattern*: Key mappings are configurable via `keybindings.yaml`.
- **Preview System**: Strategy pattern in `PreviewEngine` dispatching to specific controls based on file type (e.g., `AvaloniaEdit` for text, `LibVLCSharp` for video).
- **Configuration**: YAML-based config (`ConfigService`) using `YamlDotNet`.
- **MVVM**: Heavy usage of `CommunityToolkit.Mvvm`.
  - Use `[ObservableProperty]` for properties.
  - Use `[RelayCommand]` for commands.

## Development Workflow
- **Build**: `dotnet build`
- **Run**: `dotnet run --project lf-windows`
- **Release**:
  - Portable: `zip_release.py` (Python script).
  - Installer: `setup.iss` (Inno Setup).
- **Dependencies**:
  - `lf-watcher` must be built and placed alongside `lf-windows.exe` for file monitoring to work.

## Coding Conventions
- **Async/Await**: Use async I/O for all file operations to keep UI responsive.
- **Nullable**: Enable nullable reference types (`<Nullable>enable</Nullable>`).
- **Interop**: Windows API calls are isolated in `Interop/` namespace.
- **UI Thread**: Ensure UI updates happen on the UI thread (Avalonia dispatcher).

## Critical Files
- `lf-windows/App.axaml.cs`: DI setup, startup logic, `lf-watcher` lifecycle.
- `lf-windows/ViewModels/MainWindowViewModel.cs`: Main application state and command routing.
- `lf-windows/Services/KeyBindingService.cs`: Input handling logic.
- `lf-windows/Services/PreviewEngine.cs`: File preview logic.
