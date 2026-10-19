# Eario

Eario is a lightweight, open-source Windows tray utility for routing open apps between audio output devices.

## Features

- Drag an app icon between output-device cards to change where it plays.
- Remember per-app routes, including before an app starts playing audio.
- Change device master volume from the panel.
- Double-click a device to make it the Windows default.
- Hide unwanted apps and devices, then restore them from the tray menu.
- Follow the Windows light or dark app theme.
- Run quietly in the notification area with no taskbar entry.
- Start automatically for the current Windows user.

Eario discovers active render endpoints through the Windows Core Audio APIs every time the panel opens. It contains no machine-specific device names or IDs.

## Requirements

- Windows 10 or Windows 11, x64

## Install

Download the latest `Eario-<version>-win-x64.msi` release and open it. The installer places Eario in your local application-data folder and adds a Start menu shortcut. It is self-contained, requires no separate .NET installation, and does not request administrator access.

Launch Eario from the Start menu after installation. Left-click the notification-area speaker icon to show the routing panel. Right-click the icon for hidden-item lists and Exit. Eario adds itself to the current user's Windows startup list when it first runs.

## Build from source

Install the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0), then run:

```powershell
dotnet build -c Release
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o dist
```

The local development executable will be created at `dist/Eario.exe`.

## Build the MSI installer

The installer source and build tooling live in [`installer/`](installer). The build uses WiX Toolset 5 through its .NET SDK package, publishes a self-contained Eario payload, and embeds it in a per-user MSI:

```powershell
.\installer\build-installer.ps1 -Version 1.0.4
```

The distributable installer will be created at `artifacts/installer/Eario-1.0.4-win-x64.msi`. Publish the MSI rather than the standalone executable; end users do not need the .NET runtime or the source repository. The installed app is compressed while remaining self-contained, so users do not need to install .NET separately.

## Privacy

Eario does not include telemetry, network communication, accounts, or cloud services. Device names, process names, hidden-item preferences, and audio routes remain on the local Windows account. Hidden-item preferences are stored under `HKCU\Software\Eario`; audio routes use Windows' own per-application audio preference store.

## Compatibility note

Per-application routing uses the internal Windows audio-policy interface also used by Windows audio-routing tools. Microsoft does not publish this interface as a supported public SDK contract, so a future Windows update could require an interoperability update.

## License

Eario is available under the [MIT License](LICENSE).
