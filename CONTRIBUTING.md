# Contributing to SystemReport

Thanks for improving SystemReport. The project is intentionally small and
dependency-free, so contributions should preserve that character.

## Before you start

- Read [ARCHITECTURE.md](docs/ARCHITECTURE.md) to understand the data model and
  collection pipeline.
- Build and run the smoke tests:

  ```powershell
  .\build.ps1 -Clean -Test
  ```

- Check that your change works in all three output modes:

  ```bat
  SystemReport.exe -n -o "%TEMP%\full.txt"
  SystemReport.exe -m -n -o "%TEMP%\minimal.txt"
  SystemReport.exe -j -n -o "%TEMP%\report.json"
  ```

## Design rules

1. **Stay on C# 5 and .NET Framework 4.x.** The build must work with the in-box
   `csc.exe`. Do not introduce NuGet packages, SDK-style projects, or new runtime
   dependencies.
2. **Keep `SystemReport.cs` self-contained.** It must remain compilable as a single
   file with `System.Management.dll` as the only reference.
3. **Do not transmit data.** The tool reads local state and writes local files. Do
   not add telemetry, update checks, or network calls.
4. **Fail soft.** A missing WMI class, unsupported property, or unavailable tool
   should produce `N/A`, an omitted field, or a warning, not a crash.
5. **Warn only about actionable problems.** Use `Cfg.Warn` for things a user can or
   should fix. Use `Cfg.Note` for neutral observations. Do not turn normal hardware
   variation into a warning.
6. **Protect personal data.** Any serial number, MAC address, IP, gateway, DNS,
   Wi-Fi name, username, or similar value must use `Block.Secret`, not `Block.Add`.
7. **Keep JSON keys unique.** The model suffixes duplicates automatically, but avoid
   designing fields that rely on collisions. Use a named block when a section needs
   repeated structures.
8. **No generated source.** Do not check in `SystemReport.exe`, PDBs, packages, or
   generated reports.

## C# 5 compatibility

The compiler is invoked with `/langversion:5`. These are **not** available:

```csharp
// Not allowed
string s = $"value: {x}";
var name = nameof(x);
public string Name => _name;
var item = list?.FirstOrDefault();
var (a, b) = GetPair();
void Local() { }
public string Name { get; } = "x";
```

These are fine:

```csharp
string s = "value: " + x;
string name = "x";
var item = list == null ? null : list.FirstOrDefault();
Func<int, int> square = delegate(int n) { return n * n; };
```

Use `var` only when the type is obvious from the right-hand side, matching the
existing style.

## Code style

- Four spaces, no tabs.
- Braces on the same line for control flow and methods, matching the existing file.
- Keep the existing comment banners (`// ---------- Section ----------`).
- Prefer the helpers in `U` and `W` over ad-hoc conversions.
- Use `U.IC` (invariant culture) for machine-readable formatting.
- Use `StringComparison.Ordinal` or `OrdinalIgnoreCase` for internal comparisons.
- Dispose WMI objects, registry keys, processes, and other `IDisposable` values.
- Keep user-facing strings plain and specific; avoid jargon where a clear phrase
  works.
- Do not reformat unrelated code in the same change.

## Adding a field or section

For a field inside an existing section:

```csharp
g.Add("Firmware mode", value);
```

For a personal value:

```csharp
g.Secret("Serial number", serial);
```

For a percentage:

```csharp
g.AddPct("Used", percent, detail);
```

For a repeated structure (one block per disk, adapter, monitor, and so on):

```csharp
Block b = s.G(name);
b.Add("Status", status);
```

For a new section:

1. Add `Collectors.GetExample()`.
2. Add it to the `steps` array in `Program.Collect`.
3. Add a `Brief` line if it belongs in minimal output.
4. Add warnings/notes only for real conditions.
5. Update the section table in `README.md`, `docs/USAGE.md`, and
   `docs/ARCHITECTURE.md` if the section is user-visible.
6. Add or update a smoke-test assertion if the section affects the command-line
   contract.

## Tests

The smoke suite is intentionally black-box:

```powershell
.\test.ps1
```

It covers:

- version and help output,
- invalid options and exit codes,
- full/minimal/JSON output,
- redaction,
- nested output folders,
- `--out=FILE`,
- directory-as-output failure.

If you add a new CLI option, add a test. If you add a new output field that is
important for automation, consider adding an assertion to the JSON test. Do not
make the suite depend on specific hardware values; this machine has different
hardware from the next one.

Manual checks for collectors:

- Run as a standard user and elevated.
- Try a machine with no battery, no Wi-Fi, or no TPM if available (or a VM).
- Confirm a missing provider becomes `N/A` or a warning.
- Confirm `-r` redacts the new personal fields.

## Commit messages

Use the imperative mood and a short scope:

```text
Add battery health warning
Fix clipboard encoding for non-ASCII names
Document JSON section shapes
Build: fail when version constants disagree
```

Keep one logical change per commit. Avoid mixing formatting-only changes with
behavior changes.

## Pull request checklist

- [ ] `.\build.ps1 -Clean -Test` passes.
- [ ] The change is C# 5-compatible.
- [ ] No new dependencies or network calls.
- [ ] Personal values use `Block.Secret`.
- [ ] Warnings are actionable; neutral facts use notes.
- [ ] JSON output parses and keys remain unique.
- [ ] Documentation is updated for user-visible changes.
- [ ] No exe, PDB, package, or generated report is included.

## Release process

Maintainers:

1. Update `CHANGELOG.md` with a `## [x.y.z] - YYYY-MM-DD` section.
2. Preview:

   ```powershell
   .\release.ps1 -Version x.y.z -DryRun
   ```

3. Release:

   ```powershell
   .\release.ps1 -Version x.y.z
   ```

4. Push:

   ```bat
   git push origin main --tags
   ```

`release.ps1` refuses a dirty tree, updates the version constants, runs a clean
build and tests, commits, packages the release, and creates the annotated tag.

## Reporting bugs

Include:

- `SystemReport.exe --version`
- Windows edition and build (`winver`)
- the exact command line used
- whether the process was elevated
- the relevant section of a **redacted** report (`-r`)
- any console error text and the exit code

Do not attach an unredacted report to a public issue. Reports contain machine
names, usernames, serial numbers, network addresses, and Wi-Fi names.

## License

No license file has been added yet. By contributing, you agree that your
contribution can be distributed under whatever license the project owner adopts.
Ask before contributing if you need a specific license.
