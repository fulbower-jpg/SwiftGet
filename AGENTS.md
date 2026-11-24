# Instructions for AI coding agents

Read this file first, then `docs/SPEC.md` (how everything works) and `docs/ROADMAP.md` (what to build next).

## What this project is
SwiftGet is a free download manager for Windows (an IDM-style app): many connections per file, pause/resume,
queue, speed limit, tray icon, plus a Chrome/Edge/Brave extension that hands browser downloads to the app.
C# / WinForms / .NET Framework 4.8. Single exe. MIT licensed.

## Commands
```
dotnet build SwiftGet.csproj -c Release -o dist        # build  ->  dist\SwiftGet.exe
dotnet run --project tests/EngineSmoke -c Release       # engine tests, must end with "ALL ... CHECKS PASSED"
```
Needs only the free .NET SDK 8 on Windows. After every change: build, then run the tests.

## Rules that must not be broken (and why)
1. **Target stays `net48`, no NuGet packages.** .NET Framework 4.8 ships with Windows 10/11, so the exe is ~200 KB
   and runs anywhere with no installer. Use only what is already in the framework (`HttpClient`,
   `JavaScriptSerializer`, WinForms, named pipes).
2. **Keep the code in plain C# 7 style** (no records, no `is not`, no file-scoped namespaces, no nullable
   annotations, no `using var`). The code is also compile-checked on Linux with Mono's old `mcs` compiler.
3. **Engine and UI stay separate.** Nothing in `Engine/` may reference `System.Windows.Forms`. The UI only reads
   engine state and calls `DownloadManager` methods.
4. **UI is written in code, no Designer files** (`.Designer.cs`/`.resx`), so any agent can read and edit it.
5. **Thread safety:** segment fields are changed only inside `lock (segment)`; the segment list only inside
   `lock (item.SegLock)`. Never `await` while holding a lock. See "Engine invariants" in `docs/SPEC.md`.
6. **Never remove the `key` from `extension/manifest.json`.** It fixes the extension ID, and the same ID is
   hard-coded in `BrowserIntegration.ExtensionId`. Change one, change both.
7. **No IDM branding:** no IDM name, logo, icons or copied code. All icons are drawn from Windows' own icon font.
8. **Do not add dependencies, telemetry, ads or network calls** other than to the URLs the user downloads
   (an optional update check against the GitHub releases page is allowed, see roadmap).
9. **Everything must stay free** (tools, libraries, hosting).

## Where things are
| Path | Purpose |
|---|---|
| `Engine/DownloadJob.cs` | One download: probe, segments, workers, retry, dynamic split, finish |
| `Engine/DownloadManager.cs` | Queue, concurrency, speed, saving/loading `downloads.json` and `settings.json` |
| `Engine/Models.cs` | `DownloadItem`, `Segment`, `Settings`, `Categories` |
| `UI/MainForm.cs` | Main window, toolbar, category tree, list, tray, clipboard watcher, pipe server |
| `UI/*.cs` | Dialogs: add URL, progress (connection map), options, completion popup, extension setup |
| `NativeHost.cs`, `Ipc.cs`, `BrowserIntegration.cs` | Browser link: native messaging host, pipe to running app, registry setup |
| `extension/` | Manifest V3 extension (embedded into the exe at build time) |
| `tests/EngineSmoke/` | Self-contained engine tests with their own fake web server |

## How to work
- Make small changes, one feature at a time. Build and test after each one.
- Add a test to `tests/EngineSmoke` for every engine change or bug fix.
- UI changes cannot be unit-tested: run the app and walk through the checklist in `docs/TESTING.md`.
- Explain what you did in plain, beginner-friendly language and give copy-paste commands. The person you are
  helping may not be an experienced programmer.
- Never claim something works unless you built it and ran the tests. If you could not run something
  (for example the real Chrome), say so clearly.

## Definition of done
Build succeeds with no new warnings, `EngineSmoke` passes, the manual checklist items touched by your change
pass, and `docs/SPEC.md` / `docs/ROADMAP.md` / README are updated if behaviour changed.
