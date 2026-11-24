# Roadmap

Work top to bottom. Each item is small enough for one agent session. "Done when" is how you know it works.
Always add an engine test when the engine changes, and update `docs/SPEC.md`.

## 1. First things (verify what exists)
- [ ] **Run the manual checklist** in `docs/TESTING.md` on real Windows and fix anything that fails (icons, DPI, tray, extension).
  Done when every line is ticked.
- [ ] **Sort list columns** by clicking the header; remember column widths and window size in `settings.json`.
  Done when the order and sizes survive a restart.

## 2. Features people expect from IDM-style tools
- [ ] **Scheduler**: "start queue at HH:mm", optional "stop at", optional "shut down / sleep when finished". New menu item and dialog.
  Done when a queue starts by itself at the chosen time (test with a time 2 minutes ahead).
- [ ] **Batch links with patterns**: `https://site/file[01-20].zip` expands to 20 downloads in the Add dialog.
- [ ] **Proxy settings** (none / system / manual host:port, optional user and password) in Options. `HttpClientHandler.Proxy`.
- [ ] **Site logins**: optional user name and password per download (HTTP Basic), kept out of logs.
- [ ] **Per-download settings**: connections and speed limit for one item (right-click > Properties).
- [ ] **Safer resume**: store `ETag` / `Last-Modified` from the probe and send `If-Range`; if the file changed, restart the download.
- [ ] **Dark mode** (follow Windows setting).
- [ ] **Urdu / right-to-left UI** (strings in one place, then a language option).

## 3. Video links (optional, must stay free)
- [ ] **yt-dlp integration**: download `yt-dlp.exe` from its GitHub releases into `%APPDATA%\SwiftGet\tools`, add "Grab video" to the Add dialog
  and the browser popup. Only for content the user is allowed to download. Show the yt-dlp license note in About.

## 4. Shipping
- [ ] **Installer** with Inno Setup (free): Start-menu shortcut, optional startup, uninstall, no admin needed. Attach it to the GitHub Release
  (extend `.github/workflows/release.yml`).
- [ ] **Update check**: once a day ask `https://api.github.com/repos/<owner>/<repo>/releases/latest`; show a tray balloon if a newer tag exists. Opt-out in Options.
- [ ] **Code signing** if a free option appears (for example SignPath Foundation for open-source). Removes the SmartScreen warning.
- [ ] **Store versions of the extension**: Edge Add-ons is free; Chrome Web Store has a one-time $5 fee. The store gives the extension a new ID:
  add it to `allowed_origins` in `BrowserIntegration.cs` (keep the old one too) and remove the `key` line from the store copy.
- [ ] **Firefox**: native messaging manifest uses `allowed_extensions` and the registry path `HKCU\Software\Mozilla\NativeMessagingHosts\`; the
  extension needs a Firefox-flavoured manifest (background scripts instead of a service worker).

## 5. Maybe later
FTP links, torrents (no), 64-bit-only build, command-line options (`SwiftGet.exe <url> --silent`), integration with Windows "Send to".
