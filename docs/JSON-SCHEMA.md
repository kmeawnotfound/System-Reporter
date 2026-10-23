# JSON output format

`SystemReport.exe -j` writes a UTF-8 JSON document without a BOM. This guide
describes the shape, the meaning of each field, and how to consume it safely.

The format is intentionally small and human-readable. It is **not** a formal schema
with a version number. The `tool` field contains the application version, and the
top-level `warnings` and `notes` arrays are the most stable contract. Section names
and labels may evolve between minor releases as Windows exposes new data.

## Complete example

Values below are illustrative and redacted.

```json
{
  "tool": "SystemReport 1.1.0",
  "computer": "[redacted]",
  "generated": "Saturday, 24 October 2026  00:15:03",
  "admin": false,
  "duration_sec": 7.4,
  "warnings": [
    "Low free space on D: (10% free, 47.49 GB)",
    "A reboot is pending to finish an installation or update"
  ],
  "notes": [
    "Defender definitions are 9 days old"
  ],
  "sections": {
    "Summary": {
      "Status": "2 warnings, 1 note",
      "Warning": "Low free space on D: (10% free, 47.49 GB)",
      "Warning (2)": "A reboot is pending to finish an installation or update",
      "Note": "Defender definitions are 9 days old"
    },
    "Operating System": {
      "Edition": "Windows 11 Pro",
      "Version": "24H2 (build 26100.2314)",
      "Architecture": "64-bit",
      "Installed on": "12 Jan 2025",
      "Last boot": "21 Oct 2026 08:14",
      "Uptime": "2d 16h 1m",
      "Time zone": "(UTC+05:30) Chennai, Kolkata, Mumbai, New Delhi",
      "Current user": "[redacted]"
    },
    "Machine & Firmware": {
      "Manufacturer": "Contoso",
      "Model": "Workstation 9000",
      "System type": "x64-based PC",
      "Serial number": "[redacted]",
      "Motherboard": "Contoso Board 1.0",
      "BIOS version": "2.14",
      "BIOS date": "03 Mar 2026",
      "Secure Boot": "Enabled",
      "TPM": "Present, v2.0, initialized"
    },
    "Processor": {
      "Name": "12th Gen Intel(R) Core(TM) i7-12700",
      "Cores / Threads": "12 / 20",
      "Clock": "2.10 GHz (max 2.10 GHz)",
      "L2 / L3 cache": "12 MB / 25 MB",
      "Virtualization": "On",
      "Current load": "18%"
    },
    "Memory": {
      "Total": "32 GB",
      "In use": "38%  12.1 GB",
      "Available": "19.9 GB",
      "Slots": "2 used of 4",
      "Max capacity": "128 GB",
      "Page file": "1.2 GB used of 8 GB (system managed)",
      "Modules": {
        "DIMM 1": "16 GB  DDR5  4800 MHz  (Contoso)",
        "DIMM 2": "16 GB  DDR5  4800 MHz  (Contoso)"
      }
    },
    "Graphics": {
      "NVIDIA GeForce RTX 3060": {
        "Driver version": "32.0.15.6094",
        "Driver date": "12 Aug 2026",
        "VRAM (dedicated)": "12 GB",
        "Resolution": "2560 x 1440 @ 144 Hz"
      },
      "Monitor: Dell U2720Q": {
        "Manufacturer": "DEL",
        "Size": "27 in (60 x 34 cm)",
        "Manufactured": "2024 week 18",
        "Serial number": "[redacted]"
      }
    },
    "Storage": {
      "Physical disks": {
        "Samsung SSD 980 PRO 1TB": "931 GB  |  SSD  |  NVMe  |  Healthy"
      },
      "Volumes": {
        "C: System": "62%  301 GB free of 931 GB (NTFS)",
        "D: Data": "90%  47.49 GB free of 465 GB (NTFS)"
      }
    },
    "Devices": {
      "Problem devices": {
        "Status": "None detected"
      }
    },
    "Network": {
      "Ethernet": {
        "Adapter": "Intel(R) Ethernet Controller I225-V",
        "Type": "Ethernet",
        "MAC address": "[redacted]",
        "Link speed": "1 Gbps",
        "DHCP": "On",
        "IPv4": "[redacted]",
        "IPv6": "[redacted]",
        "Gateway": "[redacted]",
        "DNS": "[redacted]",
        "DNS suffix": "[redacted]"
      },
      "Wi-Fi": {
        "Connected to": "[redacted]",
        "Signal": "87%",
        "Radio": "802.11ax",
        "Band": "5 GHz",
        "Channel": "44",
        "BSSID": "[redacted]"
      }
    },
    "Security": {
      "Antivirus (Defender)": "On",
      "Real-time protection": "On",
      "Definitions updated": "15 Oct 2026 06:12",
      "Firewall - Domain": "On",
      "Firewall - Private": "On",
      "Firewall - Public": "On",
      "User Account Control": "On",
      "BitLocker (C:)": "On",
      "VBS": "Running",
      "Memory integrity (HVCI)": "On",
      "Credential Guard": "Off",
      "Pending reboot": "Yes",
      "Other antivirus": {
        "Contoso Endpoint": "enabled, up to date"
      },
      "Latest installed updates": {
        "KB5063878": "13 Aug 2026",
        "KB5062660": "22 Jul 2026"
      }
    },
    "Startup": {
      "Auto-start programs": {
        "OneDrive": "C:\\Program Files\\Microsoft OneDrive\\OneDrive.exe /background",
        "Teams": "%USERPROFILE%\\AppData\\Local\\Microsoft\\Teams\\Update.exe --processStart Teams.exe"
      }
    },
    "Processes": {
      "Top 5 by working set": {
        "chrome (x12)": "1.8 GB",
        "Teams": "920 MB",
        "explorer": "310 MB",
        "Code": "280 MB",
        "System": "140 MB"
      },
      "Running": "214 processes"
    },
    "Tool & Environment": {
      "Report tool": "SystemReport 1.1.0",
      ".NET runtime": "4.0.30319.42000",
      "Process": "64-bit",
      "System language": "English (United States)",
      "Run as admin": "No"
    }
  }
}
```

## Top-level fields

| Field | Type | Meaning |
|---|---|---|
| `tool` | string | Application name and version, for example `SystemReport 1.1.0` |
| `computer` | string | Machine name, or `[redacted]` with `--redact` |
| `generated` | string | Local timestamp in a fixed invariant format |
| `admin` | boolean | Whether the process had administrator rights |
| `duration_sec` | number | Collection time in seconds, one decimal |
| `warnings` | array of strings | Actionable problems, in discovery order |
| `notes` | array of strings | Neutral observations |
| `sections` | object | Report sections keyed by title |

`generated` uses the format `dddd, dd MMMM yyyy  HH:mm:ss` with an invariant
culture, so it is stable across locales even though it is a display string. Parse it
with `DateTime.Parse` using the invariant culture if needed; otherwise use your own
collection timestamp.

`duration_sec` is a JSON number. Depending on the parser it may arrive as a
`double`, `decimal`, or integer-like value.

## Sections

Each value in `sections` is an object. There are two shapes:

### Flat section

A direct map of labels to string values:

```json
"Operating System": {
  "Edition": "Windows 11 Pro",
  "Architecture": "64-bit"
}
```

### Section with named blocks

A map of block names to objects, where each object contains labels and values:

```json
"Storage": {
  "Physical disks": {
    "Samsung SSD 980 PRO 1TB": "931 GB  |  SSD  |  NVMe  |  Healthy"
  },
  "Volumes": {
    "C: System": "62%  301 GB free of 931 GB (NTFS)"
  }
}
```

Consumers should inspect whether a section value looks flat or nested rather than
assuming one shape for every section. For example:

```powershell
$section = $report.sections.Storage
foreach ($property in $section.PSObject.Properties) {
    if ($property.Value -is [PSCustomObject]) {
        # named block, for example Physical disks
        foreach ($entry in $property.Value.PSObject.Properties) {
            "$($property.Name) / $($entry.Name) = $($entry.Value)"
        }
    }
    else {
        "$($property.Name) = $($property.Value)"
    }
}
```

## Value conventions

- **Within `sections`, all leaf values are strings.** Numbers, percentages, sizes,
  booleans, and dates are formatted for humans. At the top level, `admin` is a
  boolean and `duration_sec` is a number.
- **Percentages include the value and the detail.** For example
  `"62%  301 GB free of 931 GB (NTFS)"`. The text report adds a usage bar; JSON does
  not.
- **Missing values are `N/A`.** This is normal for absent hardware, hidden WMI
  properties, or unsupported features.
- **Sensitive values are `[redacted]`** when `--redact` is active and the field is
  collected as a secret.
- **Duplicate labels are suffixed.** JSON objects cannot have duplicate keys, so a
  second `Warning` becomes `Warning (2)`, a third `Warning (3)`, and so on. Use the
  top-level arrays for an unsuffixed list.
- **Keys may contain spaces, punctuation, and colons.** Quote them in jq and use
  bracket notation in most languages.
- **Section order is stable in practice but not a contract.** Do not depend on JSON
  member order.

## Warnings and notes

The top-level arrays are duplicated inside `sections.Summary` as `Warning` and
`Note` entries (with duplicate suffixes). Prefer the top-level arrays:

```powershell
$report.warnings
$report.notes
```

```bash
jq -r '.warnings[]' report.json
```

A report with no warnings has `"warnings": []` and a Summary status of
`OK - no issues detected`.

## Redaction

With `--redact`, the following are replaced by `[redacted]` where present:

- `computer`
- current user
- serial numbers collected as secrets
- MAC address, IPv4, IPv6, gateway, DNS, DNS suffix
- Wi-Fi SSID and BSSID
- volume labels (the volume key becomes `C: Volume`)

Redaction is best effort. See [USAGE.md](USAGE.md#redaction) for the full list of
what is **not** hidden.

## Encoding and parsing

- UTF-8, no BOM.
- One JSON object; no trailing commas.
- Strings escape quotes, backslashes, tabs, newlines, and control characters.
- Carriage returns are dropped.
- Non-ASCII characters are written as UTF-8 text (not `\u` escapes).

PowerShell:

```powershell
$json = Get-Content -LiteralPath .\report.json -Raw | ConvertFrom-Json
```

Python:

```python
import json
with open("report.json", encoding="utf-8") as f:
    report = json.load(f)
print(report["tool"], len(report["warnings"]))
```

jq:

```bash
jq '.tool, .warnings, .sections["Operating System"]' report.json
```

## Stability and compatibility

- `warnings` and `notes` are the recommended integration point for health checks.
- `tool`, `computer`, `generated`, `admin`, and `duration_sec` are stable.
- Section titles and labels can change when Windows data sources change or new
  fields are added. Treat missing keys as normal.
- The format does not currently include a `schema_version`. If you need strict
  validation, pin the tool version and validate the fields you consume.
- The output is designed to be read by humans as well as machines; pretty-printing
  is intentional.

## Why not JSON Schema?

A formal schema would be large, Windows-version-dependent, and would imply a
stability guarantee the tool does not make. The section model is deliberately
permissive so a missing WMI class produces `N/A` or an absent field instead of a
validation failure. If you need a stable subset, consume the top-level metadata and
the warnings/notes arrays.
