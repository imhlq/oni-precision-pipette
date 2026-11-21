# Yet Another Precision Pipette (YAPP)

Move measured samples of liquid or gas in *Oxygen Not Included*. Pick up a sample from a cell or conduit, carry it in the pipette, and dispense it at another target. To make a drop of water fall, collect water, turn off the same-element restriction, then dispense into air above the landing spot.

**Version 0.14.2** targets U60, starting with game build 763796. [简体中文](README.zh-CN.md) · [Moono compatibility notes (中文)](README-Moono.zh-CN.md) · [MIT license](LICENSE)

## Using the pipettes

Open **Plumbing → 取液器** for liquids or **Ventilation → 取气管** for gases. The tools are unlocked in this test version. In the panel on the right, set the fill limit and choose µg, mg, g, or kg. Enter a value from 1 to 1000, or use the slider for 1, 5, 10, 15, and so on.

| Control | Action |
| --- | --- |
| Hold left mouse for 0.65 s | Collect from the highlighted cell. A partially filled pipette can collect more of the same element. |
| Hold right mouse for 0.65 s | Dispense into a valid target. Release early to cancel. |
| Ctrl + mouse wheel | Cycle through the 10 µg, 10 mg, 10 g, and 10 kg presets. |
| Tab | Switch between the map and the matching conduit overlay. Use this between pickup and delivery to change the destination type. |
| Backquote | Return to the map view. |
| Esc | Leave the tool and keep the held sample. |

The native hover card describes the **target**. The sample readout describes what the pipette **holds**. Enable **仅允许同类目标** to restrict delivery to empty or matching targets. Keep the original temperature or set a delivery temperature for an eligible dose of up to 20 g.

## Build and install

Install .NET SDK 8 or newer and locate the game's `OxygenNotIncluded_Data/Managed` directory. Moono users should use the assemblies from the U60 game installation.

```sh
git clone https://github.com/imhlq/oni-yet-another-precision-pipette.git
cd oni-yet-another-precision-pipette
```

On Windows, run the following in PowerShell, adjusting the Steam library path.

```powershell
.\build.ps1 -ManagedDir 'D:\SteamLibrary\steamapps\common\OxygenNotIncluded\OxygenNotIncluded_Data\Managed'
```

On Linux or macOS, run `bash build.sh /path/to/OxygenNotIncluded_Data/Managed`.

1. Exit the game and extract `dist/YAPP-install.zip`.
2. Copy the extracted `OniPrecision` folder into the game's user-data `mods/Local` directory. On Windows the usual destination is `Documents\Klei\OxygenNotIncluded\mods\Local\OniPrecision`. The DLL and both YAML files belong directly inside that folder.
3. Enable **Yet Another Precision Pipette (YAPP)** in the Mods menu, restart when prompted, and load a copy of your colony for the first run.

## Compatibility and saves

On Moono with game build 764025, a copied colony loaded and the native oxygen tooltip showed the element, type, mass, and temperature after 15 rounds of switching between liquid and gas overlays. **Paused consecutive transfers, map-to-conduit transfers in both directions, and reloading a held sample still need in-game verification.**

Pipe transfers check the mass and germ counts actually received. A confirmed partial delivery leaves the remainder in the pipette. If the game returns an uncertain result, YAPP locks further transfers. Exit without saving and reload a pre-transfer backup in that case. Autosave and save-and-quit coverage still need verification. When upgrading from v0.2, finish outstanding transfers and keep a separate backup.

## Developer checks

```sh
dotnet run --project tests/TransferPlan.Tests.csproj -c Release
dotnet run --project tests/TransferPlan.Tests.csproj -c Release -- --stress
dotnet run --project tests/adapter/Adapter.Tests.csproj -c Release
dotnet run --project tests/references/References.Tests.csproj -c Release -- /path/to/Assembly-CSharp.dll
```

The stress run exercises one million randomized planner inputs. Adapter tests exercise transfer control flow with simulated game interfaces. The reference check inspects the installed game's assembly metadata. For observations inside a running colony, see the [optional read-only map probe](diagnostics/map-probe/README.md).

## Automated builds and releases

Pull requests and pushes to `main` run the tests and build the install ZIP. Download the `yapp-install` artifact from a successful run on the [Actions page](https://github.com/imhlq/oni-yet-another-precision-pipette/actions/workflows/release.yml) and extract the inner `YAPP-install.zip`.

To publish a version, commit its version number in `mod_info.yaml` and push the matching `vX.Y.Z` tag. After the tagged build passes, the workflow publishes `YAPP-install.zip` on the [Releases page](https://github.com/imhlq/oni-yet-another-precision-pipette/releases).
