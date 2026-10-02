# Dynamic Worlds validation companion

This mod is a disposable-world test harness, excluded from the production `DynamicWorlds` source and package. It depends on the production `.tmod` under test through `modReferences`.

The server harness remains inert unless every guard passes:

- `DW_VALIDATION_RUN_ID` is nonempty.
- `DW_VALIDATION_ROOT` is an absolute, non-symlink disposable directory.
- `.dynamic-worlds-validation` inside that directory contains the exact run ID.
- `DW_VALIDATION_RESULT` resolves inside that directory.
- `DW_VALIDATION_REGEN_SEED` is an integer different from the initial world's seed.
- The active world is inside that directory's `Worlds` folder.
- The process is a dedicated server, and `DW_VALIDATION_MODE` is not `client`.

The separate graphical companion uses explicit client mode and the same isolation guard. Its fixture player is created in the disposable root; no normal player or world files are used.

The driver must launch tModLoader with an isolated save directory and only the production mod and this companion enabled. Never install this mod into a normal gameplay mod folder.

## Validation protocol

Commands arrive through the dedicated server console. They enqueue work; fixture mutation and assertions execute in `PostUpdateEverything` on the game update thread. On an empty dedicated server, where Terraria skips world updates, the main-loop `OnTickForThirdPartySoftwareOnly` event advances only the harness. It does not simulate production update hooks.

1. Wait for result phase `world_ready`.
2. Send `dwvalidate begin` and require `fixtures_ready`, `success: true`, the exact run ID, and all six fixture checks passing. This creates the disposable player and saves the fixture world with real `.wld`/`.twld` serialization.
3. Exit the server cleanly.
4. Launch the graphical test client into this world and player. The client companion exercises the actual production single-player regeneration path, including save, menu world generation, and reload, then checks preserved fixtures and rendered gameplay.
5. Require the graphical report's complete check set and success before accepting the candidate artifact. The driver owns process deadlines and failure cleanup.

`dwvalidate status` refreshes the server result without mutating fixtures. Optional `dwvalidate verify` tests the dedicated-server regeneration path after a real `regenworld` command, but is not part of the default gate: the current production queue stalls when no clients remain because Terraria stops world updates. The harness does not hide or repair that defect.

Each report is atomically replaced at `DW_VALIDATION_RESULT`, with a schema version, run ID, phase, check records, seed, world path, update thread ID, and any error.

The server tests exercise actual loaded production APIs and Terraria state: progression capture/apply; tile and chest tag round trips; and movable structure capture/restoration. Chest stacks are changed after their initial snapshots to exercise the production pre-regeneration refresh. The graphical run's changed world seed and erased unprotected control tile guard against accepting a run in which regeneration never happened. The independent 35-cell structure specification also checks translated geometry and updated chest contents.

This covers focused vanilla fixtures and the single-player regeneration flow. It does not certify all biomes, mod compatibility, or multiplayer reconnection. The dedicated-server idle-queue defect remains an explicit unsupported path until fixed and validated separately.
