# WindowsCleaner Design

Date: 2026-10-05

## Goal
A CCleaner-style desktop utility for Windows 11, delivered as a single self-contained `WindowsCleaner.exe`.

## Stack
.NET 10, WinForms. Published with `dotnet publish -r win-x64 --self-contained -p:PublishSingleFile=true`. The app manifest requests `requireAdministrator` (needed for Prefetch, Windows Update cache, HKLM).

## Solution layout
- `src/WindowsCleaner.Core` — no UI; unit-testable.
- `src/WindowsCleaner.App` — WinForms UI.
- `tests/WindowsCleaner.Tests` — xUnit tests.

## Core abstraction
```csharp
interface ICleaner {
    string Name { get; }
    Task<IReadOnlyList<CleanItem>> ScanAsync(CancellationToken ct);
    Task<CleanResult> CleanAsync(IEnumerable<CleanItem> selected, IProgress<string>? progress, CancellationToken ct);
}
record CleanItem(string Id, string Category, string Description, long SizeBytes, bool SelectedByDefault);
record CleanResult(long BytesFreed, int Deleted, int Skipped, IReadOnlyList<string> Errors);
```

## Components
1. **JunkCleaner** — user temp (`%TEMP%`), `C:\Windows\Temp`, Prefetch, Windows Update download cache (`SoftwareDistribution\Download`), `C:\Windows\Logs` (*.log/*.etl older than 7 days), thumbnail cache, crash dumps (`%LOCALAPPDATA%\CrashDumps`, `C:\Windows\Minidump`), Recycle Bin (via `SHEmptyRecycleBin`).
2. **BrowserCleaner** — Chrome, Edge (Chromium profiles) and Firefox: cache, cookies, history as separately selectable items. If the browser process is running, its items are listed as "close browser first" and not cleaned.
3. **RegistryCleaner** — scans for: orphaned Uninstall entries (missing install location/uninstaller), dead file-extension handlers, missing shared DLLs, stale App Paths and MUICache entries. Before any fix, exports affected keys to a timestamped `.reg` backup in `%LOCALAPPDATA%\WindowsCleaner\Backups`. A restore option imports a chosen backup.
4. **StartupManager** — lists HKCU/HKLM Run keys, Startup folders, and logon scheduled tasks; enable/disable (disable via StartupApproved keys / task disable, reversible; no deletion).
5. **AppManager** — lists installed apps from Uninstall keys with size/publisher/date; launches the app's own `UninstallString`.

## UI
Horizontal icon toolbar under the menu bar: Cleaner, Registry, Startup, Uninstall, Settings, About (the selected page is highlighted). Cleaner/Registry pages: checkbox tree grouped by category with sizes, **Analyze** and **Run Cleaner** buttons, progress bar, and a total. Startup/Uninstall pages: sortable list with action buttons.

About page: app name, version, repository link, and a **Check for updates** button (on click only) that queries the GitHub releases API and, if newer, offers an "Open download page" link to the validated release URL.

## Data flow
Analyze runs `ScanAsync` on selected cleaners in the background and fills the list. User unticks items. Run Cleaner shows a confirmation with the total size; on confirm calls `CleanAsync` and reports freed space, skipped (locked/in-use) count, and errors. In-use files are skipped, never fatal.

## Safety rules
- No deletion without a prior Analyze and an explicit confirmation.
- Deletion is restricted to a hard-coded allowlist of roots (`SafePaths`); any path resolving outside is refused.
- Symlinks and junctions are never followed or traversed.
- Registry fixes always back up first; backup failure aborts the fix.
- No telemetry; the only network call is the user-initiated 'Check for updates' on the About page (GitHub releases API, no automatic download).
- Settings persisted to `%LOCALAPPDATA%\WindowsCleaner\settings.json`.

## Testing
xUnit tests for: scanning and size computation against temp-dir fixtures, allowlist enforcement (path escape, junction), skip-on-locked-file behavior, `.reg` backup generation, startup entry parsing. Cleaners take injectable root paths/registry abstractions so tests never touch the real system. A manual smoke test runs Analyze only on the real machine.

## v1 deviations (decided while planning)
- Firefox history is not cleaned: it shares `places.sqlite` with bookmarks.
- `MEMORY.DMP` is not cleaned: its allowlist root would be all of `C:\Windows`.
- Files in the temp folders modified within the last 24 hours are skipped.

## Out of scope for v1
Scheduled cleaning, secure wipe, driver updater, tray icon, installer/auto-update.
