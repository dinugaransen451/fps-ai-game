# Member 2 - Systems Engineer + The Tank

**GV duty (player interaction physics):** `Scripts/Physics/`
- `Door.cs` - hinged door, E to open/close, carves the NavMesh when closed, can be bashed/broken
- `Barricade.cs` - heavy pushable rigidbody (player pushes it by walking into it), carves the NavMesh, has HP
- `Throwable.cs` - bottle (shatters, loud distraction noise) and grenade (fuse, area damage, breaks barricades/doors)
- `Pickup.cs` - ammo / health / grenade / bottle pickups
- `Core/Interfaces.cs` - `IBlocker` (doors + barricades share it) and `NoiseSystem`

**IS agent - The Tank:** `TankBrain.cs` + `AI/Common/BehaviorTree.cs`
```
Root (selector)
 1 Attack      : InMeleeRange -> Strike
 2 Chase       : ShouldChase -> [ PathBlocked -> Smash blocker | MoveToPlayer ]
 3 Investigate : HasNoise -> GoLook
 4 Patrol      : loop through corridor waypoints
```
- Slow (patrol 1.0, chase 2.7 m/s), 320 HP, hits for 30
- Cannot use narrow gaps: its NavMeshAgent area mask excludes area 3 -> takes the wider, predictable routes
- When no path exists, it picks the breakable blocker that adds the smallest detour, walks up and smashes it
