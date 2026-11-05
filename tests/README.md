# Integration tests

`Velox.IntegrationTests` checks that every optimization can be turned on and off and that Windows is left **exactly** as it was.

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

Options: `--skip=Id1,Id2` skips optimizations. `RefreshRate` is always skipped because it would change your monitor's refresh rate.

Exit code `0` means every optimization passed.

## Driver tests

`Velox.IntegrationTests.exe result.txt --drivers` runs the **real** driver update flow (search, restore point, download, signature check, install) with the same engine the UI uses, in two phases with automatic stop: Windows Update drivers first, then NVIDIA. It installs drivers for real, so only run it on your own machine, as administrator.

Options: `--no-wu`, `--no-nvidia`, `--only=<text>` (only devices whose name contains the text) and `--rollback=<text>` (roll back the driver of the matching device instead of updating).

Note: right after a driver change, Windows keeps reporting the old version through WMI (`Win32_PnPSignedDriver`) until the next restart. The real version is the PnP property `DEVPKEY_Device_DriverVersion`.