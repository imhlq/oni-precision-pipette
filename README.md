# Yet Another Precision Pipette (YAPP)

Move a measured sample of liquid or gas between a map cell and a conduit in *Oxygen Not Included*. Draw water from a pipe, switch to the map, and dispense it above a liquid lock. The pipette uses the cell you select and keeps the sample if the target is invalid.

[Download v0.15.0](https://github.com/imhlq/oni-yet-another-precision-pipette/releases/download/v0.15.0/YAPP-install.zip) · [简体中文](README.zh-CN.md) · [MIT license](LICENSE)

## Install

For U60, game build **763796** or later. Exit the game, extract `YAPP-install.zip`, and copy the **OniPrecision** folder into `mods/Local` in your ONI user-data directory. On Windows this is usually `Documents\Klei\OxygenNotIncluded\mods\Local`. Enable **Yet Another Precision Pipette** in the Mods menu and restart when prompted.

## Take a sample

Choose **Plumbing → Liquid Pipette** or **Ventilation → Gas Pipette**. Both tools are available immediately in this version. The native hover card shows what is in the target cell; the separate sample readout shows the element, mass, and temperature in the pipette.

| Control | Action |
| --- | --- |
| Hold left / right for 0.65 s | Collect / dispense. Release early to cancel. Top up a partial sample with the same element. |
| Preset button or Ctrl + wheel | Select **10 µg**, **10 mg**, **10 g**, or **10 kg**. For a custom limit, enter 1–1000 and choose a unit. Each tool saves its own amount and unit with the colony. |
| Tab / backquote | Toggle the matching conduit overlay / return to the map. You can switch between pickup and delivery. |
| Esc | Leave the tool and keep the sample. |

Enable **Same element only** to limit delivery to empty or matching targets. Turn it off to drip liquid into air. Samples keep their original temperature by default; eligible samples up to 20 g can use a specified delivery temperature.

## Verified in v0.15.0

On Moono, U60 build **764025**, oxygen, water, and Visco-Gel each received **64 × 15.625 g** in one sealed cell. Each started at **1 kg** and finished at **2 kg**. All 192 deliveries matched the expected cell mass and emptied the pipette. After a full game restart, the gas and liquid settings restored independently at **35 mg** and **15 kg**.

Paused consecutive transfers, both directions of map/conduit transfer, and reloading a *held sample* still need gameplay verification. If the game reports an uncertain transfer, exit without saving and reload a pre-transfer backup.

## Build from source

Use .NET SDK 8 or newer and the game's `OxygenNotIncluded_Data/Managed` directory. On Windows run `.\build.ps1 -ManagedDir 'path\to\Managed'`; on Linux or macOS run `bash build.sh /path/to/Managed`. Both produce `dist/YAPP-install.zip`.

GitHub Actions runs the randomized planner stress test, adapter scenarios, and assembly reference checks before packaging. Pushing a tag matching `mod_info.yaml` publishes the tested ZIP. [Build workflow](.github/workflows/release.yml) · [Map diagnostics](diagnostics/map-probe/README.md)
