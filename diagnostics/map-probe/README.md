# U60 map probe — optional, read-only diagnostic

**Separate from Precision Pipette. It does not enable map transfers, dose material, alter food, read/write pipes, register native transfer callbacks, or intercept saves. No real ONI execution has been tested here.**

## Windows quick start / Windows 快速采样

1. Back up and open a **copy** of the colony. Build the probe from this folder as shown below (or extract the separately delivered diagnostic install ZIP); put its `OniU60MapProbe` folder in your existing `mods/Local` directory. Enable **U60 Map Probe (Read Only)** and restart once. The game menu's “browse save files” option helps find the user-data directory if Documents was redirected.
2. Pause. Use the ordinary inspection/select tool (not a construction/demolition tool). Click **U60 map probe / 地图只读采样**, then **Target / 目标**, then the food chamber's map cell. Source is optional; skip it for an already prepared preservation chamber.
3. Enter the game's actual build number once (from its menu/log; do not infer it from “U60”). Choose **10 / 15 / 50 ug** and the route label. **These are labels, not controls that set mass.** `observed-only` means no dosing action during the trace; `pump-down` and `vanilla-vent` must describe what you actually did. Use `fresh-before`, `prespoil-before`, `after-one-cycle`, or `after-reload` as the label.
4. Click **Capture / 连续采样**, then unpause at normal speed. It logs an immediate baseline plus 120 consecutive unpaused managed scheduler callbacks by default (nominally about four simulated seconds, not a guaranteed native tick count). It stops by itself. **Snapshot / 单次快照** takes one reading, including while paused. Capture is capped at 600 callbacks. Hiding the panel does not stop a running capture; Stop does.
5. After testing, **before restarting the game**, paste this one command into Windows PowerShell to export only probe lines to the Desktop:

```powershell
Select-String -LiteralPath "$env:APPDATA\..\LocalLow\Klei\Oxygen Not Included\Player.log" -SimpleMatch 'PPMAP|' | ForEach-Object { $_.Line } | Set-Content -LiteralPath (Join-Path ([Environment]::GetFolderPath('Desktop')) 'ONI-MapProbe.txt') -Encoding UTF8
```

Send `ONI-MapProbe.txt` plus a screenshot of the selected food's freshness, storage status and temperature. A phone can be used to send those exported files; no Python or compiler is required on the playing PC. If the Desktop export is empty, check the log for `[PPMAP]` startup errors; do not treat an empty file as success.

中文简版：只用备份的副本。启用独立只读模组后，暂停 → 普通查看工具 → 点“目标”选保鲜格 → 填实际游戏版本、剂量标签、操作来源 → “连续采样” → 解除暂停，自动停止。剂量按钮**不会设置质量**。用上面的 PowerShell 命令导出桌面 `ONI-MapProbe.txt`，连同食物新鲜度、深冻/无菌状态截图一起发送。重启游戏前先导出；重载后必须重新选格并标记 `after-reload`。

The log path and recommendation to collect it before relaunch are from [Klei's official support guide](https://support.klei.com/hc/en-us/articles/360052993772-Oxygen-Not-Included-DLCs-Logs-and-Useful-Information-for-Bug-Reports).

## What the records mean

Each run has a GUID, UTC timestamp, user build/route/case labels, application version, game assembly MVID, selected cell IDs, world ID and requested callback count. Each sample logs `GameClock.GetTimeAsDouble()`, render frame, managed callback ordinal and dt. Each of the 25 positions per watch logs element ID/hash, mass kg, temperature K, disease ID/count, solid/door flags, and validity. Mass and temperature use invariant round-trip numbers plus IEEE-754 single-precision hex bits; do not use rounded tooltip text to infer microgram equality. Example: 50 µg means `50e-9 kg`, not `50e-6 kg`.

A source watch adds another separately labeled 5×5 window. **Overlapping windows must not be summed twice.** Invalid, hidden, out-of-world or out-of-grid cells are marked unavailable; no claim of complete neighborhood coverage then applies. Sampling stops on world switch, selected-cell invalidation, load, cleanup or a read error. There is no persisted probe selection/state in the colony; reselect after reload.

`seq` counts managed observations, **not native sim ticks**. Grid is the game's published readback, potentially lagged/repeated and not an atomic command receipt. Other sim work can occur between observations. Unchanged samples cannot exclude transient writes, displacement outside radius two, or changes hidden between publications. Changed neighbors cannot by themselves attribute a cause. Logging may perturb frame timing. No callback success flag or observation alone authorizes a retry/refund.

The probe does **not** log food freshness, item temperature, spoilage state or conduit inventory. Those need separately labeled screenshots/measurements. It cannot prove preservation from ambient temperature alone. See [ACCEPTANCE.md](ACCEPTANCE.md) before interpreting a trace.

## Optional analysis and rebuilding

A collaborator can run `python Analyze-MapProbe.py ONI-MapProbe.txt`. It reports center deltas and neighbor changes descriptively, and warns about incomplete traces. It does not issue a pass verdict or establish conservation/causality.

```sh
dotnet build MapProbe.csproj -c Release -p:OniManagedDir=/path/to/U60/Managed -o artifacts/local
mkdir -p artifacts/OniU60MapProbe
cp mod.yaml mod_info.yaml artifacts/local/OniU60MapProbe.dll artifacts/OniU60MapProbe/
PROBE_FIXTURE="$PWD/artifacts/offline-fixture.log" dotnet run --project tests/Probe.Tests.csproj -c Release
python3 tests/test_log.py
```

PowerShell (start in `diagnostics/map-probe`):
```powershell
$managed = 'D:\SteamLibrary\steamapps\common\OxygenNotIncluded\OxygenNotIncluded_Data\Managed'
dotnet build MapProbe.csproj -c Release "-p:OniManagedDir=$managed" -o artifacts/local
New-Item -ItemType Directory -Force artifacts/OniU60MapProbe | Out-Null
Copy-Item mod.yaml,mod_info.yaml,artifacts/local/OniU60MapProbe.dll artifacts/OniU60MapProbe
```
Only copy the folder after a successful build. The `artifacts/OniU60MapProbe` folder is the optional mod.

Offline checks: 28 probe lifecycle/format checks and five parser/read-only-surface checks passed. Both available U60 reference builds compile with zero warnings/errors. These use public reference assemblies and test doubles, **not the native game**, Harmony execution, scheduler registration or food simulation. `artifacts/offline-fixture.log` is synthetic test output, not player evidence.

[API findings and sources](API-FINDINGS.md) · [Acceptance plan](ACCEPTANCE.md)

## Moono control compatibility

The diagnostic uses ordinary horizontal `GUILayout.Button` groups for dose and route labels, not `SelectionGrid`. Invalid selection indexes are bounded before drawing or logging. This targets the Moono handwritten-IMGUI limitation identified in review (v2026.11.20.04 / commit 1043305e44fdf265cb0d16e53ef9659d8e4a7962). It is a narrow source-level adaptation, **not a claim of Moono runtime compatibility**; the remaining window, text fields, lifecycle hook and sampling need an actual Moono session.
