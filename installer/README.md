# MSI installer

`build-installer.ps1` publishes Eario as a self-contained x64 application and packages it in a per-user MSI using WiX Toolset 5.

From the repository root:

```powershell
.\installer\build-installer.ps1 -Version 1.0.4
```

The finished package is written to `artifacts/installer/Eario-<version>-win-x64.msi`. The MSI installs Eario under the current user's local application-data folder and creates a Start menu shortcut. It requires no administrator access and no separately installed .NET runtime.
