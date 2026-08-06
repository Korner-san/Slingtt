# Sling TT Evolution — Mobile (Godot 4.7.1 / C#)

Android conversion of the *Sling TT Evolution* web game
(`C:\Users\Acer\Desktop\SlingttEvolution`, TypeScript + React + three.js).

The simulation, content catalog and progression rules are a direct port of the web
original's `src/sim`, `src/game` and `src/data`, so the mobile build plays by the
same numbers. Nothing about the battle rules was reinvented for mobile.

## Layout

```
SlingTTEvolution_Godot_Repaired/
├─ godot/                     Godot project (open THIS folder in Godot)
│  ├─ project.godot
│  ├─ scenes/                 MainMenu.tscn, Battle.tscn — thin roots, one script each
│  ├─ Scripts/
│  │  ├─ Autoload/            GameRoot (content, run state, audio), DevCapture
│  │  ├─ Render/              BattleScene, ActorView, ArenaView, AimView, VfxView,
│  │  │                       CameraFit, MeshFactory, Palette
│  │  ├─ UI/                  MainMenu, BattleHud, UiKit
│  │  └─ Audio/               Sfx (procedurally synthesised, no audio files)
│  └─ android/build/          installed Gradle build template
├─ src/
│  ├─ Slingtt.Sim/            pure deterministic battle simulation — NO engine types
│  ├─ Slingtt.Game/           content, battle setup, controller, rewards, run state
│  │  └─ content/*.json       catalog, embedded into the assembly (see below)
│  └─ Slingtt.Shared/
└─ tests/Slingtt.Sim.Tests/   30 xunit tests over the sim and game layers
```

### Architecture rule

`Slingtt.Sim` and `Slingtt.Game` never reference Godot. `BattleScene` is the only
place engine types and simulation types meet, and it is where sim `Vec2` (double)
becomes engine `Vector3` (float). `PurityTests` fails the build if a Godot
reference ever creeps into the sim assembly.

### Content is embedded, not `res://`

`src/Slingtt.Game/content/*.json` are `EmbeddedResource` entries compiled into
`Slingtt.Game.dll`. A missing or mis-cased `res://` data path is invisible on
Windows and fatal on Android; embedding removes that failure mode entirely. The
same applies to art and audio — every mesh is generated procedurally in
`MeshFactory` and every sound is synthesised in `Sfx`, so the APK contains no
binary game assets that could fail to package.

## Opening the project

1. Launch `D:\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64.exe`
   (the **.NET/Mono** build — the standard build cannot run C#).
2. *Import* → select `SlingTTEvolution_Godot_Repaired\godot\project.godot`.
3. Build the C# solution once (hammer icon, top right) before pressing Play.

From the command line:

```bash
dotnet build "D:/SlingTTEvolution_Mobile/SlingTTEvolution_Godot_Repaired/godot/SlingttEvolutionMobile.csproj"
```

## Running the tests

```bash
dotnet test "D:/SlingTTEvolution_Mobile/SlingTTEvolution_Godot_Repaired/tests/Slingtt.Sim.Tests/Slingtt.Sim.Tests.csproj"
```

## Rebuilding the APK

Requires JDK 17, the Android SDK, and the Godot Android build template (all
already configured on this machine).

```bash
dotnet build "D:/SlingTTEvolution_Mobile/SlingTTEvolution_Godot_Repaired/godot/SlingttEvolutionMobile.csproj" -c ExportRelease
```

```bash
"D:/Godot_v4.7.1-stable_mono_win64/Godot_v4.7.1-stable_mono_win64_console.exe" --headless --path "D:/SlingTTEvolution_Mobile/SlingTTEvolution_Godot_Repaired/godot" --export-release "Android-APK" "D:/SlingTTEvolution_Mobile/SlingTTEvolution.apk"
```

The Gradle build takes several minutes on a cold cache. `--export-release` is
correct here: the preset is signed with the release keystore.

## Headless verification flags

`DevCapture` adds command-line flags used by the build/verification loop. They are
inert unless passed after `--`:

| flag | effect |
| --- | --- |
| `--autobattle` | boot straight into the battle scene |
| `--autoplay` | fire the active hero at the nearest enemy each turn |
| `--shot <path>` | write a PNG of the framebuffer, then quit |
| `--shot-delay <sec>` | wait this long first (default 1.5) |

```bash
"D:/Godot_v4.7.1-stable_mono_win64/Godot_v4.7.1-stable_mono_win64_console.exe" --path "D:/SlingTTEvolution_Mobile/SlingTTEvolution_Godot_Repaired/godot" --resolution 540x960 -- --autobattle --autoplay --shot "D:/shot.png" --shot-delay 4
```

## Renderer, and the two APKs

The project ships on **`gl_compatibility`** (OpenGL ES 3.0), not the Vulkan `mobile`
renderer. The original black/grey screen was a Vulkan **swapchain presentation**
failure — the engine, .NET runtime and content all initialised correctly on device
and only `vkQueuePresentKHR` failed. This game is low-poly enough that GLES3 costs
nothing visually while supporting far more devices.

That GLES3 conclusion could not be confirmed on Android hardware here (the emulator
on this machine hangs on startup — see `REPAIR-REPORT.md` §5), so **both builds are
provided**:

| file | renderer |
| --- | --- |
| `..\SlingTTEvolution.apk` | `gl_compatibility` — install this first |
| `..\SlingTTEvolution-Vulkan.apk` | `mobile` / Vulkan — install this if the first is black |

Same package name, so installing one replaces the other. To rebuild either, set both
`renderer/rendering_method` and `renderer/rendering_method.mobile` in
`project.godot` to `"gl_compatibility"` or `"mobile"` and re-export.

If **both** show black, capture the engine log while launching — it pinpointed the
original failure in a single run:

```bash
"C:\Users\Acer\AppData\Local\Android\Sdk\platform-tools\adb.exe" logcat -s godot:V Godot:V
```

## Secrets

`godot/export_presets.cfg` contains the Android release keystore path and
password. It is gitignored; `export_presets.cfg.example` is the secret-free
reference. Rotate that keystore password if this folder is ever shared.
