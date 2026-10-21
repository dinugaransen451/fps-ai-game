# FPS AI Game

Joint project for:
- **SE3062 - Intelligent Systems** (AI, pathfinding, decision-making) - 20% of module grade
- **SE3032 - Graphics and Visualization** (visuals, modeling, physics) - 20% of module grade

One Unity project, one repo, graded separately by each module.

## Concept
> Replace this with your one-line game concept.

## Team

| Student | GV Role | IS Agent |
|---|---|---|
| [Name] | World Builder (level, lighting, textures, NavMesh bake) | [Agent name / behavior] |
| [Name] | Systems Engineer (player physics: doors, barricades, throwing) | [Agent name / behavior] |
| [Name] | Core Developer (custom 3D models) | [Agent name / behavior] |
| [Name] | Agent Controller (path arrays -> movement/animation) | [Agent name / behavior] |

## Requirements
- Unity 2022 LTS
- Git LFS installed (`git lfs install`) before cloning
- Visual Studio or VS Code with Unity extension

## Getting started
1. Install Git LFS: https://git-lfs.github.com
2. Run `git lfs install` (once per machine)
3. Clone this repo
4. Open the project folder in Unity Hub (Add project from disk)
5. Open `Assets/_Project/Scenes/MainLevel.unity`

## Branching convention
```
main    -> stable, working build only. Protected, no direct commits.
dev     -> integration branch. All feature branches merge here first.
feature/world-builder
feature/systems-engineer
feature/core-developer
feature/agent-controller
feature/ai-agent-1
feature/ai-agent-2
feature/ai-agent-3
feature/ai-agent-4
```
Work on your own `feature/...` branch, push, open a Pull Request into `dev`.
Once `dev` is stable, it gets merged into `main`.

## Commit message format
```
[role/agent] short description

Examples:
[world-builder] Add corridor blockout and initial lighting
[ai-agent-2] Implement A* pathfinding with cover-cost heuristic
[systems-engineer] Add door open/close interaction script
[core-developer] Import and rig enemy character model
```

## Folder structure
```
Assets/_Project/
  Scenes/            - all .unity scene files
  Scripts/
    Player/          - player controller, input
    AI/
      Agent1_Name/   - each agent's AI logic in its own folder
      Agent2_Name/
      Agent3_Name/
      Agent4_Name/
    Physics/         - doors, barricades, throwables (Systems Engineer)
    Core/            - shared/game manager scripts
  Models/
    Characters/
    Props/
  Materials/
  Textures/
  Animations/
  Prefabs/
  Audio/
```

## Milestones
- Week 11: Concept & Scope Pitch
- Week 13: Logic & Asset Prototype
- Week 15: Final Showcase & Viva
- IS deadline: check with lecturer (spec excerpt was incomplete)
- GV deadline: 21 October 2026
