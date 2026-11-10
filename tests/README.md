# Integration tests

`Velox.IntegrationTests` checks that every optimization and every Windows tweak (the *Windows tweaks* section) can be turned on and off and that Windows is left **exactly** as it was.

For each available optimization it:

1. takes a snapshot of everything Velox can touch (registry values, services and their delayed-start flag, scheduled tasks, power plan settings, hibernation, power mode, animations, display refresh rate...);
2. turns it on and off (or off and on if it was already on);
3. takes a new snapshot and reports any difference.

It uses a temporary backup store in `%TEMP%\Velox-IntegrationTests`, never the real one in `C:\ProgramData\Velox`.

> These tests really change system settings for a few milliseconds each, then restore them. Run them manually, as administrator, on a machine you own. They are built on CI but not run there.

```powershell
dotnet build tests\Velox.IntegrationTests -c Release
# from an elevated terminal:
tests\Velox.IntegrationTests\bin\Release\net48\Velox.IntegrationTests.exe result.txt
```

Options: `--skip=Id1,Id2` skips optimizations (tweaks use the id `tw.Name`, e.g. `tw.DarkMode`) and `--tweaks` tests only the Windows tweaks. `RefreshRate` is always skipped because it would change your monitor's refresh rate.

Registry values whose Windows default is known (a missing value behaves like a specific value) are compared by their effective value: «missing» and «set to the factory value» do exactly the same in Windows.

Exit code `0` means every optimization passed.

## Driver tests

`Velox.IntegrationTests.exe result.txt --drivers` runs the **real** driver update flow (search, restore point, download, signature check, install) with the same engine the UI uses, in two phases with automatic stop: Windows Update drivers first, then NVIDIA. It installs drivers for real, so only run it on your own machine, as administrator.

Options: `--no-wu`, `--no-nvidia`, `--only=<text>` (only devices whose name contains the text) and `--rollback=<text>` (roll back the driver of the matching device instead of updating).

Note: right after a driver change, Windows keeps reporting the old version through WMI (`Win32_PnPSignedDriver`) until the next restart. The real version is the PnP property `DEVPKEY_Device_DriverVersion`.

## Smoke test

`Velox.IntegrationTests.exe result.txt --smoke` (as administrator) runs every other section for real and leaves the system as it was: system scan and findings, in-app diagnostics (ping, network info, DNS flush, disk health, battery), memory lists / free RAM / trim a process, temp files and shader cache cleaning, restore point, startup apps on/off (with a temporary entry), process priority and end task (on a copy of `PING.EXE`, and `csrss` must be refused), permanent game optimization, updater check, driver inventory/search/safety blockers, winget installed list and search, uninstall lists (desktop and Store), disk scan of the user profile and folder protection, Explorer restart and settings save/load. It never installs or uninstalls programs or drivers.

## Unit checks

`Velox.IntegrationTests.exe result.txt --unit` runs pure-logic checks that change nothing on the system: firmware filter, version rules, Windows Update device matching, NVIDIA catalog names and download URLs, Windows Update / NVIDIA duplicates for the same GPU, and the temporary files cleaner (on a scratch folder). CI runs them on every build.
