# AGENTS.md

## Build & test (Windows)

```powershell
dotnet build src/PrintBridge/PrintBridge.csproj -c Release
$env:DOTNET_ROLL_FORWARD='Major'; dotnet run --project tests/Routing -c Release   # only needed if .NET 8 runtime is missing
powershell -NoProfile -ExecutionPolicy Bypass -File tests/PublicFirewall.Tests.ps1
dotnet publish src/PrintBridge/PrintBridge.csproj -c Release -r win-x64 --self-contained true -o publish
```

- `tests/Routing` compiles selected source files directly (see `Routing.csproj`) and fakes `WindowsPrinterBridge`; keep new testable logic free of WinForms/WinRT so it can be included there.
- `.ps1` files containing Vietnamese text must be saved as UTF-8 **with BOM**, otherwise Windows PowerShell 5.1 misparses them.
- Windows Defender may quarantine `obj\...\PrintBridge.dll` right after compile (false positive); the build then fails in `CreateAppHost`. Check `Get-MpThreatDetection`.

## Host footprint

Everything PrintBridge writes outside its exe is listed in `src/PrintBridge/Settings/HostIntegration.cs` and removed by `PrintBridge.exe --uninstall`. Keep that list in sync when adding new registry keys, files or firewall rules.
