# Contributing

Thanks for taking the time. This project is deliberately small and dependency-free; please keep it
that way.

## Ground rules

- **C# 5 only.** The build uses `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe`, which is
  the compiler shipped with .NET Framework 4.x. No string interpolation, no `?.`, no expression
  bodies, no tuples, no pattern matching, no local functions. Keep the build working on a stock
  Windows 10 machine with zero setup.
- **No external packages.** `System.dll`, `System.Core.dll`, `System.Drawing.dll`,
  `System.Windows.Forms.dll`, `Microsoft.VisualBasic.dll` and nothing else.
- **The ACF is read-only input.** Never write to `appworkshop_431960.acf`.
- **Destructive operations must be reversible.** Anything that touches user files goes through the
  quarantine layer first.

## Before opening a PR

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
.\make-fixture.ps1

.\dist\WeCleaner.exe scan --lib .\test-fixture
.\dist\WeCleaner.exe quarantine --id 2222222222,3333333333 -y --lib .\test-fixture
.\dist\WeCleaner.exe restore --batch latest
.\dist\WeCleaner.exe purge --batch latest -y
```

The fixture must still classify `1111111111` as subscribed, `2222222222` as unsubscribed,
`3333333333` as no-owner, `4444444444` as suspicious, `9999999999` as a download fragment, and
ignore `notanid`.

## Reporting a misclassification

The single most useful artefact is the ACF excerpt plus the tool's own view of it:

```powershell
.\WeCleaner.exe paths
.\WeCleaner.exe scan --json --out report.json
```

Attach `report.json`. It contains no personal data beyond local filesystem paths; redact those if
you prefer.

## Localisation

The UI and CLI strings are currently Simplified Chinese and hard-coded. A clean i18n pass — resource
table, `--lang`, following the system UI language — would be very welcome. Please open an issue
first so we can agree on the shape.

## Code style

Match what is there: four spaces, braces on their own line, one class per concern, and comments that
explain *why* rather than *what*.
