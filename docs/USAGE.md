# Using SystemReport

This guide covers the interactive menu, every command-line option, output behavior,
redaction, automation, and common questions. For build instructions, see
[BUILDING.md](BUILDING.md). For the JSON shape, see [JSON-SCHEMA.md](JSON-SCHEMA.md).

## Interactive menu

Run the executable with no arguments:

```bat
SystemReport.exe
```

```text
  ==============================
    SYSTEM REPORT  v1.1.0
  ==============================

  [1] Full report     (detailed, opens when done)
  [2] Minimal report  (compact, copied to clipboard - ideal for AI chats)
  [3] JSON report     (machine-readable)
  [Q] Quit

  Tip: run as administrator for TPM and BitLocker details.

  Choose:
```

After choosing a format, the tool asks:

```text
  Hide personal info (serial, MAC, IPs, username, PC name)? [y/N]:
```

Answer `y` to enable redaction. The menu then collects the report and pauses before
closing, so double-clicking the exe does not flash a console window and disappear.

## Command-line options

Options are case-insensitive and accept `-` or `/` as the prefix. Short and long
forms are equivalent.

| Option | Argument | Description |
|---|---|---|
| `-m`, `--minimal` | none | Compact text, one short line per area. Also copied to the clipboard. |
| `-j`, `--json` | none | JSON output. Overrides `--minimal` if both are given. |
| `-r`, `--redact` | none | Hide personal and identifying values. |
| `-o`, `--out` | file path | Write to a specific file. Creates parent folders. |
| `--out=FILE` | inline | Same as `--out FILE`; `/out=FILE` also works. |
| `-c`, `--copy` | none | Copy the result to the clipboard in addition to writing the file. |
| `-n`, `--no-open` | none | Do not open the file when finished. |
| `-e`, `--elevate` | none | Relaunch with administrator rights through UAC. |
| `-v`, `--version` | none | Print `SystemReport 1.1.0` and exit. |
| `-h`, `--help` | none | Print help and exit. |

Unknown options print an error and exit with code 2. A missing value after `-o` or
`--out` also exits with code 2.

## Examples

### Full report

```bat
SystemReport.exe
```

Writes `System_Report_<PC>_<date>.txt` to the Desktop and opens it.

### Compact report for a chat

```bat
SystemReport.exe -m
```

Writes `System_Report_<PC>_<date>_min.txt`, prints the compact text to the console,
and copies it to the clipboard. The file is not opened automatically.

### Redacted JSON

```bat
SystemReport.exe -j -r -n -o "%USERPROFILE%\Desktop\reports\pc.json"
```

Creates `reports\` if needed, writes JSON without a BOM, does not open it, and
replaces personal values with `[redacted]`.

### Support ticket with elevation

```bat
SystemReport.exe -e -o "D:\Tickets\PC123\report.txt"
```

UAC prompts; after approval the elevated process runs with the same arguments and
writes the report. If UAC is cancelled, the tool exits with code 1.

### Copy without opening

```bat
SystemReport.exe -c -n
```

Writes the normal text report, copies it to the clipboard, and leaves the default
application alone.

### JSON for a script

```bat
SystemReport.exe -j -r -n -o "%TEMP%\system-report.json"
if errorlevel 1 exit /b 1
```

## Output behavior

### Default file names

If `--out` is not supplied, the report goes to the Desktop. If the Desktop folder
is unavailable, the current directory is used.

| Mode | File name |
|---|---|
| Full text | `System_Report_<PC-NAME>_<YYYY-MM-DD>.txt` |
| Minimal | `System_Report_<PC-NAME>_<YYYY-MM-DD>_min.txt` |
| JSON | `System_Report_<PC-NAME>_<YYYY-MM-DD>.json` |

With `--redact`, the `<PC-NAME>` part is omitted.

### Paths

- `~` expands to the current user's profile.
- `~\Reports\pc.json` and `~/Reports/pc.json` work.
- Environment variables such as `%USERPROFILE%`, `%TEMP%`, and `%COMPUTERNAME%`
  are expanded.
- Missing parent folders are created.
- An existing file is overwritten.
- A path that is a directory produces an error and exit code 1.
- Relative paths are resolved against the current working directory.

### Encoding and opening

- Text reports are UTF-8 **with** a BOM, which helps older Windows tools detect the
  encoding.
- JSON reports are UTF-8 **without** a BOM, which is friendlier to parsers.
- Full and JSON reports are opened in the default application when finished.
- Minimal reports are never opened automatically.
- `--no-open` suppresses opening in every mode.
- The console prints the final `Saved: <path>` line.

### Clipboard

`--copy` copies the rendered output (text or JSON) to the clipboard. Minimal mode
copies automatically. The tool writes UTF-8 bytes to `clip.exe`, so non-ASCII
volume labels and user names survive.

Clipboard access can fail in a non-interactive service session or when another
process holds the clipboard. The report file is still written; the tool prints
`Could not copy to clipboard.` and continues.

## Redaction

`--redact` replaces these values with `[redacted]`:

- PC name (and removes it from the default file name)
- Current user and domain
- Serial numbers collected as secrets (machine, monitor, and similar)
- MAC addresses
- IPv4 and IPv6 addresses
- Default gateway
- DNS servers and DNS suffix
- Wi-Fi SSID and BSSID
- Volume labels

Startup commands always have the user profile path replaced with
`%USERPROFILE%`, even without `--redact`.

Redaction does **not** hide:

- Hardware manufacturer and model names
- CPU, GPU, RAM, disk, and monitor model names
- Driver versions
- Windows edition, build, and update IDs
- Process names and memory usage
- Installed antivirus product names
- Device names

Those can still identify a person or organization. Always review a report before
sharing it publicly. Redaction is a convenience, not an anonymity guarantee.

## What each section contains

| Section | Contents |
|---|---|
| **Summary** | Overall status, warnings, and notes |
| **Operating System** | Edition, version/build, architecture, install date, last boot, uptime, time zone, current user |
| **Machine & Firmware** | Manufacturer, model, system type, serial, motherboard, BIOS version/date, Secure Boot, TPM |
| **Processor** | Name, cores/threads, clock, L2/L3 cache, virtualization, current load |
| **Memory** | Total, used, available, slots, max capacity, page file, module details |
| **Graphics** | Adapter, driver, VRAM, resolution/refresh, status, monitors |
| **Storage** | Physical disks (media, bus, health), volumes (usage, free space, file system) |
| **Devices** | Plug-and-Play devices with configuration error codes |
| **Network** | Active adapters, type, MAC, link speed, DHCP, IPs, gateway, DNS, suffix, Wi-Fi details |
| **Battery** | Charge, status, health versus design capacity (laptops only) |
| **Security** | Defender, third-party AV, firewall profiles, UAC, BitLocker, VBS/HVCI, Credential Guard, pending reboot, recent updates |
| **Startup** | Run keys (64-bit, 32-bit, current user) and Startup folder entries |
| **Processes** | Top five process groups by working set and total process count |
| **Tool & Environment** | Tool version, .NET runtime, process bitness, system language, admin status |

## Warnings and notes

Warnings are actionable problems. They appear in the Summary section and in the
JSON `warnings` array.

| Warning | Trigger |
|---|---|
| Low free space | A volume has 10% or less free |
| Memory usage is high | 90% or more of physical memory is in use |
| Page file is nearly full | 90% or more of the page file is allocated |
| Disk reports warning/unhealthy | Physical disk health is not `Healthy` or `Unknown` |
| GPU reports a problem | Display adapter status is not `OK` |
| Device problem | A device has an error code other than 22 (disabled), 24 (not present), or 45 (not connected) |
| No active antivirus | Neither Defender nor a third-party AV reports enabled |
| Defender real-time protection is off | Defender is on, real-time is off, and no third-party AV is active |
| Firewall is off | A domain, private, or public profile is disabled |
| Battery health is low | Full charge capacity is below 80% of design capacity |
| Secure Boot is disabled | Secure Boot is present but disabled |
| Pending reboot | Windows has a pending servicing or update reboot |
| Collection failed | A section could not be collected |

Notes are neutral observations:

- Uptime of 30 days or more.
- Defender definitions older than 7 days.
- The newest installed update is older than 60 days.

Collection failures do not abort the report. The affected section contains an
`Error` entry and the Summary gets a warning, so the rest of the data is still
available.

## Automation

### PowerShell

```powershell
$exe = 'C:\Tools\SystemReport\SystemReport.exe'
$out = Join-Path $env:TEMP 'system-report.json'

& $exe -j -r -n -o $out
if ($LASTEXITCODE -ne 0) {
    throw "SystemReport failed with exit code $LASTEXITCODE"
}

$report = Get-Content -LiteralPath $out -Raw | ConvertFrom-Json
"Tool:     $($report.tool)"
"Computer: $($report.computer)"
"Warnings: $($report.warnings.Count)"
$report.warnings | ForEach-Object { "  - $_" }
```

### Task Scheduler

Task Scheduler does not reliably expand environment variables in the Arguments
field. Use a small wrapper so the computer name and date are expanded by `cmd.exe`:

`C:\Tools\SystemReport\collect.cmd`:

```bat
@echo off
setlocal
set "OUT=C:\Reports\%COMPUTERNAME%_%DATE:~-4%-%DATE:~4,2%-%DATE:~7,2%.json"
"C:\Tools\SystemReport\SystemReport.exe" -j -r -n -o "%OUT%"
exit /b %ERRORLEVEL%
```

Then create a task:

- Program: `C:\Tools\SystemReport\collect.cmd`
- Start in: `C:\Tools\SystemReport`
- Run whether the user is logged on or not (if your policy allows it)

Use `-r` if the report will leave the machine. The `%DATE%` substring trick depends
on the regional date format; use PowerShell for a locale-independent date if
needed.

### PowerShell scheduled task with a fixed name

```powershell
$out = 'C:\Reports\' + $env:COMPUTERNAME + '_' + (Get-Date -Format 'yyyy-MM-dd') + '.json'
& 'C:\Tools\SystemReport\SystemReport.exe' -j -r -n -o $out
```

### Batch

```bat
@echo off
"C:\Tools\SystemReport\SystemReport.exe" -m -n -o "%TEMP%\system-report.txt"
if errorlevel 1 exit /b 1
```

### CI or inventory scripts

- Always pass `-n` to avoid opening a file in a headless session.
- Always pass `-o` with an explicit path.
- Check the exit code: 0 is success, 1 is a write/elevation failure, 2 is a bad
  option.
- Parse `warnings` first; it is the most stable part of the JSON contract.
- Do not assume every section or field is present. Hardware and Windows editions
  vary.

## Elevation

`--elevate` uses the Windows `runas` verb, which shows the UAC prompt. The elevated
process receives the same arguments.

- If the user is already an administrator in an elevated session, no prompt is shown.
- If UAC is cancelled, the tool prints a message and exits with code 1.
- Elevation can reveal TPM and BitLocker details that a standard user cannot read.
- If the UAC prompt asks for a different administrator account, the default Desktop
  may belong to that account. Use an explicit `--out` path when elevating.

## Exit codes

| Code | Meaning |
|---|---|
| `0` | Success, including a normal interactive quit |
| `1` | Output write failure, directory used as a file, or elevation cancelled/failed |
| `2` | Unknown option, missing option value, or unparseable arguments |

Collection warnings do not change the exit code.

## Common questions

**Why does collection take a few seconds?**
It queries WMI/CIM, the registry, storage providers, and a few local tools. External
commands have timeouts, and a slow provider can add a second or two. The elapsed
time is printed as `Collected in`.

**Why is TPM "Unknown" or BitLocker "Not available"?**
Windows hides those values from standard users, or the hardware/firmware does not
expose them. Try `SystemReport.exe -e`. Some editions (and most virtual machines)
do not provide a TPM or BitLocker at all.

**Why is a value N/A?**
The underlying WMI property, device, or feature is not present. N/A is normal for
missing serial numbers, unpopulated memory slots, desktops without batteries, and
virtual machines without TPMs.

**Why are there keys like `Warning (2)` in JSON?**
JSON objects cannot contain duplicate keys. The data model keeps every warning but
suffixes repeated labels with ` (2)`, ` (3)`, and so on. Use the top-level
`warnings` array when you want the list without suffixes.

**Why does the minimal report not open?**
It is designed for copying, not reading in an editor. It is printed to the console
and copied to the clipboard. Use the full report if you want a file to open.

**Why did the clipboard copy fail?**
`clip.exe` needs an interactive desktop session. In a service, scheduled task
without a desktop, or remote session, the copy can fail while the file write still
succeeds.

**Can I run it on Windows 7?**
It may work with .NET Framework 4.x, but `tpmtool`, newer WMI classes, and some
security providers are unavailable. Expect more N/A values and collection notes.

**Does it send anything over the network?**
No. It reads local data and invokes local Windows tools (`tpmtool`, `netsh`, and
`clip.exe` when copying). It does not download updates or upload reports.
