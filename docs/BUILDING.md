# Building SystemReport

This document covers the build system in detail. For a quick start, see the
[README](../README.md).

## Design goals

The build is intentionally boring:

- **No SDK.** Compile with the C# compiler that ships with the .NET Framework.
- **No package restore.** The only reference is `System.Management.dll`.
- **No generated source.** `SystemReport.cs` remains a single, standalone file.
- **No admin rights.** Everything runs per-user.
- **No hidden state.** The output is `SystemReport.exe` in the project root; packages
  go to `artifacts\`.

`Build.bat` is the bootstrap path. `build.ps1` adds clean, test, and package steps
on top of it. `release.ps1` adds versioning, commit, and tag steps.

## Prerequisites

| Requirement | Notes |
|---|---|
| Windows 10/11 | Windows 8.1/7 may work with .NET Framework 4.x |
| .NET Framework 4.x | Includes `csc.exe`; already present on Windows 10/11 |
| PowerShell 5.1 | Included with Windows 10/11; needed for `build.ps1`, `test.ps1`, `release.ps1` |
| Git | Needed only for versioning and `release.ps1` |

No Visual Studio, Windows SDK, NuGet, or `dotnet` CLI is required.

### Compiler discovery

`Build.bat` looks for the compiler in this order:

1. `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe`
2. `%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe`

If neither exists, the build stops with an explanatory message. `build.ps1` uses
the same discovery order when it writes `artifacts\build-info.txt`.

## Quick reference

| Command | What it does |
|---|---|
| `Build.bat` | Build and offer to run the exe |
| `Build.bat -q` | Build only; no prompt, no pause on error |
| `.\build.ps1` | Build and verify the binary version |
| `.\build.ps1 -Clean` | Remove the exe/artifacts, then build |
| `.\build.ps1 -Test` | Build, then run the smoke tests |
| `.\build.ps1 -Clean -Test -Package` | Full release-style build |
| `.\test.ps1` | Run smoke tests against the existing exe |
| `.\release.ps1 -Version 1.2.0 -DryRun` | Preview a release |
| `.\release.ps1 -Version 1.2.0` | Version, build, test, package, commit, tag |

PowerShell parameter names are case-insensitive. The aliases are `-c`, `-t`, `-p`,
`-q`, and `-h`.

## Bootstrap build: Build.bat

```bat
Build.bat
Build.bat -q
```

`Build.bat`:

1. Finds the .NET Framework compiler (64-bit first, 32-bit fallback).
2. Verifies that `SystemReport.cs` is next to the script.
3. Deletes any existing `SystemReport.exe` so a failed build cannot leave a stale
   binary.
4. Compiles with the flags below.
5. Offers to run the exe unless `-q` was used.

Compiler flags:

| Flag | Purpose |
|---|---|
| `/nologo` | Suppress the compiler banner |
| `/target:exe` | Build a console executable |
| `/platform:anycpu` | Run as 64-bit on 64-bit Windows, 32-bit otherwise |
| `/langversion:5` | Enforce C# 5 compatibility |
| `/optimize+` | Enable optimizations |
| `/warn:4` | Maximum warning level |
| `/out:SystemReport.exe` | Output path |
| `/reference:System.Management.dll` | WMI support |

The script is plain batch, so it works even if PowerShell is unavailable or
restricted. On failure it exits with code 1; with `-q` it does not call `pause`,
which makes it safe for CI.

## Full build: build.ps1

```powershell
.\build.ps1
.\build.ps1 -Clean
.\build.ps1 -Test
.\build.ps1 -Clean -Test -Package
.\build.ps1 -q
```

`build.ps1` performs these steps:

1. Reads `Cfg.Version`, `AssemblyVersion`, and `AssemblyFileVersion` from
   `SystemReport.cs`.
2. Requires a three-part display version (for example `1.1.0`) and an assembly/file
   version of `1.1.0.0`.
3. Optionally removes the exe, PDB, and `artifacts\`.
4. Invokes `Build.bat -q`.
5. Runs `SystemReport.exe --version` and compares it with `Cfg.Version`.
6. Optionally runs `test.ps1`.
7. Optionally creates the release package and checksum.

This catches the most common release mistake: bumping one version constant but not
the others.

### Parameters

| Parameter | Alias | Effect |
|---|---|---|
| `-Clean` | `-c` | Remove `SystemReport.exe`, its PDB, and `artifacts\` before building |
| `-Test` | `-t` | Run `test.ps1` after a successful build |
| `-Package` | `-p` | Create the zip, SHA-256 file, and build info in `artifacts\` |
| `-Quiet` | `-q` | Only errors and final artifact paths |
| `-Help` | `-h` | Show usage |

### Build verification

The build fails if:

- the source or `Build.bat` is missing,
- the compiler cannot be found,
- the compiler returns a non-zero exit code,
- the exe is not created,
- `--version` does not print `SystemReport <Cfg.Version>`,
- the version constants disagree,
- the smoke tests fail (when `-Test` is used).

## Smoke tests: test.ps1

```powershell
.\test.ps1
.\test.ps1 -ExePath .\SystemReport.exe
.\test.ps1 -KeepArtifacts
```

The tests exercise the command-line contract:

| Test | Checks |
|---|---|
| `--version` | Exit code 0 and the version from the source |
| `--help` | Documented options are present |
| Unknown option | Exit code 2 and an error message |
| Missing `-o` value | Exit code 2 |
| Full text report | File exists, has header, Summary, OS, and footer |
| Minimal report | File exists, has `Health:` and `OS:`, no boxed header |
| JSON report | Parses, has metadata, Summary/OS sections, warnings, notes |
| Redaction | PC name, user, serial (when present), and network secrets are hidden |
| Nested output | Missing parent folders are created |
| `--out=FILE` | Equals syntax is accepted |
| Directory output | Exit code 1 and a write error |

Tests write only to a unique directory under `%TEMP%`. On success it is deleted; on
failure it is kept and its path is printed. `-KeepArtifacts` keeps it even on
success.

The tests do not exercise the interactive menu, UAC elevation, or clipboard
contents. Those require a real desktop session and are verified manually.

## Packaging

```powershell
.\build.ps1 -Clean -Test -Package
```

Outputs:

```text
artifacts\
├─ SystemReport-1.1.0-win.zip
├─ SystemReport-1.1.0-win.zip.sha256
├─ build-info.txt
└─ stage-<guid>\            (temporary, removed after packaging)
```

The zip contains:

- `SystemReport.exe`
- `SystemReport.cs`
- `Build.bat`, `build.ps1`, `test.ps1`, `release.ps1`
- `README.md`, `CHANGELOG.md`, `CONTRIBUTING.md`
- `docs\` with all guides
- `build-info.txt`

`build-info.txt` records the version, build time, Git commit/describe, compiler
path, PowerShell version, and OS.

### Verifying the checksum

PowerShell:

```powershell
$expected = (Get-Content .\artifacts\SystemReport-1.1.0-win.zip.sha256 -Raw).Split()[0]
$actual = (Get-FileHash .\artifacts\SystemReport-1.1.0-win.zip -Algorithm SHA256).Hash.ToLowerInvariant()
if ($expected -ne $actual) { throw 'Checksum mismatch' }
```

Command Prompt:

```bat
certutil -hashfile artifacts\SystemReport-1.1.0-win.zip SHA256
```

The `.sha256` file uses the common `<hash>  <filename>` format.

## Releases: release.ps1

`release.ps1` automates a tagged release. It requires:

- a clean Git working tree,
- a `CHANGELOG.md` section named `## [<version>]`,
- a new Git tag (unless `-NoTag` is used).

Preview first:

```powershell
.\release.ps1 -Version 1.2.0 -DryRun
```

Then release:

```powershell
.\release.ps1 -Version 1.2.0
```

The script:

1. Validates the version and changelog entry.
2. Updates the three version constants in `SystemReport.cs`.
3. Runs `build.ps1 -Clean -Test`.
4. Commits all changes as `Release v1.2.0` (or `-Message`).
5. Creates the package from the committed tree.
6. Creates the annotated tag `v1.2.0`.
7. Prints the push command.

Options:

| Option | Effect |
|---|---|
| `-SkipTests` | Skip smoke tests (not recommended) |
| `-NoTag` | Commit without creating a tag |
| `-DryRun` | Validate and print the plan; change nothing |
| `-Force` | Allow the source version to already equal the target |
| `-Message` | Custom commit message |

If the build or tests fail, the script restores `SystemReport.cs` to its previous
version and stops before committing, so the working tree stays clean; fix the cause
and rerun the same command. If `git add` or `git commit` fails, the version bump is
left in the working tree for inspection. If the commit succeeds but packaging fails,
the commit remains and no tag is created: fix the problem, run
`.\build.ps1 -Package`, and create the tag manually. If tagging fails, create the tag
manually after fixing the cause:

```bat
git tag -a v1.2.0 -m "SystemReport 1.2.0"
```

## Continuous integration

The repository includes `.github/workflows/build.yml` for GitHub Actions. On each
push and pull request it:

1. Checks out the repository on `windows-latest`.
2. Runs `Build.bat -q`.
3. Runs `test.ps1`.
4. Runs `build.ps1 -Package -Quiet`.
5. Uploads the zip as an artifact.

The workflow uses the .NET Framework compiler that is present on the Windows runner,
so it does not install an SDK.

## Direct compiler invocation

If you need to bypass both scripts:

```bat
"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" ^
  /nologo /target:exe /platform:anycpu /langversion:5 /optimize+ /warn:4 ^
  /out:SystemReport.exe /reference:System.Management.dll SystemReport.cs
```

Run the result with `SystemReport.exe --version` to confirm it built.

## Troubleshooting

### "Could not find the .NET Framework C# compiler"

- Confirm `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe` exists.
- If only the 32-bit compiler exists, the script automatically uses
  `%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe`.
- Install .NET Framework 4.x if neither path exists.

### PowerShell says scripts are disabled

Use a per-process bypass:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -Clean -Test
```

Or, if your organization allows it:

```powershell
Set-ExecutionPolicy -Scope CurrentUser RemoteSigned
```

### The build succeeds but `--version` is wrong

Check all three constants in `SystemReport.cs`:

```csharp
public const string Version = "1.1.0";
[assembly: AssemblyVersion("1.1.0.0")]
[assembly: AssemblyFileVersion("1.1.0.0")]
```

`build.ps1` reports the mismatch before compiling.

### Packaging fails

`Compress-Archive` is part of PowerShell 5.1. If it is unavailable, update
PowerShell or create the zip manually from the listed package contents. Make sure
`artifacts\` is writable and no other process has the zip open.

### Antivirus flags the exe

The output is an unsigned, locally compiled .NET executable. Build it from source,
or submit it for review. The repository does not distribute a prebuilt binary.

## Why not MSBuild or the .NET SDK?

An `.csproj` would add a toolchain requirement (MSBuild or the `dotnet` SDK) and
generated intermediate files for a program that is one source file and one
reference. The in-box compiler is sufficient, faster to bootstrap, and keeps the
source usable by copying a single file to any Windows machine with .NET Framework
4.x. The scripts add the repeatable checks (version consistency, tests, packaging,
tags) that a project file would otherwise provide.

## Reproducibility notes

- The compiler version varies with the installed .NET Framework, so byte-for-byte
  reproducible builds across machines are not guaranteed.
- The exe is not code-signed. Sign it after building if you distribute it broadly.
- `build-info.txt` records the exact compiler path used for a package.
- The source has no generated files and no external package lockfile.
