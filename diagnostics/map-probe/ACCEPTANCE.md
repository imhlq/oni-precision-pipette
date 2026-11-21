# Useful acceptance observations for microgram hydrogen preservation

**No tests below have been run in the real game here. This probe does not produce any dose.** Separate “a chamber preserves food” from “a transfer operation reliably places material.” Passing the former cannot authorize the latter.

The [player build report](https://www.reddit.com/r/Oxygennotincluded/comments/1vg4d5v/microgram_hydrogen_food_preservation/) recommends below 20 µg, achieved by pump-down, with a conductive radiant gas pipe. It is a player observation, not a U60 specification or evidence for ordinary vents/direct emission. Therefore test **10 or 15 µg** separately from **50 µg**. Record actual measured mass; do not rename a 15 µg result a 50 µg success.

## Minimal useful run

Prepare a copied save with a thermally controlled, isolated single map cell and the intended cooling arrangement. Record pipe material, pipe gas/temperature, surrounding materials, food type/mass/initial freshness, transit method and actual game build/DLC/mod list. Photograph the layout once. Keep doors, pumps/vents and unrelated gas sources inactive during the observation window except the specifically labeled preparation action.

1. Pause before food arrival. Set the probe label `fresh-before` and the measured nominal case; choose `observed-only` if setup is already complete. Capture baseline plus 120 callbacks at normal speed. Target must read Hydrogen with positive microgram mass; record the exact kg/bits, temperature and neighbors.
2. Introduce the designated fresh food through the normal intended route. Capture `fresh-arrival`. Separately record the **food's own temperature**, freshness and the game's displayed **Deep Frozen** and **Sterile Atmosphere** statuses. Ambient gas temperature alone is not a food result.
3. Record another snapshot/capture after one cycle and ten cycles. Log freshness and status at each checkpoint, including any gas species change. These are finite-window observations, not proof of perpetual preservation; snapshots cannot prove conditions between them.
4. Save, reload the same copied save, reselect the target, and capture `after-reload` immediately and after another cycle. Compare mass bits, element, temperature and food state to pre-save observations. Do not confuse natural post-unpause diffusion/heat exchange with a serialization defect.
5. In a separate copy, repeat with **food already near spoilage**, including its transport/arrival delay. Observe until that same food would spoil in the matched control or through the declared test horizon. Capture `prespoil-before` / `prespoil-arrival` and inspect rot, off-gassing, polluted oxygen, status loss and freshness. Fresh-food success does not cover near-rotten arrivals.

For pump-down, final mass is the observed residual, not an accurately dosed input. If an exact nominal dose cannot be prepared, report the measured value and do not claim that nominal case passed. An optional ±5% grouping (e.g. 47.5–52.5 µg for the 50 µg exploratory case) is only an experimental bin, **not** proof of exact transfer. Preserve exact raw numbers regardless.

A useful positive preservation result is: the declared chamber/mass/route/food case retains the required in-game storage statuses and has no freshness loss beyond observed display resolution during the declared horizon, including reload. Report transient failures and the measurement resolution; do not round them away. It does not guarantee future cycles, all food items, all U60 builds, nor correctness of any transfer code.

## Required distinctions and controls

| Run | Preparation and instrumentation | What it can support |
|---|---|---|
| No-mod control | All mods disabled, same prepared colony copy/layout, screenshots and timing | Actual vanilla gameplay baseline; no probe logs exist here |
| Probe-only control | Only this probe, matching copy and actions | Exact map observations; compare gameplay to no-mod control to detect instrumentation/timing effects |
| 10 µg or 15 µg pump-down | Clearly label pump-down; measure actual residual | Low-mass player-build hypothesis for that observed case |
| 50 µg pump-down | Separate copy and result, never pooled with <20 µg | Whether the requested 50 µg chamber works on this build |
| Vanilla vent | Separate branch, identify exact vent type and packet/pipe setup | Vent behavior only; not proof for EmitMass or precision pipette; probe does not measure pipe inventory |
| Pipe-only candidate present | Optional matching branch; map mutations remain disabled | Coexistence observations, not a map delivery test |
| Future direct-emission harness | **Not supplied by this probe**; separate explicit authorization/code | Native command behavior only after its own validation |

Also use a matched warmer/ordinary-storage control so that no observed freshness change over a short interval is not mistaken for preservation. Keep food type, initial freshness, amount, transit and timing comparable. Use the same actual executable build for comparisons; the supplied beta/post reference DLL labels do not identify the player's exact native executable.

## A future strict-placement acceptance test (not performed by this probe)

Begin with a stationary, isolated empty single cell and an independently measured source. Separately test an intentionally occupied target with an open neighbor, using a test-only mutating harness in a disposable colony. The occupied target and neighbor must remain unchanged on refusal; displacement is a failure of the strict contract even if total material is conserved. Also test target occupancy changing after enqueue, same-element targets with masses too large to represent the proposed increment, solid/door boundaries, pause/resume, delayed/lost callbacks and reload boundaries.

For a declared, representable requested float quantity q, useful exact-placement evidence would require the independently measured source debit and target credit to agree with q, no other cell/compartment changed by the operation, and a compatible target at commit time. A strict rejection requires no source debit, credit, replacement or neighbor displacement. The read-only probe cannot establish command timing/commit causality, inspect pipe inventory, or observe all native cells/substeps. A 5×5 window is diagnostic coverage, not world-wide conservation. Unknown callbacks stay unknown; no retry/refund is justified by absence of a visible change.

At minimum send the probe-only logs, layout/food screenshots, actual build and which preparation route was used. That is useful evidence even if it only narrows a failure; it is not a native pass certificate.
