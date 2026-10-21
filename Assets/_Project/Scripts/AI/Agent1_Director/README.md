# Member 1 - World Builder + The Director

**GV duty (World):** `Scripts/World/LevelBuilder.cs`, `ProceduralTextures.cs`, `FlickerLight.cs`
- 2 floors (40 x 30 m): lobby, security office, storage, cafeteria, lab, stair hall + main corridor, joined by a ramp stairwell
- Procedural textures (concrete, plaster, tiles, ceiling tiles, wood, metal, hazard stripes, carpet, blood decals, bullet holes)
- Dim flickering horror lighting (buzzing / failing / pulsing red emergency lamps), fog, dark ambience
- 20 spawn markers (`SpawnPoints`), tank patrol routes, 2 "narrow service gaps" tagged with NavMesh area 3 ("Narrow")
- NavMesh baked from the static geometry (editor bake into the scene, or runtime bake as fallback)

**IS agent - The Director:** `Director.cs` (+ `PlayerStats.cs`)
- Reads player health + ammo -> `Intensity` (0 struggling .. 1 doing well); also tracks how long the player stays in one area
- WHEN: spawn interval lerps between 14 s (struggling) and 5 s (doing well); alive-zombie cap scales with intensity
- WHERE: markers that are far enough, hidden (line-of-sight blocked), not crowded, not the previous marker; nearest hidden marker when the player camps
- WHICH: weighted pick - Runner always; Tank only when player is doing well AND has ammo; Screamer only with >=2 zombies alive (x3 when camping)
- Every decision is logged on screen (F1 debug view)
