# Precision Pipette — U60 / Moono test branch

**v0.2.2-u60-moono-local · U60 763796 or newer · test branch, not a release.** This branch was built against two U60 reference sets and checked against Moono v2026.11.20.04 source. Actual gameplay on either player has not been verified.

[中文安装与验收](README-Moono.zh-CN.md) · [optional read-only map probe](diagnostics/map-probe/README.md)

**MAP TRANSFERS ARE DISABLED, including extraction, placement and return.** Map selection and inspection remain, with map and pipe kept as distinct compartments. Any new request involving a map endpoint refuses **before debit**. This candidate cannot directly prime map cells, deep-freeze food cells or place map liquid locks. The asynchronous native ConsumeMass/EmitMass path was removed: an EmitMass success flag cannot verify exact cell, amount or absence of rerouting; a missing callback cannot establish failure. No timeout, retry, refund or unlock guesses that outcome.

The shared gas/liquid panel retains **pipe-to-pipe** doses: gas 10 µg–1 kg, liquid 1 mg–10 kg, subject to representable float mass changes. Research-gated temperature adjustment remains limited to 20 g and same-phase temperatures. This does not modify food spoilage or maintain temperature. Pipe output is not a claim that vanilla vents deliver exact map doses.

## Safety behavior

- Resolve required declared SaveGame.OnPrefabInit / Game.CanSave hooks before patching. Missing hooks or failed installation disable transfers; no unconditional Harmony PatchAll attributes remain. If only the save guard is missing, the read-only component still attaches to retain old buffers. If the component-install hook itself is absent, buffer preservation cannot be assured: do not re-save that colony with this candidate.
- Synchronous pipe receipts are checked against actual mass and germ deltas. Verified partial receipts keep exact residual mass and actual remaining germs; mixed germ species and count overflow refuse. A zero receipt with no mutation is retryable. No guessed proportional rounding of residual germs.
- Unknown native results or exceptions quarantine the operation. No retry, refund, clearing or automatic unlock. Request context and quarantine state are serialized defensively. The displayed recovery is to leave without saving and reload a known pre-transfer backup, losing subsequent progress. A quarantined buffer is **not** a claim that all its displayed mass still exists exclusively in storage.
- Normal transfers and verified held buffers do not block saves. Quarantine uses Game.CanSave and, where present, ClusterManager.AddSaveBlocker. The beta reference set lacks the latter API. **Autosave and save-and-quit coverage is unverified.** There is no throwing serializer patch and no claim that every save route is blocked. Keep a separate backup; do not save a quarantined session. If a save nevertheless captures quarantine, reload retains the lock rather than authorizing another credit.
- v0.2 empty buffers normalize stale temperature/germ metadata. Nonempty legacy buffers keep their fields and can be explicitly delivered to a compatible pipe at original temperature; changed-temperature delivery still checks research. Legacy automatic return is disabled because origin-world identity was not saved. New buffers record and recheck origin-world identity before return. Map return is always refused. Invalid nonempty buffers are retained without enabling transfers.
- A v0.2 operation whose callback was already lost cannot be reconstructed by this upgrade. Exit v0.2 normally with no transfer outstanding before switching; retain an earlier backup.

## Clone and build

On the desktop with the game installed, use .NET SDK 8 or newer. In Steam, browse the game files and find the `OxygenNotIncluded_Data/Managed` folder containing `Assembly-CSharp.dll`. If you use Moono, use **the U60 game installation's Managed folder**, not the replacement player's folder. No game DLLs are bundled.

PowerShell:
```powershell
git clone --branch test/u60-moono --single-branch https://github.com/imhlq/oni-precision-pipette.git
cd oni-precision-pipette
.\build.ps1 -ManagedDir 'D:\SteamLibrary\steamapps\common\OxygenNotIncluded\OxygenNotIncluded_Data\Managed'
```

Linux/macOS: `bash build.sh /path/to/OxygenNotIncluded_Data/Managed`.

Unzip `dist/OniPrecision-install.zip`; put its `OniPrecision` folder in the existing `mods/Local` folder and restart. Use a copied test save and only one installation. See [Klei's log locations](https://support.klei.com/hc/en-us/articles/360052993772-Oxygen-Not-Included-DLCs-Logs-and-Useful-Information-for-Bug-Reports) if startup fails. The independent read-only diagnostic is optional and is **not** included in this install ZIP.

Offline checks:
```sh
dotnet run --project tests/TransferPlan.Tests.csproj -c Release
dotnet run --project tests/adapter/Adapter.Tests.csproj -c Release
dotnet run --project tests/references/References.Tests.csproj -c Release -- /path/to/Assembly-CSharp.dll
```

Adapter tests compile the actual PrecisionMod.cs against handwritten offline doubles. They exercise control flow and receipts, **not native game semantics, Harmony execution or KSerialization save migration**. Metadata checks and clean builds against both available U60 reference sets do not establish runtime compatibility. Reference provenance: Sgt-Imalas/Sgt_Imalas-Oni-Mods Lib snapshots d3cc380348397a553745b4bd6b1bb6c5e2ea98b4 (u60-beta) and dcc52615c856dd63c96ffdf320acb11fca795dcf (u60-post).

## Manual U60 smoke checklist (not run)

1. Back up a disposable U60 save, install only this local mod, load/reload, check one shared panel and no Harmony/load errors. Verify normal manual save, autosave and save-and-quit.
2. In isolated gas pipes, transfer 50 µg from a suitably light source into an empty pipe; check source delta, destination delta, temperature, and empty buffer. Repeat with a 30 g liquid dose and save/reload.
3. Try all map routes and map return: refusal with no debit or nearby changes. Try foreign elements, full pipes, paused sim, dismantled targets and identical endpoints.
4. Before research, reject changed temperature. After research, test a small compatible dose; reject >20 g and phase-changing temperatures. This candidate does not deliver to a food cell.
5. Recover a genuine v0.2 empty buffer with stale metadata and a nonempty map-origin buffer; explicitly deliver the latter to a compatible pipe at original temperature. Confirm legacy auto-return refuses. New pipe buffers should save/reload and return only to the original world/pipe.
6. With a controlled test harness, test refusal, partial acceptance with germs, and native exceptions/invalid receipts. Confirm known remainders persist and unknowns cannot retry. Inspect the save blocker, autosave and save-and-quit without overwriting the backup; recover unknowns by reloading that backup. Repeat on the same U60 game build under both the stock and Moono players before considering release.

See [中文说明](README.zh-CN.md). The public `main` branch and v0.2.0 release are unchanged by this test branch.
