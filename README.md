# Zombie Outbreak - Building Clearance (FPS + AI)

Option 4 of the brief: clear a two-floor infested building. The zombies are not mindless - each type behaves differently
and reacts to what you do.

## How to run
1. Open the project with the Unity version in `ProjectSettings/ProjectVersion.txt` (Unity 6, URP).
2. **First open only:** a setup script runs by itself (Console shows `[ZombieGame] ...`). It configures the zombie model, builds the
   Screamer/locker/zombie prefabs, generates the whole level into `Assets/_Project/Scenes/MainLevel.unity`, bakes the NavMesh,
   saves the generated textures/materials as assets and adds the scene to Build Settings. Give it a minute.
   (Re-run any time: **Tools > Zombie Game > Setup Everything**.)
3. Open `Assets/_Project/Scenes/MainLevel.unity` and press **Play**.

Safety net: if the editor setup ever fails, `MainLevel.unity` still works - the level is then generated and baked when you press Play.
(`Tools > Zombie Game > Reset Level To Runtime-Generated` puts the scene back to that state.)

## Controls
WASD move - Shift sprint (stamina) - Space jump - Mouse look - LMB shoot - R reload - **E** open/close doors - **F** flashlight -
**Q** throw bottle (distraction) - **G** throw grenade - walk into a barricade to push it - Esc pause - R restart (when paused/dead) - **F1 AI debug view**

## Objective
Kill 20 infected (the Director spawns up to 26). Pick up ammo, health, grenades and bottles. Gunshots, running, doors and breaking glass are heard by zombies.

## The team's parts

| Member | Game-visuals duty | Intelligent agent |
|---|---|---|
| 1 World Builder | `Scripts/World/` - level, textures, flickering lights, markers, NavMesh | **Director** (`AI/Agent1_Director`) - decides when/where/which zombie to spawn |
| 2 Systems Engineer | `Scripts/Physics/` - doors, pushable barricades, throwables, pickups | **Tank** (`AI/Agent2_Tank`) - behavior tree; breaks barricades; avoids narrow gaps |
| 3 Core Developer | Blender zombie + wrecked locker, imported & set up | **Screamer** (`AI/Agent3_Screamer`) - utility AI; flee / hide / scream-alert |
| 4 Agent Controller | `Scripts/AgentController/` - path array -> smooth movement + animation | **Runner** (`AI/Agent4_Runner`) - own A* with re-routing cost; faster as player health drops |

Each agent folder has a README with the details. Shared systems: `Scripts/Core/` (game manager, HUD, noise system, health, audio, factory),
`Scripts/Player/` (first-person controller, weapon, interaction).

## How the pieces connect (good for the viva)
- Barricades/doors implement `IBlocker`: the **Tank** breaks them, the **Runner** pays an A* cost to cross them or routes around, the NavMesh is carved for the NavMeshAgents.
- `NoiseSystem`: footsteps, gunshots, doors, bottles, explosions -> Tank investigates, Runner searches.
- `AlertSystem` (Screamer's scream) -> Tank chases the reported position, Runner searches it.
- The **Director** reads `PlayerStats` (health, ammo) and spawns through `ZombieFactory`; the **Runner** reads the same health to speed up.
- Everything is textured/modelled procedurally except the Screamer's Blender model, which the Tank and Runner reuse (tinted/scaled).

## Notes
- `Extras/WorldBuilder_Original/` holds the files from the original world-builder zip (not used - see NOTE.txt there).
- Everything in the game (textures, sounds, level) is generated from code; the only imported art is the zombie model/locker/scream from Member 3.
