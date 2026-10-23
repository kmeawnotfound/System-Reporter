# SystemReport

SystemReport is a single-file Windows diagnostic tool. It collects the machine's
operating system, hardware, storage, network, security, startup, and process state
and writes it as:

- a **readable text report** with a health summary at the top,
- a **compact summary** designed for pasting into an AI chat or support ticket,
- **structured JSON** for scripts, inventories, and dashboards.

It is deliberately small: one C# source file, no installer, no NuGet packages, no
network access, and no dependency beyond the .NET Framework that ships with Windows.

| | |
|---|---|
| **Latest release** | See [CHANGELOG.md](CHANGELOG.md) |
| **Platform** | Windows 10 and 11 (Windows 8.1/7 with .NET Framework 4.x: best effort) |
| **Runtime** | .NET Framework 4.x (included with Windows 10/11) |
| **Language** | C# 5 |
| **Build** | In-box `csc.exe`; PowerShell 5.1 for the full build/test/package workflow |
| **Admin rights** | Optional; recommended for TPM and BitLocker details |
| **Output** | Text, minimal text, JSON |

## Why this exists

Most system-information tools either dump hundreds of raw fields, require an
installer, or send data somewhere. SystemReport is meant to answer the practical
questions first:

- Is anything actually wrong? (disk space, protection, problem devices, pending reboot)
- What hardware and OS is this?
- What changed or what is running at startup?
- What is safe to paste into a chat or ticket?

The full report is still available, but the first thing you see is a **Summary**
section containing only real warnings and notes.

## Features

- **Health summary first.** Low disk space, high memory use, unhealthy disks,
  problem devices, disabled protection, low battery health, pending reboot, and
  collection failures become clear warnings. Neutral observations become notes.
- **Complete sections.** Summary, Operating System, Machine & Firmware, Processor,
  Memory, Graphics and monitors, Storage, Devices, Network and Wi-Fi, Battery
  (laptops), Security, Startup, Processes, and Tool & Environment.
- **Security detail without extra tools.** Secure Boot, TPM (via `tpmtool` with a
  WMI fallback), BitLocker, Defender and third-party antivirus, firewall profiles,
  UAC, VBS, memory integrity (HVCI), Credential Guard, pending reboot, and recent
  updates.
- **Storage and device health.** Physical disk media/bus/health, per-volume usage,
  free-space warnings, and Plug-and-Play devices reporting configuration errors.
- **Useful network data.** Active adapters, link speed, DHCP, IPv4/IPv6, gateway,
  DNS, DNS suffix, and Wi-Fi SSID/signal/radio/band/channel/BSSID.
- **Startup and process visibility.** Run keys (64-bit, 32-bit, current user),
  Startup folders, and the top five process groups by working set.
- **Privacy controls.** `--redact` hides serial numbers, MAC addresses, IPs,
  gateway, DNS, Wi-Fi names, usernames, the PC name, and volume labels.
- **Three output formats.** Full text, token-light minimal text, and JSON.
- **Friendly automation.** Stable exit codes, `--out` with folder creation, `~`
  and environment-variable expansion, and optional clipboard copy.
- **Robust collection.** Each section is collected independently; a failure becomes
  a warning instead of aborting the report. External tools have timeouts.
- **No network calls and no telemetry.** Everything is read locally and written to
  a local file.

## Requirements

### To run

- Windows 10 or Windows 11 is recommended.
- .NET Framework 4.x. Windows 10/11 include it; older systems may need the
  .NET Framework 4.x runtime installed.
- A standard user account is enough. Run as administrator (or use `--elevate`) to
  improve TPM and BitLocker results and to see some security data that Windows
  hides from standard users.

Windows 7/8.1 can work with .NET Framework 4.x, but newer WMI classes and
`tpmtool` may be unavailable. The tool degrades gracefully and reports what it can.

### To build

- The C# compiler included with the .NET Framework:
  `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe` (or the 32-bit fallback).
- PowerShell 5.1 for `build.ps1`, `test.ps1`, and `release.ps1`. Windows 10/11
  include it. `Build.bat` itself is plain batch and does not need PowerShell.
- No Visual Studio, Windows SDK, NuGet, or administrator rights are required.

## Quick start

```bat
Build.bat
```

`Build.bat` compiles `SystemReport.exe` and offers to run it. For scripts and CI:

```bat
Build.bat -q
```

Then run it:

```bat
SystemReport.exe
```

With no arguments the tool opens an interactive menu (full, minimal, or JSON, plus
an optional redaction prompt). Double-clicking the exe works too.

A minimal report looks like this (values are examples):

```text
Health: 2 warnings - Low free space on D: (10% free, 47.5 GB); A reboot is pending to finish an installation or update
OS: Windows 11 Pro 24H2 build 26100.2314 x64, up 3d4h
PC: Contoso Workstation, BIOS 2.14
CPU: 12th Gen Intel(R) Core(TM) i7-12700, 12C/20T
RAM: 32 GB (DDR5 4800MHz x2), 38% used
GPU: NVIDIA GeForce RTX 3060, 12 GB VRAM, driver 32.0.15.6094
Disk: Samsung SSD 980 PRO 1TB, 931 GB, SSD, Healthy
Vol: C: 931 GB NTFS, 62% used
Net: Ethernet (Ethernet) 192.168.1.50 1 Gbps
Wi-Fi: HomeNetwork 87%
Startup: 9 auto-start entries
Top: chrome 1.8 GB, Teams 920 MB, explorer 310 MB
```

## Build system

`Build.bat` is the low-level bootstrap compiler. It uses the in-box C# compiler
with C# 5, optimization, warning level 4, and `System.Management.dll`. It deletes
the old executable before compiling so a failed build cannot leave a stale binary
behind, and it does not pause on errors when `-q` is used.

`build.ps1` is the full build orchestrator:

```powershell
.\build.ps1                    # build and verify the binary version
.\build.ps1 -Clean -Test       # clean build + smoke tests
.\build.ps1 -Clean -Test -Package
.\build.ps1 -q                 # quiet build
```

It verifies that `Cfg.Version`, `AssemblyVersion`, and `AssemblyFileVersion` agree,
runs the binary's `--version`, and optionally runs `test.ps1` and creates a release
package.

### Build outputs

| Path | Description |
|---|---|
| `SystemReport.exe` | The built executable (ignored by Git) |
| `artifacts\SystemReport-1.1.0-win.zip` | Release package with exe, source, scripts, and docs |
| `artifacts\SystemReport-1.1.0-win.zip.sha256` | SHA-256 checksum of the package |
| `artifacts\build-info.txt` | Version, build time, commit, compiler, and OS information |

The release package includes `README.md`, `CHANGELOG.md`, `CONTRIBUTING.md`, the
source, all build scripts, and `docs\`.

### Direct compiler command

If you do not want to use either script:

```bat
"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" ^
  /nologo /target:exe /platform:anycpu /langversion:5 /optimize+ /warn:4 ^
  /out:SystemReport.exe /reference:System.Management.dll SystemReport.cs
```

See [docs/BUILDING.md](docs/BUILDING.md) for compiler discovery, CI, packaging, and
troubleshooting.

## Usage

### Interactive mode

```bat
SystemReport.exe
```

The menu offers:

1. Full report (detailed, opens when done)
2. Minimal report (compact, copied to the clipboard)
3. JSON report (machine-readable)
4. Quit

It then asks whether to hide personal information. Press `Q` to quit.

### Command line

Options are case-insensitive and accept either `-` or `/` as the prefix. Both short
and long forms work.

| Option | Description |
|---|---|
| `-m`, `--minimal` | Compact text with one short line per area. Also copied to the clipboard. |
| `-j`, `--json` | JSON output. Overrides `--minimal`. |
| `-r`, `--redact` | Hide serial numbers, MAC addresses, IPs, gateway, DNS, Wi-Fi name, username, PC name, and volume labels. |
| `-o`, `--out FILE` | Write to `FILE` instead of the Desktop. Creates parent folders. |
| `--out=FILE` | Same as `--out FILE`. |
| `-c`, `--copy` | Also copy the result to the clipboard. |
| `-n`, `--no-open` | Do not open the output file when finished. |
| `-e`, `--elevate` | Relaunch with administrator rights through UAC. |
| `-v`, `--version` | Print the version and exit. |
| `-h`, `--help` | Print help and exit. |

### Examples

```bat
rem Full report on the Desktop, opened when done
SystemReport.exe

rem Compact report, copied to the clipboard
SystemReport.exe -m

rem Redacted JSON in a specific folder, do not open it
SystemReport.exe -j -r -n -o "%USERPROFILE%\Desktop\reports\pc.json"

rem Full text report for a ticket, with administrator details
SystemReport.exe -e -o "D:\Tickets\PC123\report.txt"

rem Copy a full report to the clipboard as well
SystemReport.exe -c -n
```

`--out` expands `~` and environment variables such as `%USERPROFILE%` and creates
missing folders. The file is overwritten if it already exists. If the path is a
directory, the tool reports an error and exits with code 1.

### Default output files

With no `--out`, the report is written to the Desktop (or the current directory if
the Desktop is unavailable):

```text
System_Report_<PC-NAME>_<YYYY-MM-DD>.txt
System_Report_<PC-NAME>_<YYYY-MM-DD>_min.txt
System_Report_<PC-NAME>_<YYYY-MM-DD>.json
```

With `--redact`, the PC name is omitted from the filename. Text output is UTF-8
with a BOM so Notepad and older tools detect it correctly; JSON is UTF-8 without a
BOM.

The full and JSON reports are opened in the default application when finished.
Minimal reports are not opened (they are printed and copied instead). Use `-n` to
suppress opening in every mode.

### Health warnings

The Summary section and the JSON `warnings` array currently include:

| Warning | Trigger |
|---|---|
| Low free space | A fixed/removable volume has 10% or less free |
| Memory usage is high | 90% or more of physical memory is in use |
| Page file is nearly full | 90% or more of the page file is allocated |
| Disk reports warning/unhealthy | Storage health is not `Healthy` or `Unknown` |
| GPU reports a problem | The display adapter status is not `OK` |
| Device problem | A Plug-and-Play device has an error code other than 22, 24, or 45 |
| No active antivirus | Neither Defender nor a third-party antivirus reports enabled |
| Defender real-time protection is off | Defender is enabled, real-time is off, and no third-party AV is active |
| Firewall is off | A domain, private, or public firewall profile is disabled |
| Battery health is low | Full charge capacity is below 80% of design capacity |
| Secure Boot is disabled | Secure Boot is present but disabled |
| Pending reboot | Windows has a pending servicing or update reboot |
| Collection failed | A section could not be collected; the error is included |

Notes are neutral observations and do not count as warnings:

- Uptime of 30 days or more (a reboot may be due).
- Defender definitions older than 7 days.
- The latest installed update is older than 60 days.

### Exit codes

| Code | Meaning |
|---|---|
| `0` | Success (including a normal interactive quit) |
| `1` | Could not write the output file, or elevation was cancelled/failed |
| `2` | Unknown option or a missing option value |

A collection problem in one section does not change the exit code; it is reported
as a warning so the rest of the report is still usable.

### Automation

PowerShell:

```powershell
& .\SystemReport.exe -j -r -n -o "$env:TEMP\system-report.json"
if ($LASTEXITCODE -ne 0) { throw "SystemReport failed with $LASTEXITCODE" }
$report = Get-Content "$env:TEMP\system-report.json" -Raw | ConvertFrom-Json
$report.warnings
```

Task Scheduler:

- Program: `C:\Tools\SystemReport\collect.cmd` (a wrapper that expands the date and
  computer name; see [docs/USAGE.md](docs/USAGE.md#task-scheduler))
- Start in: `C:\Tools\SystemReport`

Using a wrapper avoids relying on Task Scheduler to expand environment variables in
its Arguments field.

Batch:

```bat
SystemReport.exe -m -n -o "%TEMP%\system-report.txt"
if errorlevel 1 exit /b 1
```

See [docs/USAGE.md](docs/USAGE.md) for more examples and scheduling notes.

## JSON output

The JSON format is intentionally simple: a small set of top-level metadata, the
warnings and notes arrays, and a `sections` object. Each section is either a flat
object of `"Label": "Value"` pairs or an object of named blocks (for example,
`Physical disks`, `Volumes`, or one block per network adapter).

```json
{
  "tool": "SystemReport 1.1.0",
  "computer": "DESKTOP-EXAMPLE",
  "generated": "Friday, 23 October 2026  23:52:22",
  "admin": false,
  "duration_sec": 7.4,
  "warnings": [
    "Low free space on D: (10% free, 47.49 GB)"
  ],
  "notes": [],
  "sections": {
    "Summary": {
      "Status": "1 warning"
    },
    "Operating System": {
      "Edition": "Windows 11 Pro",
      "Version": "24H2 (build 26100.2314)",
      "Architecture": "64-bit",
      "Current user": "[redacted]"
    },
    "Storage": {
      "Physical disks": {
        "Samsung SSD 980 PRO 1TB": "931 GB  |  SSD  |  NVMe  |  Healthy"
      },
      "Volumes": {
        "C: System": "62%  301 GB free of 931 GB (NTFS)"
      }
    }
  }
}
```

All leaf values are strings. Percentages are rendered into the string (with the
usage bar omitted in JSON). The `warnings` and `notes` arrays are the most stable
part of the format; section labels may evolve between minor versions. See
[docs/JSON-SCHEMA.md](docs/JSON-SCHEMA.md) for field details and consumer advice.

## Redaction

`--redact` (or the interactive prompt) replaces personal and identifying values
with `[redacted]`:

- PC name (also omitted from the default filename)
- Current user and domain
- Machine, monitor, and other serial numbers collected as secrets
- MAC addresses, IPv4/IPv6 addresses, gateway, DNS servers, and DNS suffix
- Wi-Fi SSID and BSSID
- Volume labels

It also replaces the user profile path in startup commands with `%USERPROFILE%`
even when redaction is off.

Redaction is **best effort**, not a guarantee of anonymity. Hardware models, driver
versions, update IDs, installed application names, and process names can still
identify a machine or organization. Review the output before posting it publicly.

## Project layout

```text
system reporter/
├─ SystemReport.cs          single-file application
├─ Build.bat                dependency-free bootstrap build
├─ build.ps1                clean/test/package orchestrator
├─ test.ps1                 command-line smoke tests
├─ release.ps1              version bump, build, package, commit, tag
├─ README.md
├─ CHANGELOG.md
├─ CONTRIBUTING.md
├─ docs/
│  ├─ BUILDING.md
│  ├─ USAGE.md
│  ├─ JSON-SCHEMA.md
│  └─ ARCHITECTURE.md
├─ artifacts/               generated packages (ignored by Git)
└─ SystemReport.exe         generated binary (ignored by Git)
```

`SystemReport.cs` is the canonical source. The compiled exe is a build product and
is not committed.

## Development

The project deliberately stays compatible with **C# 5** and the **.NET Framework
4.x** compiler that ships with Windows. Avoid string interpolation, `nameof`,
expression-bodied members, null-conditional operators, tuples, local functions,
auto-property initializers, and other newer syntax. LINQ, `var`, generics, and
`Func` delegates are fine.

Before submitting a change:

```powershell
.\build.ps1 -Clean -Test
```

Add a collector in `SystemReport.cs`, register it in `Program.Collect`, and make
sure it:

- catches nothing that should abort the whole report (the pipeline isolates
  sections),
- uses `Block.Secret` for personal values,
- adds a `Brief` line for minimal output,
- adds `Cfg.Warn` only for actionable problems and `Cfg.Note` for neutral ones,
- keeps JSON keys unique (the model suffixes duplicates with ` (2)`, ` (3)`, ...).

See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) and
[CONTRIBUTING.md](CONTRIBUTING.md).

## Versioning and releases

SystemReport uses [Semantic Versioning](https://semver.org/): `MAJOR.MINOR.PATCH`.
The version appears in three places that must agree:

- `Cfg.Version` in `SystemReport.cs` (for example `1.1.0`),
- `AssemblyVersion` and `AssemblyFileVersion` (for example `1.1.0.0`),
- the annotated Git tag (for example `v1.1.0`).

`build.ps1` verifies the first two before compiling. The release workflow is:

1. Add a `## [1.2.0] - YYYY-MM-DD` section to `CHANGELOG.md`.
2. Preview the release:

   ```powershell
   .\release.ps1 -Version 1.2.0 -DryRun
   ```

3. Run it:

   ```powershell
   .\release.ps1 -Version 1.2.0
   ```

4. Push the commit and tag:

   ```bat
   git push origin main --tags
   ```

`release.ps1` refuses to run on a dirty working tree, requires the changelog entry,
updates the version constants, runs a clean build and tests, commits, creates the
package, and creates the annotated tag.

## Troubleshooting

**The build says the C# compiler was not found.**
Install .NET Framework 4.x, or run the compiler path directly from
`%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\`. The 32-bit fallback is
`%WINDIR%\Microsoft.NET\Framework\v4.0.30319\`.

**PowerShell refuses to run the scripts.**
Use:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

**The exe is blocked or flagged by antivirus.**
It is a locally compiled, unsigned .NET executable. Build it yourself from
`SystemReport.cs`, or inspect the source before running a downloaded copy.

**TPM or BitLocker says "Unknown" / "Not available".**
Run as administrator (`SystemReport.exe -e`). Some firmware and Windows editions
do not expose these values at all.

**A section is missing or says N/A.**
The relevant WMI provider, device, or Windows feature is not present. Check the
Summary warnings; collection errors are listed there instead of aborting the run.

**No battery section.**
The battery section only appears on systems where `Win32_Battery` returns a device.
Desktops normally do not have one.

**The report did not open.**
Minimal reports are not opened by design. Use `-n` to suppress opening, or open the
printed `Saved:` path manually. Check the default application for `.txt`/`.json`.

**The JSON will not parse.**
Use a UTF-8-aware reader. The file is written without a BOM; `Get-Content -Raw`
and `ConvertFrom-Json` work in PowerShell. Report the version and the exact error
if you believe the output is malformed.

## Privacy and security

SystemReport reads local data only. It does not contact a server, download updates,
or include telemetry. It invokes three local Windows tools when needed: `tpmtool`
(TPM details), `netsh` (Wi-Fi details), and `clip.exe` (clipboard copy).

Reports can contain sensitive information. Use `--redact` before sharing, and still
review the result. The tool is not a security scanner and does not replace Defender,
Windows Update, or a proper hardware diagnostic.

## License

No license file has been added to this repository yet. Until one is chosen, all
rights are reserved by the author. Add a `LICENSE` file before distributing the
tool outside your organization.
