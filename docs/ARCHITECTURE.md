# Architecture

SystemReport is one C# source file with a deliberately small architecture: collect
data into a simple object model, then render that model as text, minimal text, or
JSON. There is no dependency injection, no plugin system, and no external runtime
configuration.

## File map

`SystemReport.cs` contains these types, in order:

| Type | Responsibility |
|---|---|
| `Cfg` | Version, admin/redaction flags, warnings and notes |
| `Entry` | One label/value pair, optionally with a percentage |
| `Block` | A named group of entries (or an unnamed group) |
| `Section` | A titled group of blocks plus brief lines for minimal output |
| `Report` | Report metadata and the ordered list of sections |
| `U` | Formatting, registry, process, path, and GPU helpers |
| `W` | Failure-tolerant WMI query helpers |
| `Collectors` | One method per report section |
| `Render` | Text, minimal, and JSON renderers |
| `Options` | Parsed command-line flags |
| `Program` | Entry point, argument parsing, menu, collection loop, output, clipboard |

The assembly attributes at the top define the product name and the version that
Explorer shows in file properties.

## Data model

```text
Report
├─ Computer, Generated, IsAdmin, DurationSec
└─ Sections (ordered)
   └─ Section
      ├─ Title
      ├─ Brief[]            one-liners for minimal output
      └─ Blocks (ordered)
         └─ Block
            ├─ Name          optional; used for grouped output
            └─ Items
               └─ Entry
                  ├─ Key
                  ├─ Value
                  └─ Pct         optional percentage for bars
```

### Entry

`Entry.Text(bool bar)` produces the display value. If `Pct` is set, the value starts
with a percentage (and optionally a 20-character usage bar in text mode). JSON calls
`Text(false)` so it contains the percentage without the bar.

### Block

`Block.Add` is the normal path. `Block.Secret` marks a value as personal so
`Cfg.Redact` can replace it. `Block.AddPct` attaches a percentage.

Two behaviors matter for output correctness:

- Empty values become `N/A` (unless a percentage is present).
- Duplicate keys inside a block get ` (2)`, ` (3)`, and so on, because JSON objects
  cannot contain duplicate member names.

### Section

`Section.G()` creates an unnamed block; `Section.G(name)` creates a named block.
Repeated block names are also suffixed. `Section.Brief` holds the short lines used
by minimal output; it is populated by collectors as they work.

### Report

`Program.Collect` creates the `Report`, fills metadata, runs each collector, and
inserts the Summary section at index 0 after all other sections have contributed
warnings.

## Collection pipeline

```text
Main
├─ ParseArgs
├─ Help / Version fast paths
├─ interactive menu (only when args.Length == 0)
├─ optional elevation
├─ Collect
│  ├─ Cfg.ResetHealth()
│  ├─ for each collector: try/catch, add section, print a dot
│  └─ build Summary last, insert first
├─ Render (Text | Minimal | Json)
├─ write file (create folders, UTF-8 encoding)
├─ print Saved path
├─ optional clipboard
└─ optional open in default app
```

Collectors are listed in a `Func<Section>[]` in `Program.Collect`:

```csharp
Func<Section>[] steps =
{
    Collectors.GetOs, Collectors.GetMachine, Collectors.GetCpu, Collectors.GetMemory,
    Collectors.GetGpu, Collectors.GetStorage, Collectors.GetDevices, Collectors.GetNetwork,
    Collectors.GetBattery, Collectors.GetSecurity, Collectors.GetStartup,
    Collectors.GetTopProcesses, Collectors.GetEnv
};
```

`GetBattery` returns `null` when no battery exists, so the section is omitted. Every
other collector returns a section. If a collector throws, the pipeline creates a
section with an `Error` entry and adds a warning; the rest of the report continues.

## Collectors

| Method | Source | Notes |
|---|---|---|
| `GetSummary` | `Cfg.Warnings`, `Cfg.Notes` | Built last, displayed first |
| `GetOs` | `Win32_OperatingSystem`, registry | Edition, build, install/boot, uptime, user |
| `GetMachine` | `Win32_ComputerSystem`, `Win32_BIOS`, `Win32_BaseBoard`, registry | Secure Boot and TPM |
| `GetCpu` | `Win32_Processor` | One block per socket |
| `GetMemory` | `Win32_ComputerSystem`, `Win32_OperatingSystem`, `Win32_PhysicalMemory*`, `Win32_PageFileUsage` | Modules and page file |
| `GetGpu` | `Win32_VideoController`, `WmiMonitorID`, `WmiMonitorBasicDisplayParams` | VRAM fallback from the driver registry key |
| `GetStorage` | `MSFT_PhysicalDisk` or `Win32_DiskDrive`, `DriveInfo` | Health and free space |
| `GetDevices` | `Win32_PnPEntity` | Error code 0 means healthy |
| `GetNetwork` | `NetworkInterface`, `netsh wlan show interfaces` | Virtual adapters sorted after physical |
| `GetBattery` | `Win32_Battery`, `BatteryFullChargedCapacity`, `BatteryStaticData` | Laptop only |
| `GetSecurity` | SecurityCenter2, Defender, registry, `Win32_EncryptableVolume`, `Win32_DeviceGuard`, `Win32_QuickFixEngineering` | Best effort without admin |
| `GetStartup` | Registry Run keys, Startup folders | 64-bit, 32-bit, current user |
| `GetTopProcesses` | `Process.GetProcesses()` | Groups by name, top five by working set |
| `GetEnv` | .NET runtime | Tool/runtime/bitness/language/admin |

### WMI helper (`W`)

Every WMI call is failure-tolerant:

- `W.Q` catches provider errors and returns an empty list.
- The searcher timeout is set to 10 seconds when the provider supports it.
- `W.S`, `W.D`, `W.B`, `W.Dt`, `W.U16`, and `W.Arr` return `null`/`0`/empty on
  missing properties or conversion errors.

This is why a missing class produces `N/A` rather than an exception. It also means
a collector rarely throws; explicit warnings are used for meaningful problems.

### External commands (`U.Run`)

`U.Run` starts a hidden process with redirected stdout/stderr, drains both pipes,
and waits with a timeout (default 8 seconds; TPM uses 5). On timeout it kills the
process and returns `null`. It sets the OEM code page for output so localized
Windows tools remain readable.

Used for:

- `tpmtool getdeviceinformation` (TPM details; WMI fallback)
- `netsh wlan show interfaces` (Wi-Fi details)
- `clip.exe` (clipboard, in `Program.CopyToClipboard`)

No other process is started during collection. The tool does not make network
requests.

## Renderers

### Text

`Render.Text` writes a 76-column boxed report:

- header with computer, generated time, admin status, and duration,
- one boxed section per collector,
- named blocks with `>> name` separators,
- aligned `key : value` rows with usage bars,
- footer with the tool version.

`Render.Wrap` wraps long values at word boundaries and prefers folder separators
when a path is too long. Values are truncated only where the format requires it
(for example, section titles longer than the box).

### Minimal

`Render.Minimal` emits only `Section.Brief` lines, one per item, with no decoration.
This is the format intended for AI chats and tickets. The same text is printed to
the console and copied to the clipboard.

### JSON

`Render.Json` is hand-rolled to avoid a JSON dependency. It:

- writes top-level metadata, `warnings`, and `notes`,
- renders each section as a flat object or an object of named blocks,
- calls `Entry.Text(false)` to omit usage bars,
- escapes quotes, backslashes, tabs, newlines, and control characters,
- drops carriage returns,
- writes UTF-8 without a BOM from `Program.Main`.

See [JSON-SCHEMA.md](JSON-SCHEMA.md) for the consumer contract.

## Redaction

`Cfg.Redact` is set from `--redact` or the interactive prompt before collection.
Collectors call `Block.Secret` for personal values; `Block.Add` replaces them with
`[redacted]` at insertion time. The PC name is handled separately in `Collect` and
`DefaultPath`. Volume labels are replaced in `GetStorage`. Startup commands always
pass through `U.MaskUser`, which replaces the user profile path with
`%USERPROFILE%` regardless of the redaction flag.

This design keeps redaction close to the data source, so a new secret field is
handled by choosing `Secret` instead of `Add`. It is still best effort; see
[USAGE.md](USAGE.md#redaction).

## Error handling and timeouts

- Each collector is isolated by `Program.Collect`.
- WMI queries are wrapped and time-limited.
- External processes are time-limited, both pipes are drained, and the process is
  killed on timeout.
- File writes are wrapped; failures print a message and return exit code 1.
- Clipboard failures are non-fatal.
- Opening the output file is best effort and ignored on failure.
- `Program.Main` returns 0/1/2; it does not let an unhandled collection exception
  terminate the process.

## Adding a section

1. Add a method to `Collectors`, for example:

   ```csharp
   public static Section GetExample()
   {
       Section s = new Section("Example");
       Block g = s.G();
       string demo = "demo";
       bool demoIsBad = false; // replace with a real check

       g.Add("Value", demo);
       g.Secret("Private value", "secret");
       s.Brief.Add("Example: " + demo);

       if (demoIsBad) Cfg.Warn("Example is not configured");
       return s;
   }
   ```

2. Add it to the `steps` array in `Program.Collect` in the desired order.
3. Use `g.Secret` for personal values, `g.AddPct` for percentages, and `s.G("Name")`
   for a named block.
4. Add a `Brief` line only if the section is useful in the compact report.
5. Use `Cfg.Warn` for actionable problems and `Cfg.Note` for neutral observations.
6. Run `.\build.ps1 -Clean -Test` and inspect all three output formats.

Keep the method C# 5-compatible. Do not use `async`, string interpolation,
null-conditional operators, or newer language features. If a WMI property is
missing, return `N/A` via `g.Add` rather than throwing.

## Testing strategy

`test.ps1` is a black-box smoke suite: it runs the built exe and checks the
command-line contract, text/minimal/JSON output, redaction, path creation, and exit
codes. It does not mock WMI, because the goal is to verify the real collection
pipeline on a real Windows machine.

`build.ps1 -Test` runs the suite after a clean build. `release.ps1` runs it before
committing a release. The GitHub Actions workflow runs it on `windows-latest`.

Manual checks worth doing when changing collectors:

- Run once as a standard user and once elevated.
- Compare `-m`, full text, and `-j` output.
- Run `-j -r` and confirm secrets are `[redacted]`.
- Try a path with spaces, a missing parent folder, and `~`.
- Confirm a failed section becomes a warning rather than a crash.

## Constraints and trade-offs

- **C# 5 / .NET Framework 4.x.** The compiler ships with Windows; no SDK or NuGet.
- **Single source file.** Easy to copy to a machine and compile in place. The cost
  is a large file and manual organization by comment banners.
- **Hand-rolled JSON.** No dependency, but the writer must escape correctly and the
  format is not schema-validated. The smoke tests parse the output.
- **WMI.** Broad hardware coverage without drivers, but slow or missing providers
  on some machines. Timeouts and `N/A` keep the report usable.
- **Best-effort security data.** Standard users cannot read everything; `--elevate`
  improves coverage but is optional.
- **No installer.** The exe is unsigned and self-contained apart from .NET Framework.

## Performance

Collection is usually a few seconds. The dominant costs are WMI queries (especially
storage and security), `tpmtool`, and `netsh`. The elapsed time is printed as
`Collected in` and stored in JSON as `duration_sec`.

If collection is slow on a machine, look for a problematic WMI provider or a hung
external command; the 10-second WMI timeout and 5-8 second process timeouts bound
the worst case per item.

## Security notes

- The tool reads local data only and does not transmit it.
- Reports may contain sensitive hardware, network, and user data. Redaction is
  best effort; review before sharing.
- The executable is not code-signed. Build from source or verify a package checksum
  before running a downloaded copy.
- The tool invokes only `tpmtool`, `netsh`, and `clip.exe`; it does not download or
  execute arbitrary code.
