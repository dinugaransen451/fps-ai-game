# Member 4 - Agent Controller + The Runner

**GV duty (movement + animation):** `Scripts/AgentController/`
- `AgentMotor.cs` - takes a path array and produces smooth movement: pure-pursuit look-ahead on the polyline, acceleration/braking,
  turn-rate-limited rotation, slowing for sharp turns and on arrival, gravity/slopes via CharacterController, stuck detection
- `ZombieAnimDriver.cs` - speed -> walk/run blend (+ attack pose) for the Blender zombie (or the block-body fallback)
- `AgentLocomotionAnimator.cs` - same for NavMeshAgent zombies (Tank)

**IS agent - The Runner:** `RunnerBrain.cs` + `NavGrid.cs`
- Own hand-written A* over a 0.5 m walkable grid sampled from the NavMesh (several floors + the stair ramp)
- Cost = distance + re-routing cost: closed door +30, barricade +80 (per blocked node). Detour cheaper -> goes around
- Blocked while walking: closed door -> shoulders it open; barricade -> marks it impassable for 12 s and recalculates (never stuck)
- Sprints straight at the player when the line is clear, otherwise follows the A* path
- Speed grows as the player's health drops: 4.6 m/s -> 7.4 m/s
- Hears gunshots / doors / bottles and Screamer alerts
