# WindowsCleaner Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build WindowsCleaner, a CCleaner-style WinForms app that cleans junk files, browser caches and registry orphans and manages startup items and installed apps on Windows 11, shipped as one self-contained `.exe`.

**Architecture:** A UI-free `WindowsCleaner.Core` library holds every cleaner behind `ICleaner` (scan → items with sizes, clean → result). System access (registry, recycle bin, running processes, file roots) is injected so xUnit tests never touch the real machine. `WindowsCleaner.App` is a thin WinForms shell: a left nav and pages that drive the cleaners.

**Tech Stack:** .NET 10 (`net10.0-windows`), WinForms, xUnit, NuGet `TaskScheduler` (scheduled-task startup entries), `reg.exe` for `.reg` backups.

## Global Constraints

- Single self-contained file: `dotnet publish -r win-x64 --self-contained -p:PublishSingleFile=true`.
- App manifest requests `requireAdministrator`.
- No deletion without a prior Analyze and an explicit confirmation.
- Deletion only inside the hard-coded allowlist roots (`SafePaths`); symlinks/junctions are never followed or traversed.
- Registry fixes always export a `.reg` backup first; backup failure aborts the fix.
- No telemetry; the only network call is the user-initiated update check on the About page.
- Data lives in `%LOCALAPPDATA%\WindowsCleaner` (`settings.json`, `Backups\`).
- Locked or in-use files are skipped and counted, never fatal.
- Out of scope: scheduled cleaning, secure wipe, driver updater, tray icon, installer/auto-update.
- Deliberate v1 deviations from the spec: Firefox *history* is not cleaned (it lives in `places.sqlite` together with bookmarks), and `MEMORY.DMP` is not cleaned (its root would be all of `C:\Windows`, too broad for the allowlist). Temp-folder files modified in the last 24 hours are skipped.

## File Structure

```
WindowsCleaner.slnx
.gitignore
src/WindowsCleaner.Core/
  WindowsCleaner.Core.csproj
  ICleaner.cs               CleanItem, CleanResult, ICleaner
  SafePaths.cs              allowlist + reparse-point guard
  SizeFormat.cs             "1.5 KB" formatting
  AppPaths.cs               %LOCALAPPDATA%\WindowsCleaner paths
  AppSettings.cs            settings.json load/save
  FileEnumerator.cs         junction-safe file enumeration
  FileTargetCleaner.cs      generic file-target cleaner (+ FileTarget)
  JunkCleaner.cs            Windows junk + IRecycleBin / ShellRecycleBin
  BrowserCleaner.cs         Chrome/Edge/Firefox targets
  IRegistryAccess.cs        registry abstraction
  RegistryAccess.cs         real implementation
  RegistryCleaner.cs        scan/fix/backup/restore
  Startup/StartupEntry.cs   StartupEntry, IStartupSource
  Startup/StartupApproval.cs
  Startup/RunKeyStartupSource.cs
  Startup/StartupFolderSource.cs
  Startup/TaskStartupSource.cs
  Startup/StartupManager.cs
  AppManager.cs             InstalledApp list + uninstall
src/WindowsCleaner.App/
  WindowsCleaner.App.csproj, app.manifest, Program.cs
  MainForm.cs, CleanerPage.cs, StartupPage.cs, UninstallPage.cs, SettingsPage.cs
tests/WindowsCleaner.Tests/
  TempDir.cs, FakeRegistry.cs
  CoreBasicsTests.cs, FileTargetCleanerTests.cs, JunkCleanerTests.cs,
  BrowserCleanerTests.cs, RegistryCleanerTests.cs, StartupTests.cs, AppManagerTests.cs
```

---

### Task 1: Scaffold, core primitives, SafePaths

**Files:**
- Create: solution, three projects, `.gitignore`
- Create: `src/WindowsCleaner.Core/{ICleaner,SafePaths,SizeFormat,AppPaths,AppSettings}.cs`
- Create: `tests/WindowsCleaner.Tests/{TempDir,CoreBasicsTests}.cs`

**Interfaces:**
- Produces:
  - `record CleanItem(string Id, string Category, string Description, long SizeBytes, bool SelectedByDefault = true)`
  - `record CleanResult(long BytesFreed, int Deleted, int Skipped, IReadOnlyList<string> Errors)` with `static CleanResult Empty` and `CleanResult Plus(CleanResult)`
  - `interface ICleaner { string Name; Task<IReadOnlyList<CleanItem>> ScanAsync(CancellationToken ct = default); Task<CleanResult> CleanAsync(IEnumerable<CleanItem> selected, IProgress<string>? progress = null, CancellationToken ct = default); }`
  - `SafePaths(IEnumerable<string> roots)`, `bool IsAllowed(string path)`
  - `SizeFormat.Format(long)`
  - `AppPaths.DataDir / SettingsFile / BackupsDir`
  - `AppSettings { int LogRetentionDays = 7; static Load(string? path=null); Save(string? path=null) }`
  - Test helper `TempDir { string Path; string Write(string relPath, string content = "x", DateTime? lastWriteUtc = null); static void Junction(string link, string target); }`

- [ ] **Step 1: Create the solution and projects**

Run from `C:\Users\Bob\Projects\WindowsCleaner`:

```bash
git init
dotnet new sln -n WindowsCleaner
dotnet new classlib -o src/WindowsCleaner.Core
dotnet new xunit -o tests/WindowsCleaner.Tests
mkdir -p src/WindowsCleaner.App
rm src/WindowsCleaner.Core/Class1.cs tests/WindowsCleaner.Tests/UnitTest1.cs
```

Replace `src/WindowsCleaner.Core/WindowsCleaner.Core.csproj` with:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>WindowsCleaner.Core</RootNamespace>
  </PropertyGroup>
</Project>
```

Create `src/WindowsCleaner.App/WindowsCleaner.App.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <UseWindowsForms>true</UseWindowsForms>
    <AssemblyName>WindowsCleaner</AssemblyName>
    <RootNamespace>WindowsCleaner.App</RootNamespace>
    <ApplicationHighDpiMode>PerMonitorV2</ApplicationHighDpiMode>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\WindowsCleaner.Core\WindowsCleaner.Core.csproj" />
  </ItemGroup>
</Project>
```

Create a placeholder `src/WindowsCleaner.App/Program.cs` so the project builds until Task 7:

```csharp
namespace WindowsCleaner.App;

internal static class Program
{
    [STAThread]
    private static void Main() { }
}
```

Then wire everything up. The tests project template targets `net10.0`; Core is `net10.0-windows`, so change it:

```bash
sed -i 's#<TargetFramework>net10.0</TargetFramework>#<TargetFramework>net10.0-windows</TargetFramework>#' tests/WindowsCleaner.Tests/WindowsCleaner.Tests.csproj
dotnet add tests/WindowsCleaner.Tests reference src/WindowsCleaner.Core
dotnet sln add src/WindowsCleaner.Core src/WindowsCleaner.App tests/WindowsCleaner.Tests
dotnet add src/WindowsCleaner.Core package TaskScheduler
```

Create `.gitignore`:

```
bin/
obj/
dist/
.vs/
*.user
```

Run: `dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 2: Write the failing tests**

`tests/WindowsCleaner.Tests/TempDir.cs`:

```csharp
using System.Diagnostics;

namespace WindowsCleaner.Tests;

public sealed class TempDir : IDisposable
{
    public string Path { get; } =
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), "wc-test-" + Guid.NewGuid().ToString("N"));

    public TempDir() => Directory.CreateDirectory(Path);

    public string Write(string relPath, string content = "x", DateTime? lastWriteUtc = null)
    {
        var full = System.IO.Path.Combine(Path, relPath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        if (lastWriteUtc is { } t) File.SetLastWriteTimeUtc(full, t);
        return full;
    }

    public static void Junction(string link, string target)
    {
        var psi = new ProcessStartInfo("cmd.exe") { CreateNoWindow = true, UseShellExecute = false };
        psi.Arguments = $"/c mklink /J \"{link}\" \"{target}\"";
        using var p = Process.Start(psi)!;
        p.WaitForExit();
        if (p.ExitCode != 0) throw new InvalidOperationException("mklink /J failed");
    }

    public void Dispose()
    {
        try
        {
            foreach (var d in Directory.EnumerateDirectories(Path, "*", SearchOption.AllDirectories)
                         .Where(d => new DirectoryInfo(d).Attributes.HasFlag(FileAttributes.ReparsePoint)).ToList())
                Directory.Delete(d); // removes the junction itself, never its target
            Directory.Delete(Path, true);
        }
        catch { /* best effort */ }
    }
}
```

`tests/WindowsCleaner.Tests/CoreBasicsTests.cs`:

```csharp
using WindowsCleaner.Core;

namespace WindowsCleaner.Tests;

public class CoreBasicsTests
{
    [Theory]
    [InlineData(0L, "0 B")]
    [InlineData(1536L, "1.5 KB")]
    [InlineData(5L * 1024 * 1024, "5 MB")]
    public void SizeFormat_formats(long bytes, string expected) =>
        Assert.Equal(expected, SizeFormat.Format(bytes));

    [Fact]
    public void CleanResult_Plus_adds_fields()
    {
        var r = new CleanResult(10, 1, 2, ["a"]).Plus(new CleanResult(5, 3, 4, ["b"]));
        Assert.Equal(15, r.BytesFreed);
        Assert.Equal(4, r.Deleted);
        Assert.Equal(6, r.Skipped);
        Assert.Equal(["a", "b"], r.Errors);
    }

    [Fact]
    public void Settings_roundtrip_and_default_on_missing_file()
    {
        using var t = new TempDir();
        var path = Path.Combine(t.Path, "settings.json");
        Assert.Equal(7, AppSettings.Load(path).LogRetentionDays);
        new AppSettings { LogRetentionDays = 30 }.Save(path);
        Assert.Equal(30, AppSettings.Load(path).LogRetentionDays);
    }

    [Fact]
    public void SafePaths_allows_descendants_only()
    {
        using var t = new TempDir();
        var safe = new SafePaths([t.Path]);
        Assert.True(safe.IsAllowed(t.Write("a/b.txt")));
        Assert.False(safe.IsAllowed(t.Path));                                   // root itself
        Assert.False(safe.IsAllowed(Path.Combine(t.Path, "..", "other.txt")));  // .. escape
        Assert.False(safe.IsAllowed(Path.Combine(t.Path + "-evil", "f.txt")));  // prefix sibling
    }

    [Fact]
    public void SafePaths_refuses_paths_through_a_junction()
    {
        using var t = new TempDir();
        using var outside = new TempDir();
        outside.Write("secret.txt");
        var link = Path.Combine(t.Path, "link");
        TempDir.Junction(link, outside.Path);
        Assert.False(new SafePaths([t.Path]).IsAllowed(Path.Combine(link, "secret.txt")));
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test`
Expected: build FAIL (`SizeFormat`, `CleanResult`, `AppSettings`, `SafePaths` not defined).

- [ ] **Step 4: Implement**

`src/WindowsCleaner.Core/ICleaner.cs`:

```csharp
namespace WindowsCleaner.Core;

public sealed record CleanItem(string Id, string Category, string Description, long SizeBytes, bool SelectedByDefault = true);

public sealed record CleanResult(long BytesFreed, int Deleted, int Skipped, IReadOnlyList<string> Errors)
{
    public static CleanResult Empty { get; } = new(0, 0, 0, Array.Empty<string>());

    public CleanResult Plus(CleanResult o) =>
        new(BytesFreed + o.BytesFreed, Deleted + o.Deleted, Skipped + o.Skipped, Errors.Concat(o.Errors).ToList());
}

public interface ICleaner
{
    string Name { get; }
    Task<IReadOnlyList<CleanItem>> ScanAsync(CancellationToken ct = default);
    Task<CleanResult> CleanAsync(IEnumerable<CleanItem> selected, IProgress<string>? progress = null, CancellationToken ct = default);
}
```

`src/WindowsCleaner.Core/SafePaths.cs`:

```csharp
namespace WindowsCleaner.Core;

/// <summary>Allowlist of root folders deletions may happen under. Refuses the root itself,
/// anything outside it, and any path that passes through a reparse point (junction/symlink).</summary>
public sealed class SafePaths
{
    private readonly string[] _roots;

    public SafePaths(IEnumerable<string> roots) =>
        _roots = roots.Select(r => Path.TrimEndingDirectorySeparator(Path.GetFullPath(r))).ToArray();

    public bool IsAllowed(string path)
    {
        var full = Path.GetFullPath(path);
        foreach (var root in _roots)
        {
            if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue;
            return !HasReparsePoint(full, root);
        }
        return false;
    }

    private static bool HasReparsePoint(string full, string root)
    {
        for (var p = full; p is not null && p.Length >= root.Length; p = Path.GetDirectoryName(p))
        {
            try
            {
                if ((File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0) return true;
            }
            catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException) { }
        }
        return false;
    }
}
```

`src/WindowsCleaner.Core/SizeFormat.cs`:

```csharp
using System.Globalization;

namespace WindowsCleaner.Core;

public static class SizeFormat
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB"];

    public static string Format(long bytes)
    {
        double v = bytes;
        var i = 0;
        while (v >= 1024 && i < Units.Length - 1) { v /= 1024; i++; }
        return i == 0 ? $"{bytes} B" : string.Create(CultureInfo.InvariantCulture, $"{v:0.#} {Units[i]}");
    }
}
```

`src/WindowsCleaner.Core/AppPaths.cs`:

```csharp
namespace WindowsCleaner.Core;

public static class AppPaths
{
    public static string DataDir { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WindowsCleaner");
    public static string SettingsFile => Path.Combine(DataDir, "settings.json");
    public static string BackupsDir => Path.Combine(DataDir, "Backups");
}
```

`src/WindowsCleaner.Core/AppSettings.cs`:

```csharp
using System.Text.Json;

namespace WindowsCleaner.Core;

public sealed class AppSettings
{
    public int LogRetentionDays { get; set; } = 7;

    public static AppSettings Load(string? path = null)
    {
        path ??= AppPaths.SettingsFile;
        try { return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new(); }
        catch { return new(); }
    }

    public void Save(string? path = null)
    {
        path ??= AppPaths.SettingsFile;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test`
Expected: all tests PASS.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: scaffold solution, core primitives and SafePaths"
```

---

### Task 2: FileTargetCleaner and JunkCleaner

**Files:**
- Create: `src/WindowsCleaner.Core/{FileEnumerator,FileTargetCleaner,JunkCleaner}.cs`
- Test: `tests/WindowsCleaner.Tests/{FileTargetCleanerTests,JunkCleanerTests}.cs`

**Interfaces:**
- Consumes: `ICleaner`, `CleanItem`, `CleanResult`, `SafePaths`, `AppSettings`, `TempDir` (Task 1).
- Produces:
  - `FileEnumerator.Files(string root, string pattern = "*", Func<FileInfo,bool>? filter = null) : IEnumerable<string>`; `FileEnumerator.Single(string file) : IEnumerable<string>`
  - `record FileTarget(string Id, string Category, string Description, Func<IEnumerable<string>> EnumerateFiles, bool SelectedByDefault = true, string? BlockedByProcess = null)`
  - `FileTargetCleaner(string name, IEnumerable<FileTarget> targets, SafePaths safe, Func<string,bool>? isRunning = null) : ICleaner`
  - `interface IRecycleBin { long GetSizeBytes(); void Empty(); }`, `ShellRecycleBin`
  - `JunkCleaner(IEnumerable<FileTarget> targets, SafePaths safe, IRecycleBin bin) : ICleaner`, `const string RecycleBinId = "junk.recyclebin"`, `static Func<FileInfo,bool> OlderThan(TimeSpan)`, `static JunkCleaner CreateDefault(AppSettings)`

- [ ] **Step 1: Write the failing tests**

`tests/WindowsCleaner.Tests/FileTargetCleanerTests.cs`:

```csharp
using WindowsCleaner.Core;

namespace WindowsCleaner.Tests;

public class FileTargetCleanerTests
{
    private static FileTargetCleaner Make(TempDir root, IEnumerable<FileTarget> targets, Func<string, bool>? running = null) =>
        new("test", targets, new SafePaths([root.Path]), running);

    [Fact]
    public async Task Scan_reports_sizes_and_omits_empty_targets()
    {
        using var t = new TempDir();
        t.Write("a/one.txt", "12345");
        var c = Make(t,
        [
            new FileTarget("t1", "Cat", "Has files", () => FileEnumerator.Files(t.Path)),
            new FileTarget("t2", "Cat", "Empty", () => FileEnumerator.Files(Path.Combine(t.Path, "none"))),
        ]);
        var item = Assert.Single(await c.ScanAsync());
        Assert.Equal("t1", item.Id);
        Assert.Equal(5, item.SizeBytes);
    }

    [Fact]
    public async Task Clean_deletes_selected_targets_and_reports_freed_bytes()
    {
        using var t = new TempDir();
        var f = t.Write("a.txt", "12345");
        var keep = t.Write("keep/b.txt", "xx");
        var c = Make(t,
        [
            new FileTarget("t1", "Cat", "A", () => FileEnumerator.Files(t.Path, "a.txt")),
            new FileTarget("t2", "Cat", "B", () => FileEnumerator.Files(Path.Combine(t.Path, "keep"))),
        ]);
        var items = await c.ScanAsync();
        var r = await c.CleanAsync(items.Where(i => i.Id == "t1"));
        Assert.False(File.Exists(f));
        Assert.True(File.Exists(keep));
        Assert.Equal(1, r.Deleted);
        Assert.Equal(5, r.BytesFreed);
    }

    [Fact]
    public async Task Clean_skips_locked_files()
    {
        using var t = new TempDir();
        var f = t.Write("locked.txt");
        var c = Make(t, [new FileTarget("t1", "Cat", "A", () => FileEnumerator.Files(t.Path))]);
        var items = await c.ScanAsync();
        using var hold = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.None);
        var r = await c.CleanAsync(items);
        Assert.True(File.Exists(f));
        Assert.Equal(0, r.Deleted);
        Assert.Equal(1, r.Skipped);
    }

    [Fact]
    public async Task Clean_refuses_files_outside_the_allowlist()
    {
        using var allowed = new TempDir();
        using var other = new TempDir();
        var f = other.Write("x.txt");
        var c = Make(allowed, [new FileTarget("t1", "Cat", "A", () => FileEnumerator.Files(other.Path))]);
        var r = await c.CleanAsync([new CleanItem("t1", "Cat", "A", 1)]);
        Assert.True(File.Exists(f));
        Assert.Equal(1, r.Skipped);
    }

    [Fact]
    public async Task Blocked_target_is_listed_unchecked_and_not_cleaned()
    {
        using var t = new TempDir();
        var f = t.Write("x.txt");
        var c = Make(t,
            [new FileTarget("t1", "Cat", "Cache", () => FileEnumerator.Files(t.Path), true, "chrome")],
            running: name => name == "chrome");
        var item = Assert.Single(await c.ScanAsync());
        Assert.False(item.SelectedByDefault);
        Assert.Contains("close chrome", item.Description);
        var r = await c.CleanAsync([item]);
        Assert.True(File.Exists(f));
        Assert.Equal(1, r.Skipped);
    }

    [Fact]
    public void Enumerator_does_not_follow_junctions()
    {
        using var t = new TempDir();
        using var outside = new TempDir();
        outside.Write("secret.txt");
        t.Write("real.txt");
        TempDir.Junction(Path.Combine(t.Path, "link"), outside.Path);
        var files = FileEnumerator.Files(t.Path).Select(Path.GetFileName).ToList();
        Assert.Equal(["real.txt"], files);
    }
}
```

`tests/WindowsCleaner.Tests/JunkCleanerTests.cs`:

```csharp
using WindowsCleaner.Core;

namespace WindowsCleaner.Tests;

public class JunkCleanerTests
{
    private sealed class FakeBin(long size) : IRecycleBin
    {
        public int EmptyCalls;
        public long GetSizeBytes() => size;
        public void Empty() => EmptyCalls++;
    }

    [Fact]
    public async Task Scan_includes_recycle_bin_and_clean_empties_it_when_selected()
    {
        using var t = new TempDir();
        var bin = new FakeBin(1000);
        var c = new JunkCleaner([], new SafePaths([t.Path]), bin);
        var items = await c.ScanAsync();
        var item = Assert.Single(items);
        Assert.Equal(JunkCleaner.RecycleBinId, item.Id);
        var r = await c.CleanAsync(items);
        Assert.Equal(1, bin.EmptyCalls);
        Assert.Equal(1000, r.BytesFreed);
    }

    [Fact]
    public async Task Clean_does_not_touch_recycle_bin_when_not_selected()
    {
        using var t = new TempDir();
        var bin = new FakeBin(1000);
        var c = new JunkCleaner([], new SafePaths([t.Path]), bin);
        await c.CleanAsync([]);
        Assert.Equal(0, bin.EmptyCalls);
    }

    [Fact]
    public void OlderThan_filters_by_last_write_time()
    {
        using var t = new TempDir();
        var old = new FileInfo(t.Write("old.txt", lastWriteUtc: DateTime.UtcNow.AddDays(-3)));
        var fresh = new FileInfo(t.Write("fresh.txt"));
        var filter = JunkCleaner.OlderThan(TimeSpan.FromDays(1));
        Assert.True(filter(old));
        Assert.False(filter(fresh));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test`
Expected: build FAIL (`FileEnumerator`, `FileTarget`, `FileTargetCleaner`, `JunkCleaner`, `IRecycleBin` not defined).

- [ ] **Step 3: Implement**

`src/WindowsCleaner.Core/FileEnumerator.cs`:

```csharp
namespace WindowsCleaner.Core;

public static class FileEnumerator
{
    // Skipping ReparsePoint means junction/symlink directories and files are neither listed nor entered.
    private static readonly EnumerationOptions Options = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
        MatchType = MatchType.Win32,
    };

    public static IEnumerable<string> Files(string root, string pattern = "*", Func<FileInfo, bool>? filter = null)
    {
        if (!Directory.Exists(root)) return [];
        return new DirectoryInfo(root).EnumerateFiles(pattern, Options)
            .Where(f => filter?.Invoke(f) ?? true)
            .Select(f => f.FullName);
    }

    public static IEnumerable<string> Single(string file) => File.Exists(file) ? [file] : [];
}
```

`src/WindowsCleaner.Core/FileTargetCleaner.cs`:

```csharp
using System.Diagnostics;

namespace WindowsCleaner.Core;

public sealed record FileTarget(
    string Id, string Category, string Description,
    Func<IEnumerable<string>> EnumerateFiles,
    bool SelectedByDefault = true,
    string? BlockedByProcess = null);

public sealed class FileTargetCleaner : ICleaner
{
    private readonly IReadOnlyList<FileTarget> _targets;
    private readonly SafePaths _safe;
    private readonly Func<string, bool> _isRunning;

    public FileTargetCleaner(string name, IEnumerable<FileTarget> targets, SafePaths safe, Func<string, bool>? isRunning = null)
    {
        Name = name;
        _targets = targets.ToList();
        _safe = safe;
        _isRunning = isRunning ?? (n => Process.GetProcessesByName(n).Length > 0);
    }

    public string Name { get; }

    public Task<IReadOnlyList<CleanItem>> ScanAsync(CancellationToken ct = default) => Task.Run(() =>
    {
        var items = new List<CleanItem>();
        foreach (var t in _targets)
        {
            ct.ThrowIfCancellationRequested();
            var blocked = t.BlockedByProcess is { } p && _isRunning(p);
            long size = blocked ? 0 : t.EnumerateFiles().Where(_safe.IsAllowed).Sum(SizeOf);
            if (size == 0 && !blocked) continue;
            var desc = blocked ? $"{t.Description} (close {t.BlockedByProcess} first)" : t.Description;
            items.Add(new CleanItem(t.Id, t.Category, desc, size, t.SelectedByDefault && !blocked));
        }
        return (IReadOnlyList<CleanItem>)items;
    }, ct);

    public Task<CleanResult> CleanAsync(IEnumerable<CleanItem> selected, IProgress<string>? progress = null, CancellationToken ct = default) =>
        Task.Run(() =>
        {
            var ids = selected.Select(i => i.Id).ToHashSet();
            long freed = 0;
            int deleted = 0, skipped = 0;
            var errors = new List<string>();
            foreach (var t in _targets.Where(t => ids.Contains(t.Id)))
            {
                if (t.BlockedByProcess is { } p && _isRunning(p))
                {
                    skipped++;
                    errors.Add($"{t.Description}: {p} is running");
                    continue;
                }
                progress?.Report(t.Description);
                foreach (var file in t.EnumerateFiles().ToList())
                {
                    ct.ThrowIfCancellationRequested();
                    if (!_safe.IsAllowed(file)) { skipped++; continue; }
                    try
                    {
                        var size = SizeOf(file);
                        File.Delete(file);
                        freed += size;
                        deleted++;
                    }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException) { skipped++; }
                }
            }
            return new CleanResult(freed, deleted, skipped, errors);
        }, ct);

    private static long SizeOf(string path)
    {
        try { return new FileInfo(path).Length; }
        catch { return 0; }
    }
}
```

`src/WindowsCleaner.Core/JunkCleaner.cs`:

```csharp
using System.Runtime.InteropServices;

namespace WindowsCleaner.Core;

public interface IRecycleBin
{
    long GetSizeBytes();
    void Empty();
}

public sealed class ShellRecycleBin : IRecycleBin
{
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct SHQUERYRBINFO
    {
        public uint cbSize;
        public long i64Size;
        public long i64NumItems;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHQueryRecycleBin(string? root, ref SHQUERYRBINFO info);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHEmptyRecycleBin(IntPtr hwnd, string? root, uint flags);

    private const uint NoConfirmation = 0x1, NoProgressUi = 0x2, NoSound = 0x4;

    public long GetSizeBytes()
    {
        var info = new SHQUERYRBINFO { cbSize = (uint)Marshal.SizeOf<SHQUERYRBINFO>() };
        return SHQueryRecycleBin(null, ref info) == 0 ? info.i64Size : 0;
    }

    public void Empty()
    {
        var hr = SHEmptyRecycleBin(IntPtr.Zero, null, NoConfirmation | NoProgressUi | NoSound);
        if (hr != 0) Marshal.ThrowExceptionForHR(hr);
    }
}

public sealed class JunkCleaner : ICleaner
{
    public const string RecycleBinId = "junk.recyclebin";

    private readonly FileTargetCleaner _files;
    private readonly IRecycleBin _bin;

    public JunkCleaner(IEnumerable<FileTarget> targets, SafePaths safe, IRecycleBin bin)
    {
        _files = new FileTargetCleaner("Junk files", targets, safe);
        _bin = bin;
    }

    public string Name => "System junk";

    public static Func<FileInfo, bool> OlderThan(TimeSpan age) => f => f.LastWriteTimeUtc < DateTime.UtcNow - age;

    public async Task<IReadOnlyList<CleanItem>> ScanAsync(CancellationToken ct = default)
    {
        var items = (await _files.ScanAsync(ct)).ToList();
        long bin = 0;
        try { bin = _bin.GetSizeBytes(); } catch { /* treat as empty */ }
        if (bin > 0) items.Add(new CleanItem(RecycleBinId, "Windows", "Recycle Bin", bin));
        return items;
    }

    public async Task<CleanResult> CleanAsync(IEnumerable<CleanItem> selected, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var list = selected.ToList();
        var result = await _files.CleanAsync(list, progress, ct);
        var binItem = list.FirstOrDefault(i => i.Id == RecycleBinId);
        if (binItem is null) return result;
        progress?.Report("Recycle Bin");
        try
        {
            _bin.Empty();
            return result.Plus(new CleanResult(binItem.SizeBytes, 1, 0, []));
        }
        catch (Exception e)
        {
            return result.Plus(new CleanResult(0, 0, 1, [$"Recycle Bin: {e.Message}"]));
        }
    }

    public static JunkCleaner CreateDefault(AppSettings settings)
    {
        var win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var temp = Path.GetTempPath();
        string W(params string[] parts) => Path.Combine([win, .. parts]);
        var explorer = Path.Combine(local, "Microsoft", "Windows", "Explorer");
        var crashDumps = Path.Combine(local, "CrashDumps");

        var day = OlderThan(TimeSpan.FromDays(1));
        var logAge = OlderThan(TimeSpan.FromDays(settings.LogRetentionDays));
        bool IsLog(FileInfo f) =>
            f.Extension.Equals(".log", StringComparison.OrdinalIgnoreCase) ||
            f.Extension.Equals(".etl", StringComparison.OrdinalIgnoreCase);

        FileTarget[] targets =
        [
            new("junk.usertemp", "Windows", "User temp files", () => FileEnumerator.Files(temp, "*", day)),
            new("junk.wintemp", "Windows", "Windows temp files", () => FileEnumerator.Files(W("Temp"), "*", day)),
            new("junk.prefetch", "Windows", "Prefetch", () => FileEnumerator.Files(W("Prefetch"), "*.pf")),
            new("junk.wucache", "Windows", "Windows Update download cache", () => FileEnumerator.Files(W("SoftwareDistribution", "Download"))),
            new("junk.logs", "Windows", "Old log files", () => FileEnumerator.Files(W("Logs"), "*", f => logAge(f) && IsLog(f))),
            new("junk.thumbs", "Windows", "Thumbnail cache", () => FileEnumerator.Files(explorer, "thumbcache_*.db")),
            new("junk.dumps", "Windows", "Crash dumps",
                () => FileEnumerator.Files(crashDumps).Concat(FileEnumerator.Files(W("Minidump")))),
        ];

        var safe = new SafePaths([temp, W("Temp"), W("Prefetch"), W("SoftwareDistribution", "Download"),
            W("Logs"), explorer, crashDumps, W("Minidump")]);
        return new JunkCleaner(targets, safe, new ShellRecycleBin());
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test`
Expected: all tests PASS.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: add FileTargetCleaner and JunkCleaner"
```

---

### Task 3: BrowserCleaner

**Files:**
- Create: `src/WindowsCleaner.Core/BrowserCleaner.cs`
- Test: `tests/WindowsCleaner.Tests/BrowserCleanerTests.cs`

**Interfaces:**
- Consumes: `FileTarget`, `FileTargetCleaner`, `FileEnumerator`, `SafePaths` (Task 2).
- Produces: `static class BrowserCleaner { static FileTargetCleaner Create(string localAppData, string appData, Func<string,bool>? isRunning = null); static FileTargetCleaner CreateDefault(); }`. Item ids: `chrome.cache|cookies|history`, `edge.cache|cookies|history`, `firefox.cache|cookies`. Cookies and history are not selected by default.

- [ ] **Step 1: Write the failing test**

`tests/WindowsCleaner.Tests/BrowserCleanerTests.cs`:

```csharp
using WindowsCleaner.Core;

namespace WindowsCleaner.Tests;

public class BrowserCleanerTests
{
    [Fact]
    public async Task Scan_and_clean_touch_only_selected_browser_data()
    {
        using var t = new TempDir();
        var local = t.Path;
        var roaming = Path.Combine(t.Path, "roaming");
        var cache = t.Write(@"Google\Chrome\User Data\Default\Cache\f1", "12345");
        var codeCache = t.Write(@"Google\Chrome\User Data\Profile 1\Code Cache\f2", "123");
        var cookies = t.Write(@"Google\Chrome\User Data\Default\Network\Cookies");
        var history = t.Write(@"Google\Chrome\User Data\Default\History");
        var crashpad = t.Write(@"Google\Chrome\User Data\Crashpad\c1");
        var ffCache = t.Write(@"Mozilla\Firefox\Profiles\abc.default\cache2\entries\e1", "12");
        var ffCookies = t.Write(@"roaming\Mozilla\Firefox\Profiles\abc.default\cookies.sqlite");

        var c = BrowserCleaner.Create(local, roaming, _ => false);
        var items = await c.ScanAsync();

        var chromeCache = items.Single(i => i.Id == "chrome.cache");
        Assert.Equal(8, chromeCache.SizeBytes);
        Assert.True(chromeCache.SelectedByDefault);
        Assert.False(items.Single(i => i.Id == "chrome.cookies").SelectedByDefault);
        Assert.False(items.Single(i => i.Id == "chrome.history").SelectedByDefault);
        Assert.Contains(items, i => i.Id == "firefox.cache");

        await c.CleanAsync(items.Where(i => i.Id is "chrome.cache" or "firefox.cache"));

        Assert.False(File.Exists(cache));
        Assert.False(File.Exists(codeCache));
        Assert.False(File.Exists(ffCache));
        Assert.True(File.Exists(cookies));
        Assert.True(File.Exists(history));
        Assert.True(File.Exists(crashpad));
        Assert.True(File.Exists(ffCookies));
    }

    [Fact]
    public async Task Running_browser_is_flagged_not_cleaned()
    {
        using var t = new TempDir();
        var cache = t.Write(@"Microsoft\Edge\User Data\Default\Cache\f1");
        var c = BrowserCleaner.Create(t.Path, Path.Combine(t.Path, "roaming"), n => n == "msedge");
        var item = Assert.Single(await c.ScanAsync());
        Assert.Equal("edge.cache", item.Id);
        Assert.False(item.SelectedByDefault);
        await c.CleanAsync([item]);
        Assert.True(File.Exists(cache));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter BrowserCleanerTests`
Expected: build FAIL (`BrowserCleaner` not defined).

- [ ] **Step 3: Implement**

`src/WindowsCleaner.Core/BrowserCleaner.cs`:

```csharp
namespace WindowsCleaner.Core;

public static class BrowserCleaner
{
    public static FileTargetCleaner CreateDefault() => Create(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));

    public static FileTargetCleaner Create(string localAppData, string appData, Func<string, bool>? isRunning = null)
    {
        var chrome = Path.Combine(localAppData, "Google", "Chrome", "User Data");
        var edge = Path.Combine(localAppData, "Microsoft", "Edge", "User Data");
        var ffLocal = Path.Combine(localAppData, "Mozilla", "Firefox", "Profiles");
        var ffRoaming = Path.Combine(appData, "Mozilla", "Firefox", "Profiles");

        var targets = new List<FileTarget>();
        targets.AddRange(Chromium("chrome", "Google Chrome", "chrome", chrome));
        targets.AddRange(Chromium("edge", "Microsoft Edge", "msedge", edge));
        targets.Add(new FileTarget("firefox.cache", "Mozilla Firefox", "Firefox cache",
            () => Subdirs(ffLocal).SelectMany(p => FileEnumerator.Files(Path.Combine(p, "cache2"))),
            true, "firefox"));
        targets.Add(new FileTarget("firefox.cookies", "Mozilla Firefox", "Firefox cookies",
            () => Subdirs(ffRoaming).SelectMany(p => Singles(p, "cookies.sqlite", "cookies.sqlite-wal")),
            false, "firefox"));

        return new FileTargetCleaner("Browsers", targets, new SafePaths([chrome, edge, ffLocal, ffRoaming]), isRunning);
    }

    private static IEnumerable<FileTarget> Chromium(string id, string display, string process, string userData)
    {
        IEnumerable<string> Profiles() => Subdirs(userData).Where(d =>
        {
            var n = Path.GetFileName(d);
            return n == "Default" || n.StartsWith("Profile ", StringComparison.OrdinalIgnoreCase);
        });

        yield return new FileTarget($"{id}.cache", display, $"{display} cache",
            () => Profiles().SelectMany(p => new[] { "Cache", "Code Cache", "GPUCache" }
                .SelectMany(n => FileEnumerator.Files(Path.Combine(p, n)))),
            true, process);
        yield return new FileTarget($"{id}.cookies", display, $"{display} cookies",
            () => Profiles().SelectMany(p => Singles(p, @"Network\Cookies", @"Network\Cookies-journal", "Cookies", "Cookies-journal")),
            false, process);
        yield return new FileTarget($"{id}.history", display, $"{display} history",
            () => Profiles().SelectMany(p => Singles(p, "History", "History-journal")),
            false, process);
    }

    private static IEnumerable<string> Subdirs(string dir) =>
        Directory.Exists(dir) ? Directory.GetDirectories(dir) : [];

    private static IEnumerable<string> Singles(string dir, params string[] names) =>
        names.SelectMany(n => FileEnumerator.Single(Path.Combine(dir, n)));
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test`
Expected: all tests PASS.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: add BrowserCleaner for Chrome, Edge and Firefox"
```

---

### Task 4: Registry abstraction and RegistryCleaner

**Files:**
- Create: `src/WindowsCleaner.Core/{IRegistryAccess,RegistryAccess,RegistryCleaner}.cs`
- Test: `tests/WindowsCleaner.Tests/{FakeRegistry,RegistryCleanerTests}.cs`

**Interfaces:**
- Consumes: `ICleaner`, `CleanItem`, `CleanResult` (Task 1).
- Produces:
  - `interface IRegistryAccess { bool KeyExists(RegistryHive, string); IReadOnlyList<string> GetSubKeyNames(RegistryHive, string); IReadOnlyList<string> GetValueNames(RegistryHive, string); object? GetValue(RegistryHive, string path, string name); void SetValue(RegistryHive, string path, string name, object value, RegistryValueKind kind); void DeleteSubKeyTree(RegistryHive, string); void DeleteValue(RegistryHive, string path, string name); void ExportKey(RegistryHive, string path, string file); void ImportFile(string file); }` (`name == ""` is the default value)
  - `RegistryAccess : IRegistryAccess` (64-bit view; export/import via `reg.exe`)
  - `RegistryCleaner(IRegistryAccess reg, Func<string,bool> pathExists, string backupRoot, Func<DateTime>? now = null) : ICleaner` with `IReadOnlyList<string> ListBackups()` (newest first) and `void Restore(string backupDir)`
  - Test helper `FakeRegistry : IRegistryAccess` with `Add(hive, path, params (string name, object value)[])`, `List<string> Exported`, `List<string> Imported`, `bool FailExport`

- [ ] **Step 1: Write the test helper and failing tests**

`tests/WindowsCleaner.Tests/FakeRegistry.cs`:

```csharp
using Microsoft.Win32;
using WindowsCleaner.Core;

namespace WindowsCleaner.Tests;

public sealed class FakeRegistry : IRegistryAccess
{
    private readonly Dictionary<string, Dictionary<string, object>> _keys = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Exported { get; } = [];
    public List<string> Imported { get; } = [];
    public bool FailExport { get; set; }

    private static string K(RegistryHive h, string p) => $"{h}\\{p}";

    public void Add(RegistryHive h, string path, params (string name, object value)[] values)
    {
        var d = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        foreach (var (n, v) in values) d[n] = v;
        _keys[K(h, path)] = d;
    }

    public bool KeyExists(RegistryHive h, string path)
    {
        var k = K(h, path);
        return _keys.ContainsKey(k) || _keys.Keys.Any(x => x.StartsWith(k + "\\", StringComparison.OrdinalIgnoreCase));
    }

    public IReadOnlyList<string> GetSubKeyNames(RegistryHive h, string path)
    {
        var prefix = K(h, path) + "\\";
        return _keys.Keys.Where(x => x.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(x => x[prefix.Length..].Split('\\')[0]).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public IReadOnlyList<string> GetValueNames(RegistryHive h, string path) =>
        _keys.TryGetValue(K(h, path), out var d) ? d.Keys.ToList() : [];

    public object? GetValue(RegistryHive h, string path, string name) =>
        _keys.TryGetValue(K(h, path), out var d) && d.TryGetValue(name, out var v) ? v : null;

    public void SetValue(RegistryHive h, string path, string name, object value, RegistryValueKind kind)
    {
        if (!_keys.TryGetValue(K(h, path), out var d)) _keys[K(h, path)] = d = new(StringComparer.OrdinalIgnoreCase);
        d[name] = value;
    }

    public void DeleteSubKeyTree(RegistryHive h, string path)
    {
        var k = K(h, path);
        foreach (var key in _keys.Keys.Where(x => x.Equals(k, StringComparison.OrdinalIgnoreCase) ||
                                                  x.StartsWith(k + "\\", StringComparison.OrdinalIgnoreCase)).ToList())
            _keys.Remove(key);
    }

    public void DeleteValue(RegistryHive h, string path, string name)
    {
        if (_keys.TryGetValue(K(h, path), out var d)) d.Remove(name);
    }

    public void ExportKey(RegistryHive h, string path, string file)
    {
        if (FailExport) throw new IOException("export failed");
        File.WriteAllText(file, "Windows Registry Editor Version 5.00");
        Exported.Add(K(h, path));
    }

    public void ImportFile(string file) => Imported.Add(file);
}
```

`tests/WindowsCleaner.Tests/RegistryCleanerTests.cs`:

```csharp
using static Microsoft.Win32.RegistryHive;
using WindowsCleaner.Core;

namespace WindowsCleaner.Tests;

public class RegistryCleanerTests
{
    private const string Uninstall = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
    private const string SharedDlls = @"SOFTWARE\Microsoft\Windows\CurrentVersion\SharedDLLs";
    private const string AppPaths = @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths";

    private static RegistryCleaner Make(FakeRegistry reg, TempDir backups, Func<string, bool>? exists = null) =>
        new(reg, exists ?? (p => p.StartsWith(@"C:\Here")), backups.Path, () => new DateTime(2026, 10, 5, 12, 0, 0));

    private static void AddOrphanUninstall(FakeRegistry reg) =>
        reg.Add(LocalMachine, Uninstall + @"\Gone",
            ("DisplayName", "Gone App"), ("UninstallString", "\"C:\\Gone\\unins.exe\" /S"));

    [Fact]
    public async Task Scan_flags_uninstall_entry_with_missing_uninstaller_only()
    {
        var reg = new FakeRegistry();
        using var b = new TempDir();
        AddOrphanUninstall(reg);
        reg.Add(LocalMachine, Uninstall + @"\Here",
            ("DisplayName", "Here App"), ("UninstallString", @"C:\Here\unins.exe /S"));
        reg.Add(LocalMachine, Uninstall + @"\Msi",
            ("DisplayName", "Msi App"), ("UninstallString", "MsiExec.exe /I{GUID}"));
        var item = Assert.Single(await Make(reg, b).ScanAsync());
        Assert.Contains("Gone App", item.Description);
    }

    [Fact]
    public async Task Scan_flags_missing_shared_dll_stale_app_path_and_dead_extension()
    {
        var reg = new FakeRegistry();
        using var b = new TempDir();
        reg.Add(LocalMachine, SharedDlls, (@"C:\Gone\a.dll", 1), (@"C:\Here\b.dll", 1));
        reg.Add(LocalMachine, AppPaths + @"\gone.exe", ("", @"C:\Gone\gone.exe"));
        reg.Add(LocalMachine, AppPaths + @"\here.exe", ("", @"C:\Here\here.exe"));
        reg.Add(CurrentUser, @"Software\Classes\.xyz", ("", "xyzfile"));
        reg.Add(CurrentUser, @"Software\Classes\.abc", ("", "abcfile"));
        reg.Add(CurrentUser, @"Software\Classes\abcfile");
        var items = await Make(reg, b).ScanAsync();
        Assert.Equal(3, items.Count);
        Assert.Equal(["App Path", "File extension", "Shared DLL"], items.Select(i => i.Category).Order().ToArray());
    }

    [Fact]
    public async Task Clean_backs_up_then_deletes()
    {
        var reg = new FakeRegistry();
        using var b = new TempDir();
        AddOrphanUninstall(reg);
        var c = Make(reg, b);
        var r = await c.CleanAsync(await c.ScanAsync());
        Assert.False(reg.KeyExists(LocalMachine, Uninstall + @"\Gone"));
        Assert.Equal(1, r.Deleted);
        Assert.Single(reg.Exported);
        Assert.True(File.Exists(Path.Combine(b.Path, "20261005-120000", "001.reg")));
    }

    [Fact]
    public async Task Clean_aborts_without_deleting_when_backup_fails()
    {
        var reg = new FakeRegistry { FailExport = true };
        using var b = new TempDir();
        AddOrphanUninstall(reg);
        var c = Make(reg, b);
        var r = await c.CleanAsync(await c.ScanAsync());
        Assert.True(reg.KeyExists(LocalMachine, Uninstall + @"\Gone"));
        Assert.Equal(0, r.Deleted);
        Assert.NotEmpty(r.Errors);
    }

    [Fact]
    public async Task Value_items_are_deleted_and_restore_imports_every_reg_file()
    {
        var reg = new FakeRegistry();
        using var b = new TempDir();
        reg.Add(LocalMachine, SharedDlls, (@"C:\Gone\a.dll", 1), (@"C:\Here\b.dll", 1));
        var c = Make(reg, b);
        await c.CleanAsync(await c.ScanAsync());
        Assert.Equal(["C:\\Here\\b.dll"], reg.GetValueNames(LocalMachine, SharedDlls));

        var backup = Assert.Single(c.ListBackups());
        c.Restore(backup);
        Assert.Single(reg.Imported);
        Assert.EndsWith("001.reg", reg.Imported[0]);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter RegistryCleanerTests`
Expected: build FAIL (`IRegistryAccess`, `RegistryCleaner` not defined).

- [ ] **Step 3: Implement**

`src/WindowsCleaner.Core/IRegistryAccess.cs`:

```csharp
using Microsoft.Win32;

namespace WindowsCleaner.Core;

/// <summary>Registry operations the cleaners need. <c>name == ""</c> addresses a key's default value.</summary>
public interface IRegistryAccess
{
    bool KeyExists(RegistryHive hive, string path);
    IReadOnlyList<string> GetSubKeyNames(RegistryHive hive, string path);
    IReadOnlyList<string> GetValueNames(RegistryHive hive, string path);
    object? GetValue(RegistryHive hive, string path, string name);
    void SetValue(RegistryHive hive, string path, string name, object value, RegistryValueKind kind);
    void DeleteSubKeyTree(RegistryHive hive, string path);
    void DeleteValue(RegistryHive hive, string path, string name);
    void ExportKey(RegistryHive hive, string path, string file);
    void ImportFile(string file);
}
```

`src/WindowsCleaner.Core/RegistryAccess.cs`:

```csharp
using System.Diagnostics;
using Microsoft.Win32;

namespace WindowsCleaner.Core;

public sealed class RegistryAccess : IRegistryAccess
{
    private static RegistryKey Base(RegistryHive h) => RegistryKey.OpenBaseKey(h, RegistryView.Registry64);

    public bool KeyExists(RegistryHive hive, string path)
    {
        using var b = Base(hive);
        using var k = b.OpenSubKey(path);
        return k is not null;
    }

    public IReadOnlyList<string> GetSubKeyNames(RegistryHive hive, string path)
    {
        using var b = Base(hive);
        using var k = b.OpenSubKey(path);
        return k?.GetSubKeyNames() ?? [];
    }

    public IReadOnlyList<string> GetValueNames(RegistryHive hive, string path)
    {
        using var b = Base(hive);
        using var k = b.OpenSubKey(path);
        return k?.GetValueNames() ?? [];
    }

    public object? GetValue(RegistryHive hive, string path, string name)
    {
        using var b = Base(hive);
        using var k = b.OpenSubKey(path);
        return k?.GetValue(name);
    }

    public void SetValue(RegistryHive hive, string path, string name, object value, RegistryValueKind kind)
    {
        using var b = Base(hive);
        using var k = b.CreateSubKey(path);
        k.SetValue(name, value, kind);
    }

    public void DeleteSubKeyTree(RegistryHive hive, string path)
    {
        using var b = Base(hive);
        b.DeleteSubKeyTree(path, throwOnMissingSubKey: false);
    }

    public void DeleteValue(RegistryHive hive, string path, string name)
    {
        using var b = Base(hive);
        using var k = b.OpenSubKey(path, writable: true);
        k?.DeleteValue(name, throwOnMissingValue: false);
    }

    public void ExportKey(RegistryHive hive, string path, string file) =>
        RunReg("export", $"{Short(hive)}\\{path}", file, "/y");

    public void ImportFile(string file) => RunReg("import", file);

    private static string Short(RegistryHive h) => h switch
    {
        RegistryHive.LocalMachine => "HKLM",
        RegistryHive.CurrentUser => "HKCU",
        RegistryHive.ClassesRoot => "HKCR",
        _ => throw new NotSupportedException(h.ToString()),
    };

    private static void RunReg(params string[] args)
    {
        var psi = new ProcessStartInfo("reg.exe")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var err = p.StandardError.ReadToEnd();
        p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0) throw new InvalidOperationException($"reg.exe {args[0]} failed: {err.Trim()}");
    }
}
```

`src/WindowsCleaner.Core/RegistryCleaner.cs`:

```csharp
using Microsoft.Win32;

namespace WindowsCleaner.Core;

public sealed class RegistryCleaner : ICleaner
{
    private const char Sep = '\t';
    private const string CurrentVersion = @"SOFTWARE\Microsoft\Windows\CurrentVersion";
    private static readonly string[] UninstallPaths =
        [CurrentVersion + @"\Uninstall", @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"];
    private const string MuiCache = @"Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\MuiCache";

    private readonly IRegistryAccess _reg;
    private readonly Func<string, bool> _exists;
    private readonly string _backupRoot;
    private readonly Func<DateTime> _now;

    public RegistryCleaner(IRegistryAccess reg, Func<string, bool> pathExists, string backupRoot, Func<DateTime>? now = null)
    {
        _reg = reg;
        _exists = pathExists;
        _backupRoot = backupRoot;
        _now = now ?? (() => DateTime.Now);
    }

    public string Name => "Registry";

    // ---- scan -------------------------------------------------------------------------------

    public Task<IReadOnlyList<CleanItem>> ScanAsync(CancellationToken ct = default) =>
        Task.Run(() => (IReadOnlyList<CleanItem>)Scan(ct).ToList(), ct);

    private IEnumerable<CleanItem> Scan(CancellationToken ct)
    {
        foreach (var i in ScanUninstall()) { ct.ThrowIfCancellationRequested(); yield return i; }
        foreach (var i in ScanSharedDlls()) { ct.ThrowIfCancellationRequested(); yield return i; }
        foreach (var i in ScanAppPaths()) { ct.ThrowIfCancellationRequested(); yield return i; }
        foreach (var i in ScanMuiCache()) { ct.ThrowIfCancellationRequested(); yield return i; }
        foreach (var i in ScanExtensions()) { ct.ThrowIfCancellationRequested(); yield return i; }
    }

    private IEnumerable<CleanItem> ScanUninstall()
    {
        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        foreach (var basePath in UninstallPaths)
        foreach (var sub in _reg.GetSubKeyNames(hive, basePath))
        {
            var path = $"{basePath}\\{sub}";
            if (_reg.GetValue(hive, path, "DisplayName") is not string name || name.Length == 0) continue;
            if (_reg.GetValue(hive, path, "SystemComponent") is 1) continue;
            if (_reg.GetValue(hive, path, "UninstallString") is not string cmd) continue;
            var exe = ExtractExe(cmd);
            if (exe is null || !IsRootedFile(exe) || Path.GetFileNameWithoutExtension(exe).Equals("msiexec", StringComparison.OrdinalIgnoreCase)) continue;
            if (_exists(exe)) continue;
            var loc = _reg.GetValue(hive, path, "InstallLocation") as string;
            if (!string.IsNullOrWhiteSpace(loc) && _exists(loc)) continue;
            yield return KeyItem("Uninstall entry", $"{name} (uninstaller missing)", hive, path);
        }
    }

    private IEnumerable<CleanItem> ScanSharedDlls()
    {
        var path = CurrentVersion + @"\SharedDLLs";
        foreach (var n in _reg.GetValueNames(RegistryHive.LocalMachine, path))
            if (IsRootedFile(n) && !_exists(n))
                yield return ValueItem("Shared DLL", n, RegistryHive.LocalMachine, path, n);
    }

    private IEnumerable<CleanItem> ScanAppPaths()
    {
        var basePath = CurrentVersion + @"\App Paths";
        foreach (var sub in _reg.GetSubKeyNames(RegistryHive.LocalMachine, basePath))
        {
            var path = $"{basePath}\\{sub}";
            if (_reg.GetValue(RegistryHive.LocalMachine, path, "") is not string target) continue;
            target = target.Trim('"');
            if (IsRootedFile(target) && !_exists(target))
                yield return KeyItem("App Path", $"{sub} → {target}", RegistryHive.LocalMachine, path);
        }
    }

    private IEnumerable<CleanItem> ScanMuiCache()
    {
        foreach (var n in _reg.GetValueNames(RegistryHive.CurrentUser, MuiCache))
        {
            var file = n;
            foreach (var suffix in new[] { ".FriendlyAppName", ".ApplicationCompany" })
                if (file.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) file = file[..^suffix.Length];
            if (IsRootedFile(file) && !_exists(file))
                yield return ValueItem("MUI cache", n, RegistryHive.CurrentUser, MuiCache, n);
        }
    }

    private IEnumerable<CleanItem> ScanExtensions()
    {
        const string classes = @"Software\Classes";
        foreach (var sub in _reg.GetSubKeyNames(RegistryHive.CurrentUser, classes))
        {
            if (!sub.StartsWith('.')) continue;
            if (_reg.GetValue(RegistryHive.CurrentUser, $"{classes}\\{sub}", "") is not string progId || progId.Length == 0) continue;
            if (_reg.KeyExists(RegistryHive.CurrentUser, $"{classes}\\{progId}") ||
                _reg.KeyExists(RegistryHive.LocalMachine, $"Software\\Classes\\{progId}")) continue;
            yield return KeyItem("File extension", $"{sub} → missing handler {progId}", RegistryHive.CurrentUser, $"{classes}\\{sub}");
        }
    }

    // ---- clean / restore --------------------------------------------------------------------

    public Task<CleanResult> CleanAsync(IEnumerable<CleanItem> selected, IProgress<string>? progress = null, CancellationToken ct = default) =>
        Task.Run(() =>
        {
            var refs = selected.Select(i => Parse(i.Id)).Where(r => r is not null).Select(r => r!).ToList();
            if (refs.Count == 0) return CleanResult.Empty;

            var dir = Path.Combine(_backupRoot, _now().ToString("yyyyMMdd-HHmmss"));
            try
            {
                progress?.Report("Backing up registry keys");
                Directory.CreateDirectory(dir);
                var n = 0;
                foreach (var key in refs.Select(r => (r.Hive, r.Path)).Distinct())
                    _reg.ExportKey(key.Hive, key.Path, Path.Combine(dir, $"{++n:D3}.reg"));
            }
            catch (Exception e)
            {
                return new CleanResult(0, 0, refs.Count, [$"Backup failed, nothing was changed: {e.Message}"]);
            }

            int deleted = 0, skipped = 0;
            var errors = new List<string>();
            foreach (var r in refs)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    if (r.ValueName is null) _reg.DeleteSubKeyTree(r.Hive, r.Path);
                    else _reg.DeleteValue(r.Hive, r.Path, r.ValueName);
                    deleted++;
                }
                catch (Exception e)
                {
                    skipped++;
                    errors.Add($"{r.Path}: {e.Message}");
                }
            }
            return new CleanResult(0, deleted, skipped, errors);
        }, ct);

    public IReadOnlyList<string> ListBackups() =>
        Directory.Exists(_backupRoot)
            ? Directory.GetDirectories(_backupRoot).OrderByDescending(d => d, StringComparer.Ordinal).ToList()
            : [];

    public void Restore(string backupDir)
    {
        foreach (var file in Directory.GetFiles(backupDir, "*.reg").Order()) _reg.ImportFile(file);
    }

    // ---- helpers ----------------------------------------------------------------------------

    private sealed record Ref(RegistryHive Hive, string Path, string? ValueName);

    private static CleanItem KeyItem(string category, string description, RegistryHive hive, string path) =>
        new($"K{Sep}{hive}{Sep}{path}", category, description, 0);

    private static CleanItem ValueItem(string category, string description, RegistryHive hive, string path, string value) =>
        new($"V{Sep}{hive}{Sep}{path}{Sep}{value}", category, description, 0);

    private static Ref? Parse(string id)
    {
        var p = id.Split(Sep);
        if (p.Length >= 3 && Enum.TryParse<RegistryHive>(p[1], out var hive))
        {
            if (p[0] == "K" && p.Length == 3) return new Ref(hive, p[2], null);
            if (p[0] == "V" && p.Length == 4) return new Ref(hive, p[2], p[3]);
        }
        return null;
    }

    private static bool IsRootedFile(string s) => s.Length > 3 && s[1] == ':' && s[2] == '\\';

    private static string? ExtractExe(string command)
    {
        command = command.Trim();
        if (command.StartsWith('"'))
        {
            var end = command.IndexOf('"', 1);
            return end > 1 ? command[1..end] : null;
        }
        var i = command.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        return i > 0 ? command[..(i + 4)] : command.Split(' ')[0];
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test`
Expected: all tests PASS.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: add registry abstraction and RegistryCleaner with backups"
```

---

### Task 5: Startup manager

**Files:**
- Create: `src/WindowsCleaner.Core/Startup/{StartupEntry,StartupApproval,RunKeyStartupSource,StartupFolderSource,TaskStartupSource,StartupManager}.cs`
- Test: `tests/WindowsCleaner.Tests/StartupTests.cs`

**Interfaces:**
- Consumes: `IRegistryAccess` (Task 4), `FakeRegistry`, `TempDir`.
- Produces:
  - `record StartupEntry(string Id, string Name, string Command, string Source, bool Enabled)`
  - `interface IStartupSource { string Name { get; } IReadOnlyList<StartupEntry> List(); void SetEnabled(StartupEntry entry, bool enabled); }`
  - `RunKeyStartupSource(IRegistryAccess)` (`Name == "Run key"`), `StartupFolderSource(IRegistryAccess, string userFolder, string commonFolder)` (`Name == "Startup folder"`), `TaskStartupSource()` (`Name == "Scheduled task"`)
  - `StartupManager(IEnumerable<IStartupSource>)` with `IReadOnlyList<StartupEntry> List()` and `void SetEnabled(StartupEntry, bool)`; `static StartupManager CreateDefault(IRegistryAccess)`

- [ ] **Step 1: Write the failing tests**

`tests/WindowsCleaner.Tests/StartupTests.cs`:

```csharp
using Microsoft.Win32;
using WindowsCleaner.Core;
using static Microsoft.Win32.RegistryHive;

namespace WindowsCleaner.Tests;

public class StartupTests
{
    private const string Run = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedRun = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string ApprovedFolder = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder";

    [Fact]
    public void RunKey_lists_entries_with_enabled_state_and_toggles_via_StartupApproved()
    {
        var reg = new FakeRegistry();
        reg.Add(CurrentUser, Run, ("Foo", @"C:\foo.exe"), ("Bar", @"C:\bar.exe"));
        reg.Add(CurrentUser, ApprovedRun, ("Bar", new byte[] { 3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }));
        var src = new RunKeyStartupSource(reg);

        var list = src.List();
        Assert.True(list.Single(e => e.Name == "Foo").Enabled);
        Assert.False(list.Single(e => e.Name == "Bar").Enabled);
        Assert.Equal(@"C:\foo.exe", list.Single(e => e.Name == "Foo").Command);

        src.SetEnabled(list.Single(e => e.Name == "Foo"), false);
        Assert.Equal(3, ((byte[])reg.GetValue(CurrentUser, ApprovedRun, "Foo")!)[0]);
        Assert.False(src.List().Single(e => e.Name == "Foo").Enabled);

        src.SetEnabled(src.List().Single(e => e.Name == "Foo"), true);
        Assert.Equal(2, ((byte[])reg.GetValue(CurrentUser, ApprovedRun, "Foo")!)[0]);
        Assert.True(src.List().Single(e => e.Name == "Foo").Enabled);
    }

    [Fact]
    public void StartupFolder_lists_files_except_desktop_ini_and_toggles()
    {
        using var t = new TempDir();
        t.Write(@"user\app.lnk");
        t.Write(@"user\desktop.ini");
        var reg = new FakeRegistry();
        var src = new StartupFolderSource(reg, Path.Combine(t.Path, "user"), Path.Combine(t.Path, "common"));

        var entry = Assert.Single(src.List());
        Assert.Equal("app.lnk", entry.Name);
        Assert.True(entry.Enabled);

        src.SetEnabled(entry, false);
        Assert.Equal(3, ((byte[])reg.GetValue(CurrentUser, ApprovedFolder, "app.lnk")!)[0]);
        Assert.False(Assert.Single(src.List()).Enabled);
    }

    [Fact]
    public void Manager_routes_toggle_to_the_owning_source()
    {
        var reg = new FakeRegistry();
        reg.Add(CurrentUser, Run, ("Foo", @"C:\foo.exe"));
        var mgr = new StartupManager([new RunKeyStartupSource(reg)]);
        var e = Assert.Single(mgr.List());
        mgr.SetEnabled(e, false);
        Assert.False(Assert.Single(mgr.List()).Enabled);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter StartupTests`
Expected: build FAIL (types not defined).

- [ ] **Step 3: Implement**

`src/WindowsCleaner.Core/Startup/StartupEntry.cs`:

```csharp
namespace WindowsCleaner.Core;

public sealed record StartupEntry(string Id, string Name, string Command, string Source, bool Enabled);

public interface IStartupSource
{
    string Name { get; }
    IReadOnlyList<StartupEntry> List();
    void SetEnabled(StartupEntry entry, bool enabled);
}
```

`src/WindowsCleaner.Core/Startup/StartupApproval.cs`:

```csharp
using Microsoft.Win32;

namespace WindowsCleaner.Core;

/// <summary>Reads/writes the Explorer "StartupApproved" binary flags (first byte even = enabled, odd = disabled).
/// This is the same reversible switch Task Manager uses; nothing is deleted.</summary>
internal static class StartupApproval
{
    public const string BasePath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\";

    public static bool IsEnabled(IRegistryAccess reg, RegistryHive hive, string approvedPath, string name) =>
        reg.GetValue(hive, approvedPath, name) is not byte[] b || b.Length == 0 || (b[0] & 1) == 0;

    public static void Set(IRegistryAccess reg, RegistryHive hive, string approvedPath, string name, bool enabled)
    {
        var data = new byte[12];
        data[0] = enabled ? (byte)2 : (byte)3;
        if (!enabled) BitConverter.GetBytes(DateTime.UtcNow.ToFileTimeUtc()).CopyTo(data, 4);
        reg.SetValue(hive, approvedPath, name, data, RegistryValueKind.Binary);
    }
}
```

`src/WindowsCleaner.Core/Startup/RunKeyStartupSource.cs`:

```csharp
using Microsoft.Win32;

namespace WindowsCleaner.Core;

public sealed class RunKeyStartupSource(IRegistryAccess reg) : IStartupSource
{
    public const string SourceName = "Run key";
    private const char Sep = '\t';
    private const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Run32Path = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run";

    private static readonly (RegistryHive Hive, string Path)[] Locations =
        [(RegistryHive.CurrentUser, RunPath), (RegistryHive.LocalMachine, RunPath), (RegistryHive.LocalMachine, Run32Path)];

    public string Name => SourceName;

    private static string Approved(string runPath) =>
        StartupApproval.BasePath + (runPath == Run32Path ? "Run32" : "Run");

    public IReadOnlyList<StartupEntry> List()
    {
        var result = new List<StartupEntry>();
        foreach (var (hive, path) in Locations)
            foreach (var name in reg.GetValueNames(hive, path))
            {
                if (name.Length == 0) continue;
                var command = reg.GetValue(hive, path, name) as string ?? "";
                result.Add(new StartupEntry($"{hive}{Sep}{path}{Sep}{name}", name, command, SourceName,
                    StartupApproval.IsEnabled(reg, hive, Approved(path), name)));
            }
        return result;
    }

    public void SetEnabled(StartupEntry entry, bool enabled)
    {
        var p = entry.Id.Split(Sep);
        StartupApproval.Set(reg, Enum.Parse<RegistryHive>(p[0]), Approved(p[1]), p[2], enabled);
    }
}
```

`src/WindowsCleaner.Core/Startup/StartupFolderSource.cs`:

```csharp
using Microsoft.Win32;

namespace WindowsCleaner.Core;

public sealed class StartupFolderSource(IRegistryAccess reg, string userFolder, string commonFolder) : IStartupSource
{
    public const string SourceName = "Startup folder";
    private const char Sep = '\t';
    private const string Approved = StartupApproval.BasePath + "StartupFolder";

    public string Name => SourceName;

    public IReadOnlyList<StartupEntry> List()
    {
        var result = new List<StartupEntry>();
        foreach (var (hive, folder) in new[] { (RegistryHive.CurrentUser, userFolder), (RegistryHive.LocalMachine, commonFolder) })
        {
            if (!Directory.Exists(folder)) continue;
            foreach (var file in Directory.GetFiles(folder))
            {
                var name = Path.GetFileName(file);
                if (name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                result.Add(new StartupEntry($"{hive}{Sep}{file}", name, file, SourceName,
                    StartupApproval.IsEnabled(reg, hive, Approved, name)));
            }
        }
        return result;
    }

    public void SetEnabled(StartupEntry entry, bool enabled)
    {
        var p = entry.Id.Split(Sep);
        StartupApproval.Set(reg, Enum.Parse<RegistryHive>(p[0]), Approved, Path.GetFileName(p[1]), enabled);
    }
}
```

`src/WindowsCleaner.Core/Startup/TaskStartupSource.cs`:

```csharp
using Microsoft.Win32.TaskScheduler;

namespace WindowsCleaner.Core;

/// <summary>Non-Microsoft scheduled tasks that run at user logon. Not unit-tested (needs the real Task Scheduler).</summary>
public sealed class TaskStartupSource : IStartupSource
{
    public const string SourceName = "Scheduled task";

    public string Name => SourceName;

    public IReadOnlyList<StartupEntry> List()
    {
        var result = new List<StartupEntry>();
        using var ts = new TaskService();
        foreach (var t in ts.AllTasks)
        {
            try
            {
                if (t.Path.StartsWith(@"\Microsoft\", StringComparison.OrdinalIgnoreCase)) continue;
                if (!t.Definition.Triggers.Any(x => x.TriggerType == TaskTriggerType.Logon)) continue;
                var command = string.Join("; ", t.Definition.Actions.Select(a => a.ToString()));
                result.Add(new StartupEntry(t.Path, t.Name, command, SourceName, t.Enabled));
            }
            catch (Exception) { /* unreadable task: skip */ }
        }
        return result;
    }

    public void SetEnabled(StartupEntry entry, bool enabled)
    {
        using var ts = new TaskService();
        var task = ts.GetTask(entry.Id) ?? throw new InvalidOperationException($"Task not found: {entry.Id}");
        task.Enabled = enabled;
    }
}
```

`src/WindowsCleaner.Core/Startup/StartupManager.cs`:

```csharp
namespace WindowsCleaner.Core;

public sealed class StartupManager
{
    private readonly IReadOnlyList<IStartupSource> _sources;

    public StartupManager(IEnumerable<IStartupSource> sources) => _sources = sources.ToList();

    public static StartupManager CreateDefault(IRegistryAccess reg) => new(
    [
        new RunKeyStartupSource(reg),
        new StartupFolderSource(reg,
            Environment.GetFolderPath(Environment.SpecialFolder.Startup),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup)),
        new TaskStartupSource(),
    ]);

    public IReadOnlyList<StartupEntry> List() =>
        _sources.SelectMany(s => s.List()).OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();

    public void SetEnabled(StartupEntry entry, bool enabled) =>
        _sources.Single(s => s.Name == entry.Source).SetEnabled(entry, enabled);
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test`
Expected: all tests PASS. If `TaskStartupSource` fails to compile because the `TaskScheduler` package API differs (`TaskService.AllTasks`, `Task.Definition.Triggers`, `TaskTriggerType.Logon`, `Task.Enabled`, `TaskService.GetTask`), check the installed package version's docs and adjust only that file.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: add startup manager (Run keys, Startup folders, scheduled tasks)"
```

---

### Task 6: AppManager

**Files:**
- Create: `src/WindowsCleaner.Core/AppManager.cs`
- Test: `tests/WindowsCleaner.Tests/AppManagerTests.cs`

**Interfaces:**
- Consumes: `IRegistryAccess`, `FakeRegistry` (Task 4).
- Produces: `record InstalledApp(string Name, string Publisher, string Version, string InstallDate, long SizeBytes, string UninstallString)`; `AppManager(IRegistryAccess)` with `IReadOnlyList<InstalledApp> List()` (sorted by name, de-duplicated by name+version) and `void Uninstall(InstalledApp)`.

- [ ] **Step 1: Write the failing test**

`tests/WindowsCleaner.Tests/AppManagerTests.cs`:

```csharp
using WindowsCleaner.Core;
using static Microsoft.Win32.RegistryHive;

namespace WindowsCleaner.Tests;

public class AppManagerTests
{
    private const string Uninstall = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    [Fact]
    public void List_returns_visible_apps_sorted_and_deduplicated()
    {
        var reg = new FakeRegistry();
        reg.Add(LocalMachine, Uninstall + @"\B",
            ("DisplayName", "Beta"), ("Publisher", "Acme"), ("DisplayVersion", "2.0"),
            ("EstimatedSize", 2048), ("UninstallString", @"C:\b\unins.exe"));
        reg.Add(LocalMachine, Uninstall + @"\A",
            ("DisplayName", "Alpha"), ("UninstallString", @"C:\a\unins.exe"));
        reg.Add(CurrentUser, Uninstall + @"\A2",
            ("DisplayName", "Alpha"), ("UninstallString", @"C:\a\unins.exe"));
        reg.Add(LocalMachine, Uninstall + @"\Sys",
            ("DisplayName", "Hidden"), ("SystemComponent", 1), ("UninstallString", "x"));
        reg.Add(LocalMachine, Uninstall + @"\Patch",
            ("DisplayName", "Patch"), ("ParentKeyName", "A"), ("UninstallString", "x"));
        reg.Add(LocalMachine, Uninstall + @"\NoName", ("UninstallString", "x"));
        reg.Add(LocalMachine, Uninstall + @"\NoUninstaller", ("DisplayName", "Stuck"));

        var apps = new AppManager(reg).List();

        Assert.Equal(["Alpha", "Beta"], apps.Select(a => a.Name).ToArray());
        var beta = apps.Single(a => a.Name == "Beta");
        Assert.Equal("Acme", beta.Publisher);
        Assert.Equal("2.0", beta.Version);
        Assert.Equal(2048L * 1024, beta.SizeBytes);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter AppManagerTests`
Expected: build FAIL (`AppManager` not defined).

- [ ] **Step 3: Implement**

`src/WindowsCleaner.Core/AppManager.cs`:

```csharp
using System.Diagnostics;
using Microsoft.Win32;

namespace WindowsCleaner.Core;

public sealed record InstalledApp(string Name, string Publisher, string Version, string InstallDate, long SizeBytes, string UninstallString);

public sealed class AppManager(IRegistryAccess reg)
{
    private static readonly string[] Paths =
    [
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
        @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
    ];

    public IReadOnlyList<InstalledApp> List()
    {
        var apps = new List<InstalledApp>();
        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        foreach (var basePath in Paths)
        foreach (var sub in reg.GetSubKeyNames(hive, basePath))
        {
            var p = $"{basePath}\\{sub}";
            string S(string n) => reg.GetValue(hive, p, n) as string ?? "";
            var name = S("DisplayName");
            var uninstall = S("UninstallString");
            if (name.Length == 0 || uninstall.Length == 0) continue;
            if (reg.GetValue(hive, p, "SystemComponent") is 1) continue;
            if (S("ParentKeyName").Length > 0) continue;
            var kb = reg.GetValue(hive, p, "EstimatedSize") is int i ? i : 0;
            apps.Add(new InstalledApp(name, S("Publisher"), S("DisplayVersion"), S("InstallDate"), kb * 1024L, uninstall));
        }
        return apps
            .GroupBy(a => (a.Name, a.Version))
            .Select(g => g.First())
            .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Launches the app's own uninstaller. The extra outer quotes stop cmd.exe from stripping the
    /// first and last quote of a quoted path.</summary>
    public void Uninstall(InstalledApp app) =>
        Process.Start(new ProcessStartInfo("cmd.exe")
        {
            Arguments = $"/c \"{app.UninstallString}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
        });
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test`
Expected: all tests PASS.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: add AppManager for listing and uninstalling apps"
```

---

### Task 7: WinForms shell, elevation manifest, and Cleaner/Registry pages

**Files:**
- Create: `src/WindowsCleaner.App/{app.manifest,MainForm.cs,CleanerPage.cs}`
- Modify: `src/WindowsCleaner.App/Program.cs`, `src/WindowsCleaner.App/WindowsCleaner.App.csproj`
- Create temporary stubs replaced in Task 8: none (Task 7's `MainForm` only wires Cleaner + Registry; Task 8 adds the other pages).

**Interfaces:**
- Consumes: `ICleaner`, `JunkCleaner.CreateDefault`, `BrowserCleaner.CreateDefault`, `RegistryCleaner`, `RegistryAccess`, `AppSettings`, `AppPaths`, `SizeFormat`.
- Produces: `CleanerPage(IReadOnlyList<ICleaner> cleaners, string runLabel, string confirmNote) : UserControl`; `MainForm.AddPage(string title, Control page)`.

UI is verified by build plus a manual smoke test (no UI unit tests; logic lives in Core).

- [ ] **Step 1: Add the manifest**

`src/WindowsCleaner.App/app.manifest`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<assembly manifestVersion="1.0" xmlns="urn:schemas-microsoft-com:asm.v1">
  <trustInfo xmlns="urn:schemas-microsoft-com:asm.v2">
    <security>
      <requestedPrivileges xmlns="urn:schemas-microsoft-com:asm.v3">
        <requestedExecutionLevel level="requireAdministrator" uiAccess="false" />
      </requestedPrivileges>
    </security>
  </trustInfo>
  <compatibility xmlns="urn:schemas-microsoft-com:compatibility.v1">
    <application>
      <supportedOS Id="{8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a}" />
    </application>
  </compatibility>
</assembly>
```

In `WindowsCleaner.App.csproj` add `<ApplicationManifest>app.manifest</ApplicationManifest>` inside the first `<PropertyGroup>`.

- [ ] **Step 2: Program entry point**

`src/WindowsCleaner.App/Program.cs`:

```csharp
namespace WindowsCleaner.App;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}
```

- [ ] **Step 3: CleanerPage**

`src/WindowsCleaner.App/CleanerPage.cs`:

```csharp
using WindowsCleaner.Core;

namespace WindowsCleaner.App;

/// <summary>Analyze → tick items → run. Used for both the junk/browser page and the registry page.</summary>
public sealed class CleanerPage : UserControl
{
    private sealed record Entry(ICleaner Cleaner, CleanItem Item);

    private readonly IReadOnlyList<ICleaner> _cleaners;
    private readonly string _confirmNote;
    private readonly ListView _list = new()
    {
        Dock = DockStyle.Fill, View = View.Details, CheckBoxes = true, FullRowSelect = true,
        ShowGroups = true, HeaderStyle = ColumnHeaderStyle.Nonclickable,
    };
    private readonly Button _analyze = new() { Text = "Analyze", AutoSize = true };
    private readonly Button _run = new() { AutoSize = true, Enabled = false };
    private readonly ProgressBar _progress = new() { Style = ProgressBarStyle.Marquee, Visible = false, Width = 110 };
    private readonly Label _status = new() { AutoSize = true, Padding = new Padding(8, 8, 0, 0) };

    public CleanerPage(IReadOnlyList<ICleaner> cleaners, string runLabel, string confirmNote)
    {
        _cleaners = cleaners;
        _confirmNote = confirmNote;
        _run.Text = runLabel;
        Dock = DockStyle.Fill;

        _list.Columns.Add("Item", 520);
        _list.Columns.Add("Size", 100, HorizontalAlignment.Right);
        _list.ItemChecked += (_, _) => UpdateSummary();
        _analyze.Click += OnAnalyze;
        _run.Click += OnRun;

        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(6) };
        bar.Controls.AddRange([_analyze, _run, _progress, _status]);
        Controls.Add(_list);   // Fill first, then Top, so docking gives Top its strip and Fill the rest
        Controls.Add(bar);
    }

    private void SetBusy(bool busy, string? text = null)
    {
        _analyze.Enabled = !busy;
        _run.Enabled = !busy && _list.CheckedItems.Count > 0;
        _progress.Visible = busy;
        if (text is not null) _status.Text = text;
    }

    private IEnumerable<Entry> Checked() =>
        _list.CheckedItems.Cast<ListViewItem>().Select(i => (Entry)i.Tag!);

    private void UpdateSummary()
    {
        var chosen = Checked().ToList();
        _run.Enabled = _analyze.Enabled && chosen.Count > 0;
        var total = chosen.Sum(e => e.Item.SizeBytes);
        _status.Text = chosen.Count == 0 ? "" : total > 0
            ? $"{chosen.Count} selected, {SizeFormat.Format(total)}"
            : $"{chosen.Count} selected";
    }

    private async void OnAnalyze(object? sender, EventArgs e)
    {
        SetBusy(true, "Analyzing…");
        _list.Items.Clear();
        _list.Groups.Clear();
        try
        {
            foreach (var cleaner in _cleaners)
            {
                var items = await cleaner.ScanAsync();
                _list.BeginUpdate();
                foreach (var item in items)
                {
                    var group = _list.Groups.Cast<ListViewGroup>().FirstOrDefault(g => g.Header == item.Category);
                    if (group is null) { group = new ListViewGroup(item.Category); _list.Groups.Add(group); }
                    var row = new ListViewItem(item.Description, group) { Tag = new Entry(cleaner, item) };
                    row.SubItems.Add(item.SizeBytes > 0 ? SizeFormat.Format(item.SizeBytes) : "–");
                    row.Checked = item.SelectedByDefault;
                    _list.Items.Add(row);
                }
                _list.EndUpdate();
            }
            SetBusy(false);
            UpdateSummary();
            if (_list.Items.Count == 0) _status.Text = "Nothing to clean.";
        }
        catch (Exception ex)
        {
            SetBusy(false, "Analyze failed.");
            MessageBox.Show(this, ex.Message, "WindowsCleaner", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async void OnRun(object? sender, EventArgs e)
    {
        var chosen = Checked().ToList();
        if (chosen.Count == 0) return;
        var total = chosen.Sum(x => x.Item.SizeBytes);
        var size = total > 0 ? $", {SizeFormat.Format(total)}" : "";
        var answer = MessageBox.Show(this,
            $"{chosen.Count} items{size}.\n\n{_confirmNote}\n\nContinue?",
            "WindowsCleaner", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (answer != DialogResult.Yes) return;

        SetBusy(true, "Cleaning…");
        try
        {
            var progress = new Progress<string>(s => _status.Text = s);
            var result = CleanResult.Empty;
            foreach (var g in chosen.GroupBy(x => x.Cleaner))
                result = result.Plus(await g.Key.CleanAsync(g.Select(x => x.Item), progress));

            var msg = $"Removed {result.Deleted} items, freed {SizeFormat.Format(result.BytesFreed)}.";
            if (result.Skipped > 0) msg += $"\nSkipped {result.Skipped} (in use or protected).";
            if (result.Errors.Count > 0) msg += "\n\n" + string.Join("\n", result.Errors.Take(5));
            _list.Items.Clear();
            _list.Groups.Clear();
            SetBusy(false, "Done. Analyze again to refresh.");
            MessageBox.Show(this, msg, "WindowsCleaner", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            SetBusy(false, "Clean failed.");
            MessageBox.Show(this, ex.Message, "WindowsCleaner", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
```

- [ ] **Step 4: MainForm (Cleaner + Registry pages for now)**

`src/WindowsCleaner.App/MainForm.cs`:

```csharp
using WindowsCleaner.Core;

namespace WindowsCleaner.App;

public sealed class MainForm : Form
{
    private readonly FlowLayoutPanel _nav = new()
    {
        Dock = DockStyle.Left, Width = 150, FlowDirection = FlowDirection.TopDown,
        WrapContents = false, Padding = new Padding(6),
    };
    private readonly Panel _content = new() { Dock = DockStyle.Fill };
    private readonly Dictionary<string, Control> _pages = [];

    public MainForm()
    {
        Text = "WindowsCleaner";
        Size = new Size(1000, 660);
        MinimumSize = new Size(820, 520);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9.5f);

        Controls.Add(_content);
        Controls.Add(_nav);

        var settings = AppSettings.Load();
        var reg = new RegistryAccess();
        var registryCleaner = new RegistryCleaner(reg, p => File.Exists(p) || Directory.Exists(p), AppPaths.BackupsDir);

        AddPage("Cleaner", new CleanerPage(
            [JunkCleaner.CreateDefault(settings), BrowserCleaner.CreateDefault()],
            "Run Cleaner",
            "Selected files will be permanently deleted."));
        AddPage("Registry", new CleanerPage(
            [registryCleaner],
            "Fix selected issues",
            "A .reg backup of every affected key is saved first (Settings → Restore)."));

        Show("Cleaner");
    }

    public void AddPage(string title, Control page)
    {
        _pages[title] = page;
        var button = new Button { Text = title, Width = 130, Height = 36, FlatStyle = FlatStyle.System };
        button.Click += (_, _) => Show(title);
        _nav.Controls.Add(button);
    }

    private void Show(string title)
    {
        _content.Controls.Clear();
        _content.Controls.Add(_pages[title]);
    }
}
```

- [ ] **Step 5: Build and smoke-test**

Run: `dotnet build`
Expected: Build succeeded, 0 errors.

Manual smoke test (the app requires elevation, so launch it via UAC rather than `dotnet run` from a non-elevated shell):

```powershell
Start-Process -FilePath "src\WindowsCleaner.App\bin\Debug\net10.0-windows\WindowsCleaner.exe"
```

Expected: UAC prompt, then a window with Cleaner and Registry nav. On Cleaner click **Analyze** (do NOT run the cleaner yet): grouped items with sizes appear. On Registry click **Analyze**: orphan entries (if any) appear. Close the app.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: add WinForms shell with Cleaner and Registry pages"
```

---

### Task 8: Startup, Uninstall and Settings pages

**Files:**
- Create: `src/WindowsCleaner.App/{StartupPage,UninstallPage,SettingsPage}.cs`
- Modify: `src/WindowsCleaner.App/MainForm.cs`

**Interfaces:**
- Consumes: `StartupManager.CreateDefault`, `StartupEntry`, `AppManager`, `InstalledApp`, `RegistryCleaner.ListBackups/Restore`, `AppSettings`, `AppPaths`, `SizeFormat`.
- Produces: `StartupPage(StartupManager)`, `UninstallPage(AppManager)`, `SettingsPage(AppSettings, RegistryCleaner)` (all `UserControl`).

- [ ] **Step 1: StartupPage**

`src/WindowsCleaner.App/StartupPage.cs`:

```csharp
using WindowsCleaner.Core;

namespace WindowsCleaner.App;

public sealed class StartupPage : UserControl
{
    private readonly StartupManager _manager;
    private readonly ListView _list = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false };
    private readonly Label _status = new() { AutoSize = true, Padding = new Padding(8, 8, 0, 0) };

    public StartupPage(StartupManager manager)
    {
        _manager = manager;
        Dock = DockStyle.Fill;
        _list.Columns.Add("Name", 200);
        _list.Columns.Add("Status", 80);
        _list.Columns.Add("Source", 120);
        _list.Columns.Add("Command", 420);

        var refresh = new Button { Text = "Refresh", AutoSize = true };
        var enable = new Button { Text = "Enable", AutoSize = true };
        var disable = new Button { Text = "Disable", AutoSize = true };
        refresh.Click += async (_, _) => await Reload();
        enable.Click += async (_, _) => await Toggle(true);
        disable.Click += async (_, _) => await Toggle(false);

        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(6) };
        bar.Controls.AddRange([refresh, enable, disable, _status]);
        Controls.Add(_list);
        Controls.Add(bar);
        HandleCreated += async (_, _) => await Reload();
    }

    private async Task Reload()
    {
        _status.Text = "Loading…";
        try
        {
            var entries = await Task.Run(_manager.List);
            _list.BeginUpdate();
            _list.Items.Clear();
            foreach (var e in entries)
            {
                var row = new ListViewItem(e.Name) { Tag = e };
                row.SubItems.Add(e.Enabled ? "Enabled" : "Disabled");
                row.SubItems.Add(e.Source);
                row.SubItems.Add(e.Command);
                _list.Items.Add(row);
            }
            _list.EndUpdate();
            _status.Text = $"{entries.Count} startup items";
        }
        catch (Exception ex)
        {
            _status.Text = "Failed to load.";
            MessageBox.Show(this, ex.Message, "WindowsCleaner", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task Toggle(bool enable)
    {
        if (_list.SelectedItems.Count == 0) return;
        var entry = (StartupEntry)_list.SelectedItems[0].Tag!;
        try { await Task.Run(() => _manager.SetEnabled(entry, enable)); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "WindowsCleaner", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        await Reload();
    }
}
```

- [ ] **Step 2: UninstallPage**

`src/WindowsCleaner.App/UninstallPage.cs`:

```csharp
using WindowsCleaner.Core;

namespace WindowsCleaner.App;

public sealed class UninstallPage : UserControl
{
    private readonly AppManager _apps;
    private readonly ListView _list = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false };
    private readonly Label _status = new() { AutoSize = true, Padding = new Padding(8, 8, 0, 0) };

    public UninstallPage(AppManager apps)
    {
        _apps = apps;
        Dock = DockStyle.Fill;
        _list.Columns.Add("Name", 320);
        _list.Columns.Add("Publisher", 200);
        _list.Columns.Add("Version", 100);
        _list.Columns.Add("Size", 90, HorizontalAlignment.Right);

        var refresh = new Button { Text = "Refresh", AutoSize = true };
        var uninstall = new Button { Text = "Uninstall…", AutoSize = true };
        refresh.Click += async (_, _) => await Reload();
        uninstall.Click += OnUninstall;

        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(6) };
        bar.Controls.AddRange([refresh, uninstall, _status]);
        Controls.Add(_list);
        Controls.Add(bar);
        HandleCreated += async (_, _) => await Reload();
    }

    private async Task Reload()
    {
        _status.Text = "Loading…";
        var apps = await Task.Run(_apps.List);
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var a in apps)
        {
            var row = new ListViewItem(a.Name) { Tag = a };
            row.SubItems.Add(a.Publisher);
            row.SubItems.Add(a.Version);
            row.SubItems.Add(a.SizeBytes > 0 ? SizeFormat.Format(a.SizeBytes) : "");
            _list.Items.Add(row);
        }
        _list.EndUpdate();
        _status.Text = $"{apps.Count} apps";
    }

    private void OnUninstall(object? sender, EventArgs e)
    {
        if (_list.SelectedItems.Count == 0) return;
        var app = (InstalledApp)_list.SelectedItems[0].Tag!;
        if (MessageBox.Show(this, $"Run the uninstaller for \"{app.Name}\"?", "WindowsCleaner",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        try { _apps.Uninstall(app); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "WindowsCleaner", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }
}
```

- [ ] **Step 3: SettingsPage**

`src/WindowsCleaner.App/SettingsPage.cs`:

```csharp
using System.Diagnostics;
using WindowsCleaner.Core;

namespace WindowsCleaner.App;

public sealed class SettingsPage : UserControl
{
    public SettingsPage(AppSettings settings, RegistryCleaner registry)
    {
        Dock = DockStyle.Fill;
        Padding = new Padding(12);

        var days = new NumericUpDown { Minimum = 0, Maximum = 365, Value = settings.LogRetentionDays, Width = 70 };
        var save = new Button { Text = "Save", AutoSize = true };
        save.Click += (_, _) =>
        {
            settings.LogRetentionDays = (int)days.Value;
            settings.Save();
            MessageBox.Show(this, "Saved. Restart the app for this to take effect.", "WindowsCleaner");
        };

        var openBackups = new Button { Text = "Open backup folder", AutoSize = true };
        openBackups.Click += (_, _) =>
        {
            Directory.CreateDirectory(AppPaths.BackupsDir);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AppPaths.BackupsDir}\"") { UseShellExecute = true });
        };

        var restore = new Button { Text = "Restore registry backup…", AutoSize = true };
        restore.Click += (_, _) =>
        {
            Directory.CreateDirectory(AppPaths.BackupsDir);
            using var dialog = new FolderBrowserDialog
            {
                Description = "Choose a backup folder to restore",
                InitialDirectory = AppPaths.BackupsDir,
            };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            if (MessageBox.Show(this, $"Import all .reg files from\n{dialog.SelectedPath}?", "WindowsCleaner",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            try
            {
                registry.Restore(dialog.SelectedPath);
                MessageBox.Show(this, "Backup restored.", "WindowsCleaner");
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "WindowsCleaner", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        };

        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        panel.Controls.Add(new Label { Text = "Delete log files older than (days):", AutoSize = true });
        panel.Controls.Add(days);
        panel.Controls.Add(save);
        panel.Controls.Add(new Label { Text = " ", AutoSize = true });
        panel.Controls.Add(new Label { Text = $"Registry backups: {AppPaths.BackupsDir}", AutoSize = true });
        panel.Controls.Add(openBackups);
        panel.Controls.Add(restore);
        Controls.Add(panel);
    }
}
```

- [ ] **Step 4: Wire into MainForm**

In `src/WindowsCleaner.App/MainForm.cs`, replace the `Show("Cleaner");` line in the constructor with:

```csharp
        AddPage("Startup", new StartupPage(StartupManager.CreateDefault(reg)));
        AddPage("Uninstall", new UninstallPage(new AppManager(reg)));
        AddPage("Settings", new SettingsPage(settings, registryCleaner));

        Show("Cleaner");
```

- [ ] **Step 5: Build, test, smoke-test**

Run: `dotnet build && dotnet test`
Expected: build succeeded; all tests PASS.

Manual smoke test: launch the exe as in Task 7. Check **Startup** lists items and Disable/Enable flips the Status column (re-enable anything you disabled); **Uninstall** lists apps with sizes (do not actually uninstall anything); **Settings** shows the days box and backup buttons.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: add Startup, Uninstall and Settings pages"
```

---

### Task 9: Publish the single-file exe and final verification

**Files:**
- Modify: `src/WindowsCleaner.App/WindowsCleaner.App.csproj` (publish defaults)
- Output: `dist/WindowsCleaner.exe`

**Interfaces:**
- Consumes: everything above.
- Produces: `dist/WindowsCleaner.exe`, a self-contained single file.

- [ ] **Step 1: Add publish defaults**

In `WindowsCleaner.App.csproj`, add to the first `<PropertyGroup>`:

```xml
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <SelfContained>true</SelfContained>
    <PublishSingleFile>true</PublishSingleFile>
    <IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>
    <EnableCompressionInSingleFile>true</EnableCompressionInSingleFile>
```

- [ ] **Step 2: Publish**

Run: `dotnet publish src/WindowsCleaner.App -c Release -o dist`
Expected: `dist/WindowsCleaner.exe` exists (roughly 60–100 MB compressed, self-contained). Verify: `ls -la dist/*.exe`.

- [ ] **Step 3: Final test run and smoke test of the published exe**

Run: `dotnet test -c Release`
Expected: all tests PASS.

Launch `dist\WindowsCleaner.exe` (UAC prompt expected). Confirm: all five pages open, Cleaner → Analyze lists items, Registry → Analyze lists items or "Nothing to clean.". Only run a real clean once you've reviewed the listed items.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "build: publish single-file self-contained exe"
```
