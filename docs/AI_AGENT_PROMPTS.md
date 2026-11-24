# Prompts to give your AI coding agent

Open this repository folder in your agent (Antigravity, Claude Code, Cursor, Codex...), then paste one of these.
Everything the agent needs is in `AGENTS.md`, `docs/SPEC.md`, `docs/ROADMAP.md` and `docs/TESTING.md`.

---
## 1. First run: check that everything works
```
Read AGENTS.md and docs/SPEC.md. Then:
1. Build the app and run the engine tests exactly as described in AGENTS.md.
2. Tell me in simple words whether the build and all tests passed. If something fails, fix it and explain what was wrong.
3. Walk me through the manual checklist in docs/TESTING.md one step at a time, and wait for me to answer before the next step.
```

## 2. Build the next feature from the roadmap
```
Read AGENTS.md, docs/SPEC.md and docs/ROADMAP.md.
Implement this roadmap item: <PASTE THE ITEM, e.g. "Scheduler">.
Follow the rules in AGENTS.md. Work in small steps, build and run the tests after each step, add a test if you change the engine,
update docs/SPEC.md, and then tell me in simple words what changed and how I try it myself.
```

## 3. Fix a bug
```
Read AGENTS.md and docs/SPEC.md.
Bug: <WHAT YOU DID, WHAT YOU EXPECTED, WHAT HAPPENED, any error text or screenshot>.
Find the cause first and explain it in simple words. Then fix it, add a test that would have caught it (if it is in the engine),
build, run the tests, and tell me how to check the fix.
```

## 4. Rebuild the whole project from scratch (only if you want a fresh start)
```
Create a brand new Windows download manager in a NEW empty folder, using docs/SPEC.md as the only source of truth and AGENTS.md for the rules.
Build it in this order, running a test or a manual check after each step:
 1. Engine (probe, segments, workers, retry, resume, manager) + tests/EngineSmoke style tests
 2. Main window, Add URL dialog, progress window
 3. Options, tray, clipboard watcher, completion popup
 4. Single instance + pipe, native messaging host, extension, setup dialog
 5. build.bat, GitHub Actions workflows, README
Do not move on while a test is failing. Explain progress in simple words.
```

## 5. Publish a new version
```
Update the version number in SwiftGet.csproj and the extension manifest, make sure the build and tests pass, then tell me the exact
git commands to create a tag named vX.Y.Z and push it, so GitHub builds the release.
```
