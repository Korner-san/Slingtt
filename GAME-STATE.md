# Slingtt Evolution — Current State

**Last verified:** 2026-08-06. Build clean, 30/30 tests passing, live battle capture confirmed working.

This document describes what the game *is* and what actually exists right now in this project (`SlingTTEvolution_Godot_Repaired`, the Godot/C# port). For deep design rationale see the original web game's `docs/GDD.md` at `C:\Users\Acer\Desktop\SlingttEvolution` — this file summarizes and cross-checks against it, but that GDD is the fuller source of truth for anything not covered here.

---

## 1. Concept

**Slingtt Evolution** is a portrait, turn-based **physics battler**. Each turn you pull a hero back like a slingshot and release; the hero launches across a closed arena, ricochets off walls and enemies, comes to rest, then automatically fires an ultimate skill from wherever it stopped. Between battles you climb a tower of floors, pull weapons and armor from a gacha, and level/evolve them to unlock and upgrade those ultimates.

**Design pillars** (from the GDD):
1. **The pull is everything** — the single drag-back-and-release input has to feel precise and weighty; everything else exists to make that gesture more interesting.
2. **Loadout is the build** — heroes are chassis with a small stat spread and a passive; the *weapon* defines how you attack, the *armor* defines how far/safely you move. Power comes from evolving gear, not from hero rarity.
3. **Legible chaos** — ricochets are chaotic, but the player must always be able to trace what happened and trust the next shot can be planned.
4. **Console polish, no bloat** — loads fast, holds 60fps on mid-range hardware.

---

## 2. Core loop

- **Battle (seconds):** aim → release → watch the ricochet resolve → read the new board → aim again.
- **Floor (minutes):** enter arena → clear all enemies within the turn limit → collect resources → advance. Every 5th floor is a boss; every 20th is a shop.
- **Meta (sessions):** spend Sling Cores on gacha pulls → equip better weapon/armor → spend Evo Ore leveling toward the next evolution gate (10/20/30) → push deeper floors → earn more resources.

---

## 3. What's actually implemented right now

Verified directly (build + tests + a live `--autobattle --autoplay` capture), cross-checked against `REPAIR-REPORT.md`:

**Working:**
- Touch/mouse drag-back-to-launch slinging, with a trajectory preview built by running the real simulation forward (not a separate approximation) — includes the first 2 predicted wall bounces
- Swept continuous collision (no tunneling through fast-moving actors)
- All three weapon-vs-enemy travel behaviors, genuinely distinct (not unified): sword elastic-bounces with decaying damage, lance pierces straight through with a pierce budget, hammer stops dead on contact and detonates an AoE
- Weapon ultimates (Crossing Slash / Piercing Ray / Aftershock) firing automatically once travel fully resolves, from the final position
- Armor ultimates (Bulwark / Swift / Vital) firing once per battle, on first crossing below 50% HP
- Round-robin turn order with stun-skipping, a turn-order strip showing upcoming actors, and an active-turn indicator (pulsing ring)
- Enemy AI, HP/shield/heal, hit-stop + screen shake + camera punch on impact
- 5 floors including a floor-5 boss with a split/adds mechanic
- Floor-clear rewards, HP healing between floors, HP carried across floors within a run, run milestones
- Persistent save (a defeat resumes from the last checkpoint floor, not floor 1)
- Main menu, battle HUD, turn strip, results screen, defeat/retry flow
- Portrait-locked responsive layout with safe-area insets
- Fully procedural audio (no audio files at all — everything is synthesized)

**Not implemented** (also true of the original web game — not a regression, just unbuilt UI):
- Gacha pull screen/animation (rates and pity logic exist in `balance.json`, no UI)
- Shop screen (appears every 20 floors per design; no UI)
- Item upgrade/evolution UI (the leveling math exists; nothing to trigger it from)
- Hero roster management screen
- Firebase sync/auth (the web original is server-authoritative for gacha/economy; this port is fully offline/local)

**Known platform caveat:** ships on the `gl_compatibility` (OpenGL ES 3.0) renderer, not Vulkan. A Vulkan swapchain-presentation bug (`vkQueuePresentKHR` failing every frame) caused black screens on the dev's Android phone even though the engine, .NET runtime, and content all initialized correctly — confirmed via logcat. GLES3 was not independently confirmed on Android hardware (the local emulator couldn't boot to test it), so **two APKs ship**: `SlingTTEvolution.apk` (GLES3, install first) and `SlingTTEvolution-Vulkan.apk` (fallback if the first is black).

---

## 4. Battle system, in detail

- **Arena:** 9×16 world units, closed rectangle. Heroes start along the bottom band, enemies along the top. Walls are perfectly reflective (restitution 1.0) and wall bounces deal no damage.
- **Turn order:** `Hero1 → Hero2 → Hero3 → Enemy1...EnemyN`, repeating; dead actors skipped. Turn limit is 30 rounds — exceeding it is a defeat.
- **The sling input:** press the active hero, drag *away* from where you want to shoot (rubber-band style — real slingshot intuition), release. Draw ratio = clamp(dragDistance / 3.0 world units, 0.25, 1.0). Releasing below 0.25 draw cancels the turn instead of firing a weak shot (avoids mobile fat-finger misfires). Input is locked during resolution — no queuing.
- **Movement:** launch speed = 26 units/s × draw ratio, decays exponentially at a friction rate of 0.32/s. Travel ends when speed drops below 0.6 units/s **or** the equipped armor's `moveDuration` elapses, whichever comes first — hard-capped at 5 seconds regardless of stats. Wherever the hero stops is where it stays until its next turn; position between turns is a real strategic resource.
- **Weapon behavior on enemy contact** (wall behavior is identical for all three — always a clean elastic bounce):

  | Weapon | On contact | Damage |
  |---|---|---|
  | Sword | Elastic bounce, keeps traveling | 100% first hit, −10%/subsequent hit, floor 40% |
  | Lance | Passes through, no deflection | 90% per pierce, no decay, up to the weapon's pierce count |
  | Hammer | Stops immediately, detonates | 60% direct + 140% AoE center falling to 60% at rim |

  Same enemy can't be damaged twice within 0.08s (stops a wedged hero from draining an enemy in one frame).
- **Damage formula:** `raw = ATK × hitMultiplier × evolutionMultiplier`, `mitigated = raw × (1 − DEF/(DEF+300))`, `final = max(1, round(mitigated × variance))` where variance ∈ [0.95, 1.05] from the battle's seeded PRNG (so results are reproducible, not just "random").
- **Ultimates:** weapon ultimate unlocks at item level 10 (upgrades at 20/30), fires automatically once travel fully resolves — it is not a separate turn and can't be aimed. Armor ultimate unlocks the same way, fires once per battle the first time that hero drops below 50% HP (resolution order: damage → death check → armor ultimate, so a killing blow never also triggers it).
- **Victory/defeat:** victory when all enemies hit 0 HP. Defeat when all three heroes hit 0 HP or the turn limit expires — but a defeat only costs progress since the last checkpoint floor (every 10th floor banks automatically), so a bad run isn't a full reset.

---

## 5. Current content roster

**Heroes** (chassis — small stat spread + passive, unlocked by progression, not gacha):

| Hero | HP | DEF | Passive |
|---|---|---|---|
| Bram | 300 | 30 | +10% HP |
| Lyra | 220 | 20 | +10% ATK |
| Tove | 260 | 40 | +12% DEF |
| Kess | 200 | 25 | +12% ATK |
| Orin | 340 | 25 | +8% HP |
| Mira | 240 | 35 | +10% DEF |

**Weapons** (gacha equipment — this is where build identity lives):

| Weapon | Type | Rarity | ATK | Ultimate |
|---|---|---|---|---|
| Squire's Blade | Sword | R | 100 | Crossing Slash |
| Ash Lance | Lance | R | 105 | Piercing Ray |
| Cobble Maul | Hammer | R | 115 | Aftershock |
| Dawnbreaker | Sword | SR | 135 | Crossing Slash |
| Stormpike | Lance | SR | 140 | Piercing Ray |
| Sunforge Hammer | Hammer | SSR | 175 | Aftershock |

**Armor:**

| Armor | Rarity | HP | DEF | Move duration | Ultimate |
|---|---|---|---|---|---|
| Traveler's Garb | R | 600 | 40 | 3.0s (fast/fragile) | Swift Wind |
| Squire's Mail | R | 900 | 70 | 2.2s | Bulwark |
| Ironhide Plate | R | 1300 | 110 | 1.4s (slow/tanky) | Bulwark |
| Lifebloom Vest | SR | 1000 | 80 | 2.2s | Vital Bloom |

**Ultimates** (each has 3 tiers, unlocked/upgraded at item level 10/20/30):

| Ultimate | Type | Effect |
|---|---|---|
| Crossing Slash | Sword | Cross-shaped slash from final position; tier 3 adds a second rotated cross |
| Piercing Ray | Lance | Beam along the last travel vector, full arena length; tier 3 adds a perpendicular secondary beam |
| Aftershock | Hammer | Second, larger detonation at final position; tier 3 adds a brief stun |
| Bulwark | Armor | Absorb shield for 2 rounds (3 rounds at tier 3) |
| Swift Wind | Armor | +30–60% move duration next turn |
| Vital Bloom | Armor | Heal 20–45% max HP |

**Enemies:**

| Enemy | HP | DEF | ATK | Weapon behavior |
|---|---|---|---|---|
| Gloom Imp | 400 | 30 | 55 | Sword-type |
| Pike Husk | 350 | 25 | 60 | Lance-type, pierce 3 |
| Boulder Brute | 700 | 60 | 70 | Hammer-type, AoE 1.6 |
| **Warden of the Fifth Floor (boss)** | 2600 | 90 | 90 | Hammer-type, AoE 2.0 |

**Floors:** 1–4 are standard mixes of the three regular enemies (escalating count/variety), floor 5 is the Warden boss fight alone. Chapter 1 is defined as 100 floors in the GDD; only floors 1–5 have content authored right now (`floors.json` stops at 5).

---

## 6. Economy & progression

- **Currencies:** Sling Cores (from floor clears/boss first-clears → spent on gacha), Evo Ore (from floor clears + gacha duplicates → spent on item leveling), Evolution Cores (boss clears only → spent on evolution gates at 10/20/30), Sparks (every floor → spent on shop consumables).
- **Gacha:** SSR 3%, SR 12%, R 85%. Every 10-pull guarantees SR or better. Soft pity ramps from pull 60, hard pity guarantees SSR at pull 80. Single pull costs 100 Sling Cores, a 10-pull costs 900 (a built-in discount vs. 10× singles).
- **Leveling:** every weapon/armor levels 1→30; each level scales the primary stat by roughly ×(1 + 0.06×(level−1)), tripling by level 30. Evolution gates at 10/20/30 additionally require an Evolution Core each and unlock/upgrade the item's ultimate.
- **Floor progression:** boss every 5 floors, shop every 20, checkpoint (defeat-resume point) every 10. Enemy stats scale as `base × (1 + 0.11 × floor)`, with an intentional difficulty dip right after each boss.

---

## 7. Technical architecture

- **Engine:** Godot 4.7.1-stable-mono, C#, portrait mobile target.
- **Layering:** `Slingtt.Sim` (pure deterministic simulation, zero Godot references — enforced by `PurityTests`) → `Slingtt.Game` (content loading, battle setup, controller, rewards, run state) → Godot `Scripts/Render` + `Scripts/UI` (the only layer allowed to touch both engine and sim types).
- **Content:** `src/Slingtt.Game/content/*.json` (heroes, enemies, weapons, armor, ultimates, floors, balance/gacha/economy numbers, and `en.json` for display strings) are compiled in as `EmbeddedResource`, not loaded from `res://` — this was a deliberate choice to eliminate a whole class of "works on Windows, fatal on Android" path-casing bugs.
- **No binary game assets:** every mesh is generated procedurally in code (`MeshFactory`) and every sound is synthesized at runtime (`Sfx`), matching the GDD's "toy-like heft" art direction without shipping any art files.
- **This is a direct port**, not a redesign: `Slingtt.Sim` was ported line-for-line from the original web game's TypeScript `src/sim`, with `SimMath.RoundJs` specifically handling the JS-vs-.NET rounding difference (`Math.round` rounds half-away-from-zero; .NET's default is banker's rounding) so damage/reward numbers match the original exactly.
- **Tests:** 30 xunit tests in `tests/Slingtt.Sim.Tests` covering content loading, every floor building a valid battle, a full floor-1 battle played to completion, aim-prediction not mutating the live world, reward grants being idempotent across a simulated crash, and corrupt-save fallback. All passing as of this document.
- **Dev/debug tooling:** the `DevCapture` autoload adds CLI flags (`--autobattle`, `--autoplay`, `--shot <path>`, `--shot-delay <sec>`) for scripted, screenshot-verifiable testing without manual input — see `README.md`.

---

## 8. Relationship to the other Slingtt Evolution project

There is a second, unrelated Godot project at `D:\slingtt-evolution-mobile` that was built from scratch (from a written spec document, not ported from the web game) across earlier sessions. It diverged significantly — different hero count/roster, enemies redesigned as color-coded slimes, weapon-specific travel behavior later replaced with a universal "billiard bounce everything" model, and explicitly placeholder/never-finalized balance throughout, with no automated test coverage. As of 2026-08-06 that project is set aside; **this project is primary.**
