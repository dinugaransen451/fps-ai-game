# Member 4 - Agent Controller + The Runner

## Who this member is
Member 4 owns movement/animation glue for agents (path-following motor, animation driver, block-body fallback) and the Runner zombie agent with its own A* pathfinding.

## Included files (with what each does)
- Assets/_Project/Scripts/AgentController/AgentMotor.cs - CharacterController-based path follower: pure-pursuit look-ahead on polyline, acceleration/braking, turn-rate limited rotation, slowdown for sharp turns/arrival, gravity/slopes, stuck detection (IsBlocked), calls ZombieAnimDriver.Set with speed/blend/attack.
- Assets/_Project/Scripts/AgentController/ZombieAnimDriver.cs - Speed->walk/run blend and attack pose for Blender zombie or fallback, integrates with animation.
- Assets/_Project/Scripts/AgentController/AgentLocomotionAnimator.cs - Locomotion helper for NavMeshAgent-driven zombies (Tank) reading agent.velocity to drive animation.
- Assets/_Project/Scripts/AgentController/BlockRig.cs - Block-body fallback visual rig when mesh/animator not present.
- Assets/_Project/Scripts/AI/Agent4_Runner/RunnerBrain.cs - Runner agent: uses NavGrid A*, sprints straight when line clear else follows path, deals with closed doors (bashes/open) and barricades (marks impassable for 12s and repaths), hears noise/alerts, speed increases as player health drops (4.6 -> 7.4 m/s).
- Assets/_Project/Scripts/AI/Agent4_Runner/NavGrid.cs - Own hand-written A* over 0.5m grid sampled from NavMesh (floors/ramps). NavMesh.Raycast for links. A* cost = distance + blockCost (re-routing); RefreshBlockCosts applies IBlocker.PathPenalty. String-pulling smoothing. Can find path to nearest node, partial paths to closest reachable.
- Assets/_Project/Scripts/AI/Agent4_Runner/README.md - Member 4 description and Runner/NavGrid overview.

## Main classes
AgentMotor, ZombieAnimDriver, AgentLocomotionAnimator, BlockRig, RunnerBrain, NavGrid.

## Key algorithms
- Path-following motor (AgentMotor): polyline following with look-ahead, segment advancement, turn-limited rotation with turn-factor slowdown, arrival/braking, gravity, stuck detection (blocked if expected distance >> moved).
- Own A* (NavGrid): grid build by sampling NavMesh + ramps, adjacency via NavMesh.Raycast (wall-respecting), binary heap A*, heuristic straight line (admissible), edge cost + blockCost[neighbor], path smoothing by straight-line checks.
- Door/barricade cost & re-routing: every node inside closed door/barricade gets extra cost (IBlocker.PathPenalty). If detour cheaper A* goes around; if only way, path may go through and Runner bashes door or marks barricade impassable and repaths.
- Re-pathing when blocked: on blocker collision/impasse, Runner invalidates/repairs state, marks blocked nodes via pathing cost or temporary forbidden area and recalculates path.
- Health-based speed-up: Runner maxSpeed scales inversely with player health (low health -> faster chase).
