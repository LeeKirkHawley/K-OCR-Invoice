Summary
-------
This PR tightens dependency injection across the desktop app so the UI no longer uses design-time fallbacks or constructs core/shared services directly.

What I changed
--------------
- Removed parameterless/designer constructors and replaced design-time fallbacks with DI-only constructors for:
  - `MainWindow` (now requires all core services via ctor)
  - `BatchProcessDialog` (now requires `IImageService` and `IFileService` from DI)
  - `SettingsDialog` (now requires `IConfigurationService`)
- Registered `IImageService` in `K-OCRDesktop/App.axaml.cs` and resolved required services with `GetRequiredService<T>()`.
- Removed runtime `new ImageService()` / `new FileService()` / `new ConfigurationService()` fallbacks in desktop UI code.
- Updated call sites (MainWindow → BatchProcessDialog, MainWindow → SettingsDialog) to pass DI-resolved services.
- Left safe design-time behavior out intentionally (Avalonia designer no longer instantiates those views without DI).

Files changed (high level)
-------------------------
- K-OCRDesktop/Views/MainWindow.axaml.cs
- K-OCRDesktop/Views/BatchProcessDialog.axaml.cs
- K-OCRDesktop/Views/SettingsDialog.axaml.cs
- K-OCRDesktop/App.axaml.cs

Why
---
- Enforces a clear separation between UI and core logic.
- Prepares `K-OCRLib` to be reused by non-UI hosts (web front end) without the desktop providing fallbacks.
- Reduces implicit coupling and makes behavior deterministic (no hidden `new` instances).

Notes / Warnings
----------------
- Avalonia designer warnings will appear because some Views no longer have parameterless constructors. This is expected after tightening DI.
- Build passes locally. Unit tests are a recommended next step.

Suggested follow-ups
--------------------
- Replace other small UI helper "new" instances with DI where it improves testability (optional).
- Add integration tests to verify DI wiring (MainWindow, BatchProcessDialog creation via a ServiceProvider).
- Update developer docs to show how to run the desktop with DI (App.ConfigureServices).

Ready actions
-------------
- I can open a PR with these changes and include this note as the PR description.
- I can also add DI-based unit tests next (recommended).

Would you like me to create the PR now?