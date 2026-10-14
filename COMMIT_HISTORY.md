# Development history (NEON PROTOCOL Unity project, branch r2-transform)

Exported from the full project repository. The full Unity project (models, textures, licensed third-party assets) is not included here: some files exceed GitHub's 100 MB limit and some third-party assets may not be redistributed. Built during Monad Metropolis (1 Sep – 13 Oct 2026).

```
2026-10-13 19:39 UTC  f3ca62d  Finish R6 combat route, door and extraction fixes, and Monad testnet integration
2026-10-13 08:38 UTC  6dd08a2  Label automated browser route and prevent public QA registration
2026-10-13 08:23 UTC  f80be3d  Clarify prototype Monad status and controller menu switching
2026-10-13 01:42 UTC  f9cbce4  R5: stable first-person views and experimental gamepad controls
2026-10-12 18:15 UTC  8181c37  R4 takeover note: build revision
2026-10-12 18:15 UTC  8b80be4  R4 takeover note: final build fad46c0, open items (bike model licence, hands check, FPS arms)
2026-10-12 18:15 UTC  f9139a0  R4 evidence (build fad46c0): full-route browser timestamps, editor QA regression, social e2e 25/25 (local chain), manifest, stills
2026-10-12 17:58 UTC  fad46c0  R4: founder's lightcycle model (cockpit view, rider arms IK on the grips, fingers curled), deep synth engine loop, walk-through walls sealed (core-room pillars, encounte
2026-10-12 16:32 UTC  21581b8  R4: restore the on-foot rig after a completed ride (VISIT THE VAULT / RETRY left player + camera rig disabled), inspect panel shown before releasing the mouse (vault eje
2026-10-12 15:40 UTC  5797976  R4 repair batch: core door passage, bike divider/steering, bike switch aim, result/vault UX, Raku menu identity
2026-10-12 09:45 UTC  ac60be4  R3 evidence (build 907e2df): encounter browser + Unity (with audio) recordings, acceptance checks, frame-rate checks, perf, social e2e incl. Vault MINT and wallet failur
2026-10-12 09:23 UTC  907e2df  Fix: Vault MINT used the mint(uint256) selector (0xa0712d68) but NeonMark has mint(uint32) (0xa71bbebe), so every UI mint would revert; found by the extended social test
2026-10-12 08:50 UTC  0e0d5b4  R3: encounter copy (STEAL THE CORE. GET OUT. / POWER THE IRIS / crouch hint), frame-rate + hitch control test (FR), encounter browser recorder
2026-10-12 08:41 UTC  998d514  R3: Vault MINT works for your own latest registered run (owner fetched from the record), wallet-link / mint-eligibility explained before registering, social test exercis
2026-10-12 08:30 UTC  ac62dbb  R3 encounter reset (NEON_NEXT/release/encounter-reset): one-floor encounter from Astra's measured layout (entry baffle, chamfered reactor mass, low peek / tall retreat /
2026-10-12 01:12 UTC  7cf19b7  R2 evidence re-run on the final build a6a67e1 (perf 14.75-14.8 ms avg / 22 ms p95; route video; marker fix verified)
2026-10-12 00:58 UTC  6e5d580  R2 evidence: manifest (build a6a67e1), featured-route perf 15.0-15.4 ms avg / 23 ms p95 (Low 1280x720 iGPU), social e2e 15/15 on local chain, before/after, FP vs TP, ass
2026-10-12 01:44 UTC  a6a67e1  R2: objective marker guides to the Relay Room exit while still in the room (no 300 m marker through walls)
2026-10-12 01:21 UTC  76b16f8  R2: Relay Room from the CC0 Quaternius Ultimate Modular Sci-Fi kit (palette-remapped URP materials, authored colliders, closed two-storey hall, mezzanine relay encounter
2026-10-11 22:09 UTC  bb988cb  R1 release-candidate evidence (build from 93cbb79): manifest, perf (Low 1280x720 iGPU: 19.1 ms avg / 31 ms p95 gameplay), social e2e 15/15 on local chain, perf scans, ri
2026-10-11 21:44 UTC  93cbb79  R1: ride HUD clears on-foot objective/hint/SPOTTED, featured ride copy (REACH THE FINALITY GATE, shards = bonus), shared art says REGISTERED; browser route recorder + ma
2026-10-11 21:18 UTC  c243630  R1: rig diagnosis harness (CRYPTO vs SWAT control, same clips, layers added one at a time), paint fade verified on screen (gone by ~9 s), help copy matches the real tag-
2026-10-11 20:58 UTC  38c7ca5  R1 perf: profiler found CharacterAnimator.LateUpdate = 7 ms/frame + ~14k allocs/frame (HumanTrait.MuscleName allocates a full array per access, and a full muscle-space G
2026-10-11 20:45 UTC  a32b8ed  R1: Vault uses the player camera (menu camera yields), artwork encodes the run's real rules version, shared page shows rules + relays out of the route's total and says R
2026-10-11 20:29 UTC  bce5368  R1: fix web menu (missing brace broke neon-ui.js since 40166a0; now checked in module mode); escape audit: bike-bounds sweep found the lane-choice divider (s 600-830) wa
2026-10-11 19:50 UTC  f2302a9  R1: walkable Vault (unarmed room: centrepiece + 6 frames, E inspect with REGISTERED / MINTED shown separately, collectors wing door opens only on a fresh verified NEON M
2026-10-11 19:27 UTC  40166a0  R1: featured judge route (rules v2: Relay A Iris -> Core -> short escape from s=120, shards optional; full adventure = rules v1, opt-in), one-goal HUD copy (PAINT THE LI
2026-10-11 18:52 UTC  5146c95  R1: paint fade (Astra patch, reviewed: hashes matched, applied with CRLF tolerance; compiles, 2 passes) + bright art direction (R1Art): warm off-white structure, blue-gr
2026-10-11 15:20 UTC  7578fdd  Gate 1/2: Iris-specific HUD hint, hide the objective marker within 7 m, narrow-window menu as one scrolling column, marker never under the objective bar; per-object ligh
2026-10-11 15:04 UTC  08c762a  Gate 1/2: keyboard-reachable web menus (Unity reads keys from the canvas only; menu/briefing/pause focus their primary button), ?test=g runs the first-minute routine in 
2026-10-11 14:42 UTC  cc50324  Gate 1: one-opponent first lesson and real ambient
2026-10-11 14:07 UTC  77b9f3e  Gate 1: Iris relay A (Astra's FBX mounted as visual child; numbered pads 1-2-3 in order, quiet reject, petals open, core hit -> ForceOnline + one Kind.Relay mark, power 
2026-10-11 08:50 UTC  56a163c  Route samples only on the Act I map; checkpoint-2 browser evidence (briefing, HUD, result, achievement + Hall of Fame pages)
2026-10-11 08:35 UTC  ecfc8d7  ACT III Finality Elevator (dais rises up a ring shaft, orbit camera, counts REAL Monad blocks observed during the ascent; timer stops at the gate); rival tag-outs credit
2026-10-11 08:24 UTC  d9271ce  Arrival art pass (panoramic windows over a procedural megacity + Monad spire, kit pillars/ribs/trusses/cables/rails, amber key light, shafts, dust, holo title; fog-free 
2026-10-11 07:58 UTC  5ce3a63  M2: all human rivals rebuilt on CRYPTO (same model/animator/rifle/IK) with faction suits; factions VECTOR (crimson, KESTREL+VANTA) vs HALO (gold, HALO+new ORBIT); AI tar
2026-10-11 02:42 UTC  ed6a809  Rivals re-bind their NavMeshAgent on Start (player-build load order); browser AI check script
2026-10-11 02:29 UTC  9b004aa  Rescue M1: CRYPTO turn-in-place via root-motion turn clips (dead-zone, no turntable spin), arms animated when relaxed (IK only when aiming), jump take-off/landing states
2026-10-11 01:23 UTC  b495dc8  Audit: START_HERE durations + commit reference
2026-10-11 01:15 UTC  3d457ab  Independent quality audit: evidence docs (gameplay reality, Monad reality, critical defects, feature status, hackathon readiness) + QA-only recording tools (AuditDirecto
2026-10-10 22:39 UTC  551513f  Thin light trail; browser ride test (Tools/webtest/ridetest.mjs) passes on the web build
2026-10-10 22:14 UTC  f397c3a  Bike: handlebar, light trail, speed streaks; practice entry + browser test 'finality'; handoff doc updated
2026-10-10 21:44 UTC  4304d6f  Finality Run finale: bike parks at a tunnel-fitted 3-arch Finality Gate with energy field, camera pulls back; vista towers kept clear of the track; take 4 recorded
2026-10-10 21:30 UTC  cdf4c5e  Full heist smoke test passes through Act II: relays, vault lock, Core, Finality Gate -> lightcycle ride -> extracted (0 runtime errors). Fix: hidden cage top kept an inv
2026-10-10 21:20 UTC  99a9a2d  Finality Run playable end-to-end in play mode: tunnel rendering fixed, bike model orientation fixed, rigid chase camera, CRYPTO riding pose (humanoid muscles), dust part
2026-10-10 19:38 UTC  4c0bb13  WIP Finality Run (Act II): lightcycle model, controller, drones, track builder, bike HUD, heist phase hook; vault warm light + dust. NOT play-tested; tunnel walls render
2026-10-10 19:33 UTC  ca0ea45  Fix player-build scene corruption (VaultNode MonoBehaviour moved to its own file; editor-only sphere mesh replaced). New web menu/HUD/result UI from the design mockups, 
2026-10-10 18:13 UTC  6836a0f  Core Vault art pass: Blender architecture kit (reactor dais, cradle, crown rings, ceiling frame, pillars, wall panels, pipes, rails, capacitors), panel texture set, beam
2026-10-10 17:56 UTC  1f013dd  Registry v2 (not deployed): art in event log instead of storage (measured 0.028 vs ~0.10 MON per registration), pause + owner handover; relay reads/verifies art from log
2026-10-10 17:41 UTC  c69bf51  Next-session handoff note
2026-10-10 17:25 UTC  04421b2  Live Monad layer A: observed mainnet block feed (public RPC, 1/s, backoff, LIVE/STALE/OFFLINE) + in-world Block Reactor kiosk at arrival; TMP fonts
2026-10-10 17:16 UTC  ebd29ec  CRYPTO rifle: custom Blender model, shoulder-anchored aim with two-arm IK + finger grip, mechanical reload (release/eject/fetch/insert/pressurize), weapon HUD card
2026-10-10 16:51 UTC  25fd892  CRYPTO (Mixamo Ch45_nonPBR) is now the player: humanoid import, material, rifle recalibrated
2026-10-10 16:45 UTC  176917c  Checkpoint: WIP Core Vault encounter (rings/scanners/shutters, untested) before handoff directive
2026-10-10 11:50 UTC  9a947e2  Rescue 1: runtime error control, character/weapon/camera repair, readable tag-out
2026-10-10 10:14 UTC  cf41638  Audit tooling only (camera clip probe, scripted proof take) - no gameplay changes
2026-10-10 10:05 UTC  a52b8d8  Checkpoint before production audit
2026-10-10 02:42 UTC  b7de0b0  Demo recording pipeline (Unity Recorder), gate light column, trench guide lights
2026-10-10 02:24 UTC  09b9940  Autopilot playthrough + fixes it found: full heist extracted in 93s through real controls
2026-10-10 01:35 UTC  8e8c3f9  Lighting pass: tile large blocks (Forward per-object light limit), zone key spots, physically scaled intensities
2026-10-10 01:30 UTC  1c79f51  Docs: README (submission write-up + disclosures), PERFORMANCE, CURRENT_STATE
2026-10-10 01:19 UTC  32e34a0  Music, objective markers, AI nameplates, alarm lighting, paint atlas 2x density
2026-10-10 01:08 UTC  b3ac78f  Level fixes from route checks: stair headroom, gaps, vault east door vs stairs, rails blocking bridge, NW leak
2026-10-10 00:50 UTC  f90fed4  Perf: remove GPU instancing (16s cold compile in Chrome D3D11), paintballs as one dynamic mesh
2026-10-10 00:30 UTC  d0c52d4  Art pass, community art wall, site (landing + gallery), perf: fewer shader variants
2026-10-10 00:12 UTC  72245ad  Gate 2: THE FRACTURE level, heist loop, rivals, drone, HTML HUD/menu/result
2026-10-09 23:47 UTC  6170928  Gate 1: test chamber, Mixamo competitor with procedural rifle IK, audio, registry contract, signature renderer
2026-10-09 23:12 UTC  759fc96  Gate 0: web build pipeline, load test, core controller/camera/paint systems
2026-10-09 22:54 UTC  f7ff7bf  Initial Unity 6 URP project: NEON PROTOCOL
```
