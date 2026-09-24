# Diplomski Rad — 2D platformer prototype (Unity)

Prototype developed for the master's thesis *Video game development using a game engine*
(Univerzitet Donja Gorica, Fakultet za informacione sisteme i tehnologije).

- **Engine:** Unity **6000.0.39f1** (Unity 6). Other versions are untested.
- **Unity project folder:** [`Diplomski_Rad/`](Diplomski_Rad/). Open this folder in Unity Hub, not the repository root.
- **Windows build:** see the repository's *Releases* page.

The whole scene is built by editor scripts. No part of it is hand-edited, so it can be rebuilt from scratch at any time.

## Rebuilding the scene

In the editor menu bar:

1. **Tools → Build Platformer Scene** creates `Assets/Scenes/PlatformerScene.unity`: the camera, tilemap, hand-built level section, player, enemies, pickups, spikes, parallax background, wind zone and UI.
2. **Tools → Generate Procedural Level (Seeded)…** fills the procedural zones on both sides of the hand-built section. The same seed always produces the same level. The plain **Tools → Generate Procedural Level** command picks a random seed and prints it to the Console (`[ProcGen] Seed = N`), so any generated level can be recreated later.

The committed scene and the release build use seed **2026**.

## Controls

| Action | Keyboard / mouse | Gamepad |
|---|---|---|
| Move | A / D or ← / → | Left stick |
| Jump (hold for full height) | Space or ↑ | South button |
| Elemental dash (towards the cursor) | Left mouse button | Right trigger |
| Respawn (debug) | R | – |

## Tests

The EditMode tests in [`Assets/Editor/Tests/`](Diplomski_Rad/Assets/Editor/Tests/) run the real `PlayerController` and Unity's 2D physics headlessly (physics stepped by script):

| Test | What it checks |
|---|---|
| `LevelGeneratorTests` | Runs the generator for seeds 1–500 and reads the tilemap back. Checks that the floor is continuous, that every obstacle on the floor can be climbed, that every platform can be reached from its neighbour in both directions with the basic jump, that no object spawns where the player cannot fit, and that the same seed gives the same level. |
| `PlayerMovementTests` | Measures the jump in the engine (apex height, air time, horizontal reach). Checks that a spike registers every visible touch when walking, jumping, and dashing with each element. |

Open **Window → General → Test Runner → EditMode → Run All**, or run the tests without the editor UI:

```
"C:\Program Files\Unity\Hub\Editor\6000.0.39f1\Editor\Unity.exe" -batchmode -nographics ^
  -projectPath Diplomski_Rad -runTests -testPlatform EditMode ^
  -testResults results.xml -logFile unity.log
```

The measured numbers are printed to the log on lines starting with `[REPORT]`. Save any open scene before running the tests from the editor. The tests swap in a temporary scene and restore yours afterwards.

## Command-line scene rebuild and build

```
Unity.exe -batchmode -quit -projectPath Diplomski_Rad -executeMethod CommandLineTools.RebuildScene -seed 2026
Unity.exe -batchmode -quit -projectPath Diplomski_Rad -executeMethod CommandLineTools.BuildWindows
```

The build is written to `Diplomski_Rad/Builds/Windows/Diplomski_Rad.exe` (also available as **Tools → Build Windows Player**).

## Key parameters

Values as set in the code. One Unity unit equals one tile: the tilemap cell size is 1 × 1, and every sprite is 16 px at 16 pixels-per-unit.

| Parameter | Value | Where |
|---|---|---|
| Physics gravity | −9.81 (project default, not overridden) | `ProjectSettings/Physics2DSettings.asset` |
| Physics step | 0.02 s (50 Hz) | `ProjectSettings/TimeManager.asset` |
| Player `gravityScale` | 4.5 | `BuildGameScene.CreatePlayer` |
| Fall / apex gravity multiplier | ×2.5 while falling / ×0.7 when 0.01 < v<sub>y</sub> < 1.2 | `PlayerController` |
| Player `Rigidbody2D.mass` | 1 (default). Has no effect on the jump. | – |
| Jump | `linearVelocity.y = jumpForce` (a direct write, not `AddForce`), `jumpForce` = 17 | `PlayerController.Jump` |
| Horizontal move speed | 13 units/s. Acceleration 150 on ground, 85 in air. | `PlayerController` |
| Player collider | 1 × 1 box + 0.05 edge radius (1.1 × 1.1 total) | `BuildGameScene.CreatePlayer` |
| Platform heights (cell y) | −3, −2, −1 (surfaces at world y −2, −1, 0). The floor surface is at −3. | `ProceduralLevelGenerator.PLATFORM_HEIGHTS` |
| Platform width | 3–6 tiles (`MIN_PLAT_W`, `MAX_PLAT_W`) | `ProceduralLevelGenerator` |
| Gap between platforms | 2–4 tiles (`MIN_GAP`, `MAX_GAP`) | `ProceduralLevelGenerator` |
| Floor thickness | 20 tiles, continuous across both zones | `ProceduralLevelGenerator.FLOOR_THICKNESS` |
| Procedural zones | x = −75…−16 and x = 21…80 (60 columns each) | `ProceduralLevelGenerator` |
