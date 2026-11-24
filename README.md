# SwiftGet

**A free, open-source download manager for Windows** with a classic IDM-style look, plus a browser extension for Chrome, Edge and Brave.

- **Faster downloads:** up to 32 connections per file. When one connection finishes, it takes half of the biggest remaining piece, so all connections stay busy to the end.
- **Pause and resume:** survives closing the app, crashes and restarts. Dropped connections are retried automatically.
- **Familiar layout:** toolbar, category tree (Compressed, Documents, Music, Programs, Video), download list, progress window with a live per-connection map.
- **Queue and limits:** downloads-at-once limit, global speed limiter, "Download later" queue.
- **Everyday comforts:** system tray, clipboard link watcher, "download complete" popup, start with Windows, many links at once.
- **Browser extension:** takes over browser downloads (with your cookies, so logged-in downloads work) and adds "Download with SwiftGet" to the right-click menu.
- **Tiny and free:** one ~200 KB exe, no installer, no runtime to install, no ads, no tracking. MIT license.

## Download

Go to the **[Releases](../../releases)** page and download `SwiftGet.exe`. Double-click it.
Windows may show "Windows protected your PC" because the exe is not code-signed (that costs money). Click **More info**, then **Run anyway**.

## Build it yourself

1. Install the free .NET SDK once: `winget install Microsoft.DotNet.SDK.8`
2. Double-click **`build.bat`**. Your app appears in `dist\SwiftGet.exe`.

No installs at all? Fork this repo on GitHub and open the **Actions** tab. Every push builds `SwiftGet.exe` for you (look under *Artifacts*).

## Browser extension

1. Start SwiftGet once.
2. In SwiftGet open **Tasks > Browser extension setup...** and click **Prepare extension**.
3. In your browser open `chrome://extensions` (or `edge://extensions`), turn on **Developer mode**, click **Load unpacked**, and pick the folder that opened.

If SwiftGet can't be reached, the browser simply keeps the download, so nothing is ever lost.

## For developers and AI coding agents

| Read this | To learn |
|---|---|
| [`AGENTS.md`](AGENTS.md) | Rules, commands and project map (also used by `CLAUDE.md` / `GEMINI.md`) |
| [`docs/SPEC.md`](docs/SPEC.md) | Exactly how everything works (engine, UI, browser link, file formats) |
| [`docs/ROADMAP.md`](docs/ROADMAP.md) | What to build next, with "done when" checks |
| [`docs/TESTING.md`](docs/TESTING.md) | Automatic tests and the manual checklist |
| [`docs/AI_AGENT_PROMPTS.md`](docs/AI_AGENT_PROMPTS.md) | Ready-to-paste prompts for your coding agent |

Run the engine tests: `dotnet run --project tests/EngineSmoke -c Release`

## Project layout

```
Engine/        the download engine (no UI code)
UI/            WinForms windows and dialogs (written in code, no designer files)
extension/     Chrome/Edge extension (embedded inside the exe at build time)
tests/         engine tests with their own fake web server
docs/          specification, roadmap, testing guide, agent prompts
.github/       automatic build, test and release
```

## Releasing a new version

```
git tag v1.0.0
git push origin v1.0.0
```
GitHub then builds `SwiftGet.exe` and attaches it to a new Release automatically.

## License

[MIT](LICENSE). SwiftGet is an independent project and is not affiliated with Internet Download Manager.
