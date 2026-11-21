# API diagnostic: no verified strict conditional map placement

## Conclusion and boundary

**No genuinely strict single-cell, conditional, no-displacement/no-spill placement contract was established from the inspected U60 public interface and open-source call sites.** This is not proof that no internal implementation could ever provide one. The U60 native library and a real runtime are absent; the supplied assemblies are stripped references. Keep map mutation disabled. This diagnostic does not add a replacement sim or a mutating harness.

For safe transfer, an operation would need to atomically test the target's live native state, refuse incompatible occupancy without altering it or neighbors, account for the actual representable delta, and provide an unambiguous completion identity. A managed Grid precheck followed by an asynchronous enqueue does not establish that contract. Even verified 1×1 source consumption would not make the separate destination operation conditional or atomic with it.

## Direct U60 metadata evidence

`evidence/api-signatures.txt` was generated with the included System.Reflection.Metadata reader. It contains both beta/post `Assembly-CSharp` and firstpass interfaces, parameter defaults, callback fields, and method RVAs. Relevant methods have RVA=0: there are no implementation bodies here to inspect. Hashes are in `evidence/reference-sha256.txt`.

Reference snapshots from Sgt-Imalas/Sgt_Imalas-Oni-Mods:

- [beta Lib](https://github.com/Sgt-Imalas/Sgt_Imalas-Oni-Mods/tree/d3cc380348397a553745b4bd6b1bb6c5e2ea98b4/Lib)
- [post-update Lib](https://github.com/Sgt-Imalas/Sgt_Imalas-Oni-Mods/tree/dcc52615c856dd63c96ffdf320acb11fca795dcf/Lib)

These are snapshot labels, not a verified mapping to the player's exact executable build (such as a menu/log reporting 764025). Record the actual game version.

| Surface found in both sets | What it establishes | What it does not establish |
|---|---|---|
| `ConsumeMass(cell, element, mass, byte radius, callback)` and `(cell, element, mass, byte width, byte height, callback)` | Explicit source element and geometric parameters exist | Native extent/zero/loss semantics; atomic coupling to emission |
| `EmitMass(cell, elementIndex, mass, temperature, germs, callback)` | Destination request and callback index | No strict/no-spill flag or expected-state token is exposed in this signature |
| `MassEmittedCallback` fields: callbackIdx, elemIdx, suceeded, diseaseIdx, mass, temperature, diseaseCount | Receipt payload shape | No destination-cell field; payload meanings are not documented by metadata |
| `ModifyCell(..., ReplaceType, bool do_vertical_solid_displacement, callback)` | A replacement enum and a narrowly named displacement flag | `false` is not evidence of “never displace gas/liquid”; `None` is not an atomic compare-and-set guarantee |
| `ReplaceType.None=0`, `Replace=1`, `ReplaceAndDisplace=2` | Available enum values | Runtime behavior or retry safety |
| `AddRemoveSubstance`, `ModifyMass`, `ReplaceElement`, `ReplaceAndDisplaceElement` | Other mutation APIs exist | No demonstrated non-destructive conditional placement/transaction contract |

## Open-source call sites actually inspected

Local public source commits: Sgt-Imalas `893d9dfe849e0c36c02329d8eb6193d948daa359`; peterhaneve `2faede0016540dd5bd994558333fb1d4e95148d3`. These show usage, not U60 semantics:

- [Slurpable.cs, lines 128–154](https://github.com/Sgt-Imalas/Sgt_Imalas-Oni-Mods/blob/893d9dfe849e0c36c02329d8eb6193d948daa359/OniRetroEdition/MultiTools/SlurpTool/Slurpable.cs#L128): uses the radius overload and creates a chunk from the consumption receipt. Does not establish emission or no-neighbor-change behavior.
- [ThrusterPoweredLander.cs, lines 148–152](https://github.com/Sgt-Imalas/Sgt_Imalas-Oni-Mods/blob/893d9dfe849e0c36c02329d8eb6193d948daa359/Rockets-TinyYetBig/TODO/__LegacyContentToSortOut/LandingLegs/ThrusterPoweredLander.cs#L148): legacy exhaust call to EmitMass without a receipt. Not precision-placement evidence.
- [Yggdrasil.cs, lines 116–123](https://github.com/Sgt-Imalas/Sgt_Imalas-Oni-Mods/blob/893d9dfe849e0c36c02329d8eb6193d948daa359/CoreMolior/Yggdrasil.cs#L116): explicitly replaces cells while constructing a world; not a transfer primitive.
- [FilteredDestroyTool.cs, lines 187–194](https://github.com/peterhaneve/ONIMods/blob/2faede0016540dd5bd994558333fb1d4e95148d3/SandboxTools/FilteredDestroyTool.cs#L187): uses ReplaceElement to destroy material. Unsuitable as a conservative delivery mechanism.
- [FastTrack MetricsPatches.cs](https://github.com/peterhaneve/ONIMods/blob/2faede0016540dd5bd994558333fb1d4e95148d3/FastTrack/Metrics/MetricsPatches.cs#L276): instruments the managed ISim33ms updater. Supports using that observation cadence; does not identify native ticks.

## Independent reimplementation: corroboration, not U60 proof

The third-party [oni-sim-vanilla README](https://github.com/Salacious-Oni-Dev/oni-sim-vanilla/blob/af528bfd3eaf7274467f7d7bded87f8f7c58b7e2/README.md) describes an independent replacement and differential testing against build **744825**, not this player's U60 runtime. No replacement library is bundled or installed.

At the same commit, [EMITTERS.md](https://github.com/Salacious-Oni-Dev/oni-sim-vanilla/blob/af528bfd3eaf7274467f7d7bded87f8f7c58b7e2/docs/EMITTERS.md#massemission) describes displacement for occupied targets. [ApplyMassEmission](https://github.com/Salacious-Oni-Dev/oni-sim-vanilla/blob/af528bfd3eaf7274467f7d7bded87f8f7c58b7e2/sim/simdll.cpp#L614) attempts displacement before placing; its success receipt carries the requested mass, while the cell's floating-point delta is accounted separately. This demonstrates why a success flag, or even its mass field, is insufficient evidence for exact delivery in that implementation. It does not prove identical U60 behavior, universal rerouting, or the absence of another private API.

## What would justify revisiting map support

A separately authorized native U60 test harness would need command IDs, enqueue and completion observations, exact source/target/neighbor ledgers, target-state race tests, same-element float-absorption tests, occupied-target/open-neighbor tests and save/callback-loss handling. Passing one isolated hydrogen example is insufficient. The read-only probe supplies useful Grid evidence for such work, but cannot execute the command, observe every native substep or prove a universal safety property.
