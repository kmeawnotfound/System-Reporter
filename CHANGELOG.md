# Changelog

All notable changes to SystemReport are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project uses [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [1.1.0] - 2026-10-24

### Added

- Health summary at the top of the report, with actionable warnings and neutral
  notes. The same data is available as `warnings` and `notes` in JSON.
- TPM details through `tpmtool`, with a WMI fallback.
- Secure Boot, VBS, memory integrity (HVCI), and Credential Guard reporting.
- Third-party antivirus detection alongside Microsoft Defender, including
  real-time protection and definition age.
- Problem-device reporting with Plug-and-Play error codes, while treating
  intentionally disabled/disconnected devices as informational.
- Startup entries from 64-bit, 32-bit, and current-user Run keys and both Startup
  folders.
- Monitor identification, size, manufacture date, and serial number.
- Page-file usage and capacity reporting.
- Richer network data: adapter type, link speed, DHCP, IPv4/IPv6, gateway, DNS,
  DNS suffix, and Wi-Fi SSID/signal/radio/band/channel/BSSID.
- Redaction mode for serial numbers, MAC addresses, IPs, gateway, DNS, Wi-Fi names,
  usernames, PC name, and volume labels.
- JSON output for automation.
- Compact minimal output intended for AI chats and support tickets, with automatic
  clipboard copy.
- Command-line options for output path, clipboard copy, suppressing the automatic
  open, elevation, version, and help.
- An interactive menu for full, minimal, and JSON reports.
- Build and test tooling: `build.ps1`, `test.ps1`, `release.ps1`, packaging, and
  SHA-256 checksums.
- Documentation set: README, build/usage/JSON/architecture guides, changelog, and
  contribution guide.

### Changed

- The displayed version is now the full semantic version (`1.1.0`) and is kept in
  sync with the assembly/file version (`1.1.0.0`) and the Git tag (`v1.1.0`).
- `Build.bat` now compiles with explicit C# 5, warning level 4, optimization, and a
  stale-executable cleanup before compiling.
- `Build.bat -q` no longer pauses on errors, so it is safe for scripts and CI.
- Text output wraps long values and paths more readably.
- Reports open automatically when finished, except minimal reports and when
  `--no-open` is used.

### Fixed

- External command execution now has timeouts and avoids leaving child processes
  or handles behind.
- Process and registry handles are disposed reliably.
- Clipboard copy handles non-ASCII text correctly by writing UTF-8 to `clip.exe`.
- Output paths containing `~`, environment variables, spaces, or missing folders
  are handled consistently.
- JSON keys are kept unique when a section contains repeated labels.
