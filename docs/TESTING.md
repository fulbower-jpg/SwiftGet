# Testing

## Automatic engine tests
```
dotnet run --project tests/EngineSmoke -c Release
```
Starts its own fake web server on `127.0.0.1`, downloads files and checks every byte. It covers: Range downloads with 8 connections,
small files, servers without Range, unknown size, `Content-Disposition` names, empty files, 404, connections that drop halfway (retry),
duplicate names, and pause -> close app -> reopen -> resume. It must end with `ALL ... CHECKS PASSED` (exit code 0).
GitHub Actions runs the same thing on every push.

If `dotnet run` complains, build then run the exe: `dotnet build tests/EngineSmoke -c Release` and start
`tests\EngineSmoke\bin\Release\net48\EngineSmoke.exe`.

## Compile check without Windows (Linux/macOS, optional)
Install Mono (`mono-mcs`, `libmono-system-windows-forms4.0-cil`, `libmono-system-web-extensions4.0-cil`, `libmono-system-net-http4.0-cil`):
```
mcs -target:winexe -out:SwiftGet.exe -win32icon:app.ico \
  -r:System.Windows.Forms -r:System.Drawing -r:System.Net.Http -r:System.Web.Extensions -r:System.Core \
  Program.cs Ipc.cs NativeHost.cs BrowserIntegration.cs Engine/*.cs UI/*.cs
mcs -out:smoke.exe -r:System.Net.Http -r:System.Web.Extensions -r:System.Core tests/EngineSmoke/*.cs Engine/*.cs && mono smoke.exe
```
This only proves it compiles and the engine works; it cannot show how the UI looks on Windows.

## Manual checklist (real Windows 10/11)
Tick each line. Items marked * have never been checked on real Windows yet.

**Install and start**
- [ ] `build.bat` makes `dist\SwiftGet.exe`; it starts without errors (SmartScreen "Run anyway" is expected).
- [ ] * Toolbar icons show as real pictures (not boxes) on Windows 10 and 11.
- [ ] * Window looks sharp at 100 %, 125 %, 150 % display scaling.

**Downloading**
- [ ] Add URL (Ctrl+N), paste a large file link, Enter: progress window opens, speed and connection map move.
- [ ] Pause, Resume: continues from the same percentage. Close the app while downloading, reopen, Resume: continues.
- [ ] Options > speed limit 500 KB/s: transfer rate stays near that.
- [ ] Add 3 links with "Download Later", then Start Queue: at most "Downloads at the same time" run at once.
- [ ] Completion popup appears; Open and Open folder work; file is in the right category folder.
- [ ] Delete asks about deleting the file; Redownload starts from zero.
- [ ] A dead link shows Error, with the reason in the tooltip.

**Tray and background**
- [ ] * Close window with the X: app stays in tray; tray Open and Exit work.
- [ ] * "Start when Windows starts" works and starts hidden in the tray.
- [ ] * Copy a link to a .zip: tray balloon appears; clicking it opens the Add dialog.

**Browser extension (Chrome, Edge, Brave)**
- [ ] * Tasks > Browser extension setup > Prepare extension opens a folder; "Load unpacked" accepts it; the extension ID is
      `omhegfoodfdkjlejdkibljilhcnmdlgb`.
- [ ] * Popup shows "Connected to SwiftGet" (or "Ready" when the app is closed).
- [ ] * Click a download link in the browser: the Add dialog opens with the link; the browser's own download disappears.
- [ ] * Same with the app fully closed: the app starts by itself and shows the dialog.
- [ ] * A download that needs a login (cookies) works.
- [ ] * Right-click a link > Download with SwiftGet.
- [ ] * Rename/delete the exe, click a download: the browser keeps the download and the badge shows `!`.
