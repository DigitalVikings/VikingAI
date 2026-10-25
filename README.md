# VikingAI

A foundational navigation, context-steering, sensory perception, tactical pack coordination, smart-object affordance, and unloaded-zone NPC simulation framework for Valheim.

---

## Core Architecture

1. **Unified 2.5D Clearance Navigation Graph (`NavGraphManager`)**
   - Normalizes vanilla `Pathfinding`'s $10\times$ tile-border link penalty (`10.0 -> 1.2`) to eliminate $32\,\text{m}$ tile-seam zig-zagging.
   - Replaces multi-agent-type NavMesh rebuild stalls with a 2.5D clearance-annotated span graph (`TrueClearanceRadius`, `CeilingClearance`, `StructuralHp`) and pluggable `INavTraversalRule` contracts (`GroundWalkRule`, `WaterSwimRule`, `DoorTraversalRule`, `LedgeJumpRule`, `SiegeBreachRule`).

2. **Player-Centric Tactical Flow Fields (`TacticalFlowField`)**
   - Maintains a $64 \times 64\,\text{m}$ ($2\,\text{m}$ cell) Dijkstra integration and gradient vector field around combat anchors at $5\,\text{Hz}$, stamping crowd density and fire hazards so large packs route around congestion.

3. **16-Ray Context Steering & Polar Follower Formations (`SteeringPipeline`)**
   - Evaluates 16-slot radial `Interest` and `Danger` maps across modular `ISteeringContributor` stages (`PathAndFlow`, `NeighborSeparation`, `ObstacleWhisker`, `HazardAvoidance`, `PlayerYield`).
   - Decouples `SetLookDir` from `SetMoveDir` on `Character` to unlock native $360^\circ$ humanoid combat strafing and backstepping.
   - Assigns tamed followers and companions into polar V-wedge formation slots behind the player and automatically steps aside when approached in corridors or doorways.
   - Automatically opens and re-closes unlocked doors (`DoorTraversalController`) for profiles with `CanOpenDoors` enabled.

4. **Layered Brain Pipeline, 4-Stage Sensory Engine & Pack Coordinator (`BrainPipeline`)**
   - **Sensory Perception Engine (`SensoryMemoryController`)**: Generalized `Character`-to-`Character` 4-stage detection ramp (`Unaware -> Suspicious -> Alerted -> Engaged`), directional sunlight/shadow raycasting via `RenderSettings.sun`, time-sliced foliage concealment, mathematical equipment-weight acoustic profiles, and a 32-slot `AllyCall` ring buffer.
   - **Tactical Pack Coordinator (`CombatPackCoordinator`)**: Enforces simultaneous melee Attack Token budgets per target and distributes waiting attackers onto a $45^\circ$ flanking ring with anti-perch fallback.

5. **Smart Object Settlement Affordances & ZDO NPC Equipment (`AffordanceRegistry`)**
   - Slot-reserved settlement work providers (`SmelterAffordanceProvider`, `FireplaceAffordanceProvider`, `RepairPieceAffordanceProvider`, `HarvestPickableAffordanceProvider`, `LeisureChairAffordanceProvider`) driven by `SmartAffordanceBrainModule`.
   - ZDO-backed NPC inventory persistence (`"VAI_v1_Inv"`) and full `VisEquipment` paperdoll synchronization (hair, beard, and holstered back items) on non-player `Humanoid`s.

6. **1 Hz Macro-ZDO Virtual Agent Director (`VirtualAgentDirector`)**
   - Simulates unloaded settlement NPCs and patrols at $1\,\text{Hz}$ in `ZDO` space, applies deterministic time-delta catch-up when zones reload, and transports active party companions across portals and dungeon transitions.

---

## Vanilla-Faithful Baseline & Configuration

Unopinionated engine improvements (tile-seam cost normalization, context steering, crowd separation, follower formations, and friendly yield-on-approach) are enabled by default. Opinionated changes to wild vanilla enemy tactics (`VanillaEnemiesUsePackTokens`, `VanillaHumanoidsStrafeInCombat`, `VanillaEnemiesJumpLedges`, `VanillaEnemiesBreachWeakWalls`, `VanillaEnemiesUseSensoryEngine`) default to **`false`** so vanilla enemies retain their familiar feel unless opted in via configuration or a downstream mod's `AgentBehaviorProfile`.

---

## Developer Console Commands

- `vikingai debug [grid|flow|steering|tokens|affordances|senses|all|off]` — Toggles real-time 3D GL and HUD telemetry overlays.
- `vikingai stats` — Prints live tracked piece and affordance counts from `PieceRegistry`.
