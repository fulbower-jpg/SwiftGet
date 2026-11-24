# SwiftGet specification

This is the single source of truth for how SwiftGet behaves. If you rebuild the project from scratch, follow this.

## 1. Goals
- A free, offline-capable download manager for Windows 10/11 that feels like a classic IDM-style app.
- Faster downloads through several connections per file; downloads survive pauses, crashes and restarts.
- One small exe, no installer needed, no runtime to install (.NET Framework 4.8 is part of Windows).
- Browser extension (Chrome / Edge / Brave) that sends browser downloads to the app.

Non-goals (for now): FTP, torrents, video-site grabbing, Mac/Linux.

## 2. Technology
C#, WinForms, .NET Framework 4.8 (`net48`), SDK-style project, no NuGet packages.
JSON via `System.Web.Script.Serialization.JavaScriptSerializer`. HTTP via `System.Net.Http.HttpClient`.
Code style: C# 7 features at most (so the old Mono `mcs` compiler can check it).

## 3. Data on disk (`%APPDATA%\SwiftGet\`)
| File | Content |
|---|---|
| `downloads.json` | List of `DownloadItem` (see below), saved on every state change and every ~3 s while downloading. Written to `.tmp` then swapped in. |
| `settings.json` | `Settings` object |
| `native\com.swiftget.host.json` | Native-messaging host manifest (rewritten at every app start) |
| `extension\` | Unpacked browser extension (created by "Browser extension setup") |

Downloads are written to `<save folder>\<Category>\<name>.part` and renamed to `<name>` when complete.

### DownloadItem (persisted fields)
`Id, Url, Referer, Cookie, UserAgent, FileName, Folder` (base save folder chosen when added), `SavePath` (final path, set after the probe),
`TotalSize` (-1 = unknown), `SupportsRange, Probed, Status, Error, InQueue, AddedTicks, LastTryTicks, Segments[]`.
Status values: `Stopped=0, Queued=1, Connecting=2, Downloading=3, Complete=4, Error=5`.
Dates are stored as ticks (JavaScriptSerializer mangles `DateTime`). On load, any item that is not Complete/Error becomes Stopped
(nothing auto-starts at launch).

### Segment
`Start, End` (inclusive byte range), `Downloaded` (bytes already written for this range). `Assigned` is runtime only.

### Settings (defaults)
`DefaultFolder` = `%USERPROFILE%\Downloads`, `UseCategoryFolders` = true, `MaxConnections` = 8 (1..32), `MaxConcurrent` = 3 (1..10),
`SpeedLimitKBps` = 0 (unlimited, global), `MonitorClipboard` = true, `MinimizeToTray` = true, `ShowProgressDialog` = true.

### Categories (by file extension)
Compressed: zip rar 7z gz tgz tar bz2 xz zst cab. Documents: pdf doc docx xls xlsx ppt pptx txt rtf epub mobi csv odt.
Music: mp3 wav flac aac ogg m4a wma opus. Programs: exe msi apk dmg iso jar bat msix appx deb rpm img bin.
Video: mp4 mkv avi mov wmv flv webm m4v 3gp mpg mpeg ts. Everything else: Other.

## 4. Download engine (`Engine/`)

### 4.1 Probe (first start of an item)
`GET url` with `Range: bytes=0-0`, headers only (`ResponseHeadersRead`).
- `206` with a `Content-Range` total -> `SupportsRange = true`, `TotalSize = total`.
- `200` -> `SupportsRange = false`, `TotalSize = Content-Length` or -1.
- `416` -> empty file, `TotalSize = 0`.
- other status -> error `Server replied: <code> <reason>`.
File name priority: `Content-Disposition` (`filename*=` then `filename=`), then last path segment of the final (redirected) URL,
then the host name. Invalid characters become `_`. If the name exists (or `.part` exists, or another item uses it) the name becomes `name (1).ext`, `(2)`...

### 4.2 Segments
If ranges are supported: `n = clamp(TotalSize / 512 KB, 1, MaxConnections)` equal ranges (last one takes the remainder).
Otherwise one segment. The `.part` file is pre-sized to `TotalSize`.
On resume the saved segments are reused if the part file still exists and is at least `TotalSize` long; otherwise everything restarts.
Unknown size: one stream, no resume, restart from zero, size is set when the stream ends.

### 4.3 Workers and dynamic segmentation
`MaxConnections` workers (1 if no range support). A worker repeatedly takes an unassigned, unfinished segment; if none is left it
**steals**: it picks the segment with the most bytes remaining; if that has at least `2 x 256 KB` left, the segment is split at the
midpoint of what is left and the worker takes the upper half as a new segment. So all connections stay busy to the end.

### 4.4 One request (`FetchAsync`)
- `Range: bytes=<next byte>-<segment end>`. When resuming at a position > 0 the answer must be `206`, otherwise fatal error
  ("server stopped supporting resume").
- 64 KB buffer. Read loop: read, then **under the segment lock** clamp the byte count to what is still owned by this segment
  (another worker may have just taken the tail), wait for the speed limiter, write at the right file offset, and only **after the
  write** add to `Downloaded`.
- Each worker has its own `FileStream` on the `.part` file (`FileShare.ReadWrite`).
- Per-read timeout 30 s. Implemented with a linked `CancellationTokenSource.CancelAfter` **and** a registration that disposes the
  `HttpResponseMessage` on cancel, because on .NET Framework `ReadAsync` on the response stream ignores cancellation tokens.
- `UseCookies = false`, no automatic decompression, `Accept-Encoding: identity` (otherwise byte ranges break).
- Request headers copied from the item: `Referer`, `Cookie`, `User-Agent`.

### 4.5 Errors and retry
Per segment: up to 8 consecutive failed attempts without progress, waiting 1, 2, 4, 8, 10 s between attempts. Progress resets the counter.
Fatal (no retry): HTTP 4xx except 408/429, disk write errors, ranges no longer supported.
When one worker fails fatally, the others are cancelled and the item becomes `Error` with the message.

### 4.6 Completion
All segment bytes must add up to `TotalSize` (otherwise error "Download incomplete"). The `.part` file is renamed to `SavePath`
(a new unique name is picked if something appeared meanwhile), segments are cleared, status = Complete, `Completed` event fires.

### 4.7 Manager
- `Add` creates a Stopped item with a guessed file name. `Start` makes it Queued; `Pump` starts queued items while fewer than
  `MaxConcurrent` jobs run. `Stop` cancels the job (status becomes Stopped when its workers have ended). `StartQueue/StopQueue` act on
  items with `InQueue = true` ("Download Later" sets it).
- `RemoveAsync` stops the job, removes the item, deletes the `.part` file, and the final file only if asked.
- `Redownload` deletes partial/complete data and probes again.
- Speed: a timer every 500 ms computes `Speed = 0.7 * old + 0.3 * instant` per active item.
- Speed limiter: one global limiter shared by all connections (`Engine/SpeedLimiter.cs`).

### 4.8 Engine invariants (do not break)
1. A segment's `End` can shrink at any time (steal). Never write past the current `End`.
2. `Downloaded` is increased only after the bytes were written.
3. A steal must leave at least `MinSplit` bytes to the original owner, so an in-flight 64 KB buffer can never overlap the new segment.
4. Lock order: `item.SegLock` first, then a single `segment`. Never take `SegLock` while holding a segment lock.
5. Never `await` inside a `lock`.

## 5. User interface (`UI/`)
Code-only WinForms. Segoe UI 9 pt. Main window sizes itself with a DPI helper (`Px`), dialogs use `AutoScaleMode.Dpi`.
The app is DPI-aware through `app.manifest`.

**Main window** (top to bottom): menu (Tasks, Downloads, View, Options, Help) / toolbar with 32 px icons drawn from the Windows icon font
(Add URL, Resume, Stop, Stop All | Delete, Clear Done | Options, Start Queue, Stop Queue) / split view: left category tree
(All Downloads > Compressed, Documents, Music, Programs, Video, Other; Unfinished; Finished; Queues > Main download queue),
right list with columns File Name, Size, Status, Time left, Transfer rate, Last try date, Save to / status bar (counts, total speed).
List refreshes every 400 ms without flicker (double-buffered, cell text updated only when it changed).
Status text: `45.3%`, `Connecting...`, `Queued`, `Stopped (45.3%)`, `Complete`, `Error` (tooltip shows the message).
Double click: opens the file if complete, otherwise the progress window. Right-click menu: Open, Open folder, Resume, Stop, Redownload, Copy address, Delete.
Delete asks whether to also delete the file from disk.

**Add URL dialog:** multi-line address box (one link per line, Enter starts the download, pasting many lines works), save folder + Browse,
buttons Start Download / Download Later / Cancel. Only http(s) links are accepted. The clipboard is pre-filled when it holds a file link.

**Progress window:** URL, status, size, downloaded %, rate, time left, resume capability, number of active segments, overall bar,
and the **connection map** (a bar where each segment is drawn in its own colour from its start to its downloaded position),
buttons Pause/Resume, Hide, Close, Open folder. Closes itself on completion.

**Completion popup:** small always-on-top window bottom-right (not stealing focus): file name, size, folder, Open / Open folder / Close, auto-closes after 20 s. Plays the system sound.

**Tray:** icon with menu (Open, Add URL, Stop all, Exit). Closing the window hides to tray when `MinimizeToTray`. Exit with running downloads asks first.

**Clipboard watcher:** every second; if the clipboard holds an http(s) link to a known file type, a tray balloon offers to download it.

**Options:** default folder, category folders, clipboard watching, progress window, tray behaviour, start with Windows
(`HKCU\...\Run`, started with `--tray`), connections per download, simultaneous downloads, speed limit.

**Single instance:** a named mutex. A second launch passes its link/args to the first one over the pipe and exits.

## 6. Browser link

### 6.1 Pipe `SwiftGet.Pipe` (app side, one-way, text lines)
`SHOW` brings the window up. `ADD <url>` opens the Add dialog. A line starting with `{` is JSON:
`{"cmd":"add","url":"...","referer":"...","cookie":"...","userAgent":"..."}` -> Add dialog (always on top, main window stays hidden if it was)
pre-filled with the link; the referer/cookie/user-agent are attached to the new item when the address was not edited.

### 6.2 Native messaging host
Chrome starts `SwiftGet.exe` with the argument `chrome-extension://<id>/` (the exe detects this and runs headless).
Protocol: 4-byte little-endian length + UTF-8 JSON, both directions. Commands:
`{"cmd":"ping"}` -> `{"ok":true,"version":"1.0","running":<bool>}`; `{"cmd":"show"}`; `{"cmd":"add","url",...}` -> forwards to the pipe,
starting `SwiftGet.exe --tray` first if it is not running (waits up to 10 s). Bad links and unknown commands return `{"ok":false,"error":"..."}`.

### 6.3 Registration (every app start, per user, no admin)
Manifest `%APPDATA%\SwiftGet\native\com.swiftget.host.json` (`path` = current exe, `allowed_origins` = `chrome-extension://omhegfoodfdkjlejdkibljilhcnmdlgb/`).
Registry (default value = manifest path): `HKCU\Software\Google\Chrome\NativeMessagingHosts\com.swiftget.host`, same under
`Microsoft\Edge` and `BraveSoftware\Brave-Browser`.

### 6.4 Extension (`extension/`, Manifest V3)
Permissions: downloads, nativeMessaging, contextMenus, cookies, storage, all URLs.
- `chrome.downloads.onCreated`: if enabled and the link is http(s) and matches the rules (file extension list, optional "everything but HTML",
  minimum size, excluded sites) -> send `add` (with cookies for that URL) to the host. **Only if the host answers ok**, cancel and erase the browser's
  own download. If the host is missing, the browser keeps downloading and the toolbar badge shows `!`.
- Context menu "Download with SwiftGet" on links, images, video, audio.
- Popup: connection status (ping), on/off switch, Open SwiftGet, Settings. Options page: extension list, intercept-all, minimum size, excluded sites.
- The extension ID is fixed by the `key` in `manifest.json`. The extension is embedded in the exe and unpacked from
  *Tasks > Browser extension setup*; the user loads it with "Load unpacked".

## 7. Known limitations
No scheduler, proxy settings, logins, FTP, video-site grabbing. No ETag / `If-Range` check, so a file that changes on the server during a pause
can produce a corrupt result. The UI has been checked under Mono/Xvfb but needs the manual checklist on real Windows (`docs/TESTING.md`).
The exe is not code-signed, so Windows SmartScreen shows "More info > Run anyway".
