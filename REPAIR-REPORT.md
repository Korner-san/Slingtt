# Sling TT Evolution — Mobile conversion repair report

Date: 2026-08-03
Godot: 4.7.1.stable.**mono** (`D:\Godot_v4.7.1-stable_mono_win64`)
.NET SDK: 8.0.423 · JDK: Temurin 17.0.20 · Android SDK: `C:\Users\Acer\AppData\Local\Android\Sdk`

---

## 1. What the previous work actually was

**Native Godot C#. Not a web wrapper, not a hybrid — and not the game.**

The inherited project (`D:\slingtt-evolution-mobile`) was an **M0 engine spike**, and
its own documentation says so. `src/Slingtt.Sim/World.cs` carried the comment:

> *"M0-spike world: one ball bouncing inside walled bounds. This stands in for the
> full Actor[]/turn-order World … which is M1 scope."*

The entire Godot project consisted of:

| | |
| --- | --- |
| Scenes | **1** — `scenes/battle/BattleRoot.tscn` |
| Scripts | **1** — `Scripts/Render/SimDriver.cs` |
| Assets | `assets/{audio,fonts,models,shaders,textures}` — **all five empty** |
| Simulation | one ball, wall bounces, friction (`BattleSim.cs`, 3 KB) |
| Content | none — no heroes, enemies, weapons, floors or rewards |
| UI | none — no menu, no HUD, no results |

`BattleRoot.tscn` was a `Node3D` holding a dark-grey box floor, four grey walls, one
red sphere, an aim stick, a camera and a light. There was no `WorldEnvironment`, no
`CanvasLayer`, and no `Control` node anywhere in the project.

Meanwhile the **real** game — six heroes, four enemies, three weapon families with
distinct contact behaviour, six ultimates, five floors, a currency/reward economy —
existed only in the TypeScript original at `C:\Users\Acer\Desktop\SlingttEvolution`.
None of it had been ported.

## 2. Precise cause of the grey screen

There were **three independent defects**. Any one of them alone produces the
reported symptom.

### 2.1 There was no game to draw (certain)

The main scene was a featureless grey box: floor `#1B1826`-ish at RGB(0.16, 0.19,
0.22), walls at RGB(0.35, 0.40, 0.48), against Godot's **default clear colour, which
is grey** — the project set no `default_clear_color` and the scene contained no
`WorldEnvironment`. A dark grey arena on a grey background, with no UI of any kind,
*is* a grey screen. Even in the best case the APK could never have shown "the game",
because the game was not in it.

### 2.2 Portrait orientation was silently ignored (verified)

`project.godot` contained:

```ini
window/handheld/orientation="portrait"
```

That is **Godot 3 syntax**. Godot 4 stores this setting as the `ScreenOrientation`
enum *index*, so the string failed to parse and fell back to `0` = **Landscape**.

Verified on device: the app opened at **2400×1080 landscape** while every camera fit
and layout is authored for 1080×1920 portrait. Fixed to `=1`; the rebuilt APK's
manifest now reports `android:screenOrientation=1` (confirmed with `aapt2 dump
xmltree`).

### 2.3 Vulkan presentation failed on device (verified, and the decisive one)

This is the finding that matters most, because it means *"the export succeeded" and
"the C# ran" were both true while the screen stayed blank.*

Running the rebuilt game on Android with the Vulkan `mobile` renderer, logcat showed
the engine and the managed game initialising **completely correctly**:

```
I godot: Godot Engine v4.7.1.stable.mono.official
I godot: Vulkan 1.3.0 - Forward Mobile - Using Device #0 ...
I godot: [Slingtt] content loaded: 6 heroes, 4 enemies, 5 floors, arena 9x16
V Godot: OnGodotMainLoopStarted
```

No script errors, no missing resources, no failed scene load, content deserialised
from the embedded catalog. And then, repeating every frame:

```
E godot: ERROR: Couldn't present to Vulkan queue (VkResult error 5).
E godot:    at: command_queue_execute_and_present (rendering_device_driver_vulkan.cpp:3359)
```

`vkQueuePresentKHR` failing means every rendered frame was discarded. The game was
running perfectly and simply never reached the display.

**Fix:** the project now ships on `gl_compatibility` (OpenGL ES 3.0) instead of the
Vulkan `mobile` renderer. This game is low-poly with two directional lights and no
post-processing, so GLES3 costs nothing visually, and it is the widest-support path
on Android — including phones with unreliable Vulkan drivers. Reverting is a
two-line change in `project.godot`, documented in `README.md`.

### 2.4 Ruled out

Checked and **not** contributing: `project.godot` validity, main-scene existence and
packaging, script parse errors, node paths, absolute paths, missing autoloads,
`res://` casing, Android permissions, signing, or missing files in the APK. The
original APK genuinely did contain its scene and its .NET assemblies — the packaging
was fine. **A successful export proved nothing, exactly as suspected.**

## 3. Documents that mattered

| Source | What it gave |
| --- | --- |
| `Slingtt-Mobile-Studio-Pack/02-technical/20-architecture.md` | the sim/game/render layering the port preserves |
| `…/21-determinism-spec.md` | double-precision sim, no engine types below render, the "one legitimate `pow`" rule |
| `…/22-godot-project-structure.md`, `28-build-and-release.md` | Gradle build required for C# Android export at all |
| `godot/export_presets.cfg` header comment | why Gradle, and that both presets had exported end-to-end |
| `src/Slingtt.Sim/*.cs` comments | explicit "M0-spike … M1 scope" markers that dated the work |
| **`Desktop\SlingttEvolution\src\**`** | the actual game — the single most important source |
| `…\src\docs\GDD.md`, `adr/0002`, `adr/0004` | pure-sim and content-as-data decisions |

## 4. What was repaired vs. rebuilt

**Preserved** — `Vec2`, `Rng` (mulberry32), the `SimConfig` shape, the fixed-timestep
accumulator design, the layer boundaries, the Gradle export setup, and the Android
build template.

**Rebuilt from the TypeScript original** — everything else, because the spike had no
equivalent to rebuild *from*:

| Layer | Ported from | Now |
| --- | --- | --- |
| `Slingtt.Sim` | `src/sim/*.ts` | `Actor`/`World`/events, swept-TOI `Collision`, `Damage`, sword/lance/hammer, `Ultimates`, `TurnOrder`, `EnemyAi`, `BattleSim`, `Predict` |
| `Slingtt.Game` | `src/game`, `src/data`, `src/shared` | `Content` (embedded JSON), `Formulas`, `BattleSetup`, full `BattleController` (interpolation, hit-stop, shake, aim+prediction), `Coords`, `VisualRoster`, `Rewards`, `RunState` with save/load |
| Godot render | `src/render`, `src/art` | `BattleScene`, `ActorView`, `ArenaView`, `AimView`, `VfxView`, `CameraFit` (same 56°/42°/0.9 constants), `MeshFactory`, `Palette` |
| Godot UI | `src/ui` | `MainMenu`, `BattleHud`, results overlay, `UiKit` |
| Audio | `src/audio` | `Sfx` — procedurally synthesised, matching the original's oscillator approach |

### Porting hazards handled

- **`Math.round` vs `Math.Round`.** JavaScript rounds half away from zero; .NET uses
  banker's rounding. Porting directly would have shifted every damage roll and
  reward. All ported arithmetic goes through `SimMath.RoundJs`.
- **Content packaging.** The catalog is an `EmbeddedResource` inside
  `Slingtt.Game.dll` rather than `res://` data. A mis-cased or unpackaged data path
  is invisible on Windows and fatal on Android — the exact class of bug behind the
  original failure. Likewise every mesh is generated in code and every sound
  synthesised, so the APK ships **zero binary game assets**.
- **Sim purity.** `Slingtt.Sim` still has no Godot reference; `PurityTests` enforces it.

## 5. Verification performed

`dotnet test` — **30/30 passing**, including content loading, every floor building a
valid battle, a full floor-1 battle played to completion through `BattleController`,
prediction not mutating the live world, reward grants being idempotent across a
simulated crash, and a corrupt save falling back to a fresh run instead of bricking.

Desktop (Godot 4.7.1-mono, 540×960 portrait) — main menu, battle scene, live combat
with damage numbers and impact VFX, and the floor-cleared results screen were each
captured and inspected. Reward figures were checked against `balance.json` by hand:
floor 1 yields +21 Sling Cores / +15 Evo Ore / +29 Sparks, and `900 − 21 = 879` cores
to a 10-pull — all correct.

Android — see §2.3. Confirmed on device via logcat: engine init, Vulkan device
creation, **.NET runtime start, content deserialised from the embedded catalog
(`6 heroes, 4 enemies, 5 floors`), and `OnGodotMainLoopStarted`** — with zero script
errors and zero missing resources. The install, the portrait manifest flag
(`aapt2 dump xmltree` → `screenOrientation=1`), and app restart were also verified.

### Not verified — state this plainly

**That `gl_compatibility` presents a frame on Android has NOT been confirmed on
hardware.** After the first successful boot of the session, the Android emulator on
this machine hung during startup on every subsequent launch — five attempts across
three AVDs and three GPU backends, each stalling at
`SharedLibrary::open for [vulkan-1.dll]` with the guest consuming ~1 CPU-second.
That is a host-side emulator fault with no relationship to this APK, but it means
the last link in the chain is reasoned, not demonstrated:

- *Demonstrated:* the game runs correctly on Android up to and including the main
  loop, and the Vulkan **swapchain present** call is the single thing that failed.
- *Reasoned:* `gl_compatibility` presents through EGL/`GLSurfaceView`, an entirely
  different path that never touches the Vulkan swapchain, and it renders correctly
  under desktop OpenGL.

Because that last step is inference, **both renderer builds ship** (§7) so the
failure mode can be resolved in one install rather than one rebuild.

## 6. Feature status

**Working:** touch drag-and-release slinging with honest trajectory preview; swept
continuous collision; sword bounce-decay, lance pierce, hammer stop-and-detonate;
weapon ultimates (cross/beam/aftershock) and armour ultimates (bulwark/swift/vital);
round-robin turn order with stun skipping; enemy AI; HP/shield/heal; hit-stop, screen
shake and camera punch; five floors with a boss; floor-clear rewards, healing, HP
carry, milestones; persistent save; menu, HUD, turn strip, results, defeat/retry;
portrait-locked responsive layout with safe-area insets; procedural audio.

**Not implemented (out of scope of the repair, absent from the web original too):**
gacha pulls, the shop, item upgrade/evolution UI, hero roster management, Firebase
sync/auth. `balance.json` carries gacha and shop numbers, but the web original had no
UI for them either. Floors stop at 5 because `floors.json` defines 5 — content, not
code.

## 7. Build facts

| | |
| --- | --- |
| Godot | 4.7.1.stable.mono |
| Renderer | `gl_compatibility` (OpenGL ES 3.0) |
| Package | `com.slingtt.evolutionmobile` |
| Version | 1.0.0 (code 1) |
| Orientation | portrait (`screenOrientation=1`) |
| ABIs | `arm64-v8a` + `x86_64` |
| Signing | release keystore |
| Internet permission | **off** — the game is fully offline |
| Project | `D:\SlingTTEvolution_Mobile\SlingTTEvolution_Godot_Repaired` |
| APK (primary) | `D:\SlingTTEvolution_Mobile\SlingTTEvolution.apk` — `gl_compatibility` |
| APK (fallback) | `D:\SlingTTEvolution_Mobile\SlingTTEvolution-Vulkan.apk` — `mobile`/Vulkan |

### Two builds, on purpose

Both APKs are the same game, same package name, same portrait fix — they differ
only in renderer, so installing one replaces the other. Because the GLES3
presentation path could not be confirmed on hardware here (§5), shipping both turns
an unverified assumption into a 30-second A/B on the real device:

1. Install `SlingTTEvolution.apk`. If the menu appears, you are done.
2. If it is black, install `SlingTTEvolution-Vulkan.apk` instead.

If **both** are black, the fault is neither renderer, and the next step is
`adb logcat -s godot:V` while launching — that log identified the original failure
in one run and will identify this one too.

The APK deliberately carries **both** `arm64-v8a` (real phones) and `x86_64`
(emulator) so that the artifact verified on the emulator is byte-for-byte the
artifact installed on the phone, rather than a near-miss build. That costs ~40 MB;
dropping `x86_64` in `export_presets.cfg` shrinks it if size matters.
