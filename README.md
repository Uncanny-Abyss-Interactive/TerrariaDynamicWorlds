# Dynamic Worlds

Dynamic Worlds is a tModLoader utility mod for rebuilding a Terraria world's terrain without throwing away the playthrough built on top of it.

The mod snapshots your world state, saves the important things you marked, runs fresh world generation through a menu loading screen, restores your preserved data, and automatically loads you back into the regenerated world.

Single-player is still the most polished path, but the mod now also supports multiplayer world-tool syncing and server-admin `/regenworld` with a countdown, disconnect, and reconnect flow.

If you are running a server, start with the new [server admin guide](./SERVER_ADMIN_GUIDE.md).

For development builds and automatic Git-to-tModLoader deployment, see the
[deployment guide](./DEPLOYMENT.md).

## What Dynamic Worlds Currently Does

- Runs `/regenworld [seed]` through a real loading screen instead of freezing the live game.
- Preserves major world progression such as Hardmode, ore tiers, boss flags, event flags, invasions, game mode, world identity, and saved NPC state.
- Preserves exact tiles with `Reality Anchor`.
- Forces empty space with `Reality Eraser`.
- Preserves full builds with `Structure Anchor`.
- **NEW:** Automatically relocates preserved vanilla pylons to matching biomes with `Biome Dowser`.
- Restores chest and dresser contents inside anchored tiles and structure zones.
- Reassigns town NPC housing when their homes survived through anchors or structure zones.
- Repairs preserved vanilla pylons so they register correctly again after regen.
- Automatically reloads the regenerated world and places the player at a valid spawn.
- Runs an automatic regen scheduler with a configurable interval in either in-game days or real-world days.
- Supports multiplayer world-tool syncing, permissions, and server-admin `/regenworld`.
- Supports optional scheduled multiplayer regen using the same countdown/disconnect flow as manual server regen.
- **NEW:** Advanced configuration system to control regen behavior (seed randomization, evil type preservation, cheat gating).
- **NEW:** Calamity Mod progression tracking and restoration support.
- **NEW:** Per-tile anchor system now supports modded tiles, walls, and container items via tModLoader serialization.
- **NEW:** Unified world tool overlay—hold any tool to see all anchors, erasures, and structure zones together.
- **NEW:** Building zone command suite (`/dwzone`, `/clearzones`) with structure zone management.

## Important Current Limits

- Multiplayer regen is a server-maintenance flow, not a seamless in-world live regen. Connected players are warned, disconnected, then rejoin after the server finishes regenerating the world.
- `/multiregen` is still single-player only.
- Vanilla pylons are relocated and repaired by `Biome Dowser` if preserved in a pylon zone, but relocation depends on finding a valid matching biome at the destination.
- `Preserve Dungeon Side` and `Preserve Biome Features` exist in config as planned options, but they are not implemented yet.
- Scheduled regen can now be enabled or disabled in config, its day interval can be customized, and it can use either in-game days or real-world days. Multiplayer scheduling is separately gated so servers can keep manual regen only if they prefer.
- Calamity Mod compatibility requires Calamity to be installed; the mod does not automatically detect other major content mods yet.

## Quick Start

1. Enter a world. With `Automatically Give Tools` enabled (the default), Dynamic Worlds supplies the four core preservation tools if you do not already have them.
2. Use `Reality Anchor` for exact tiles you never want to lose.
3. Use `Structure Anchor` for houses, bases, and large builds you want moved onto the new terrain as a unit.
4. Use `Reality Eraser` anywhere you always want cleared out after regen.
5. Run `/regenworld` when you are ready, or wait for the configured automatic cycle to trigger.

## How Regeneration Works

When regen starts, the flow is:

1. Capture world progression, NPC roster, housing data, player spawn context, anchored tiles, erased tiles, structure zones, and preserved container contents.
2. Save and quit the current world cleanly.
3. Switch to a menu-style loading screen.
4. Run normal Terraria/tModLoader world generation with a random seed, the current seed, or a seed you supplied.
5. Reapply preserved progression and config-aware world settings.
6. Clear erased tiles.
7. Restore structure zones.
8. Restore anchored tiles and preserved container contents.
9. Reassign preserved NPC housing where the restored room is still valid.
10. Re-register preserved vanilla pylons.
11. Reload the regenerated world automatically and place the player at bed spawn or world spawn.

The key practical detail is that regen does not continue inside live gameplay. The world is saved and exited first, then generation happens from the loading screen.

## What Gets Preserved

### World Progression

- Hardmode state
- World evil flag
- Game mode
- Boss progression
- Event and invasion progression
- Pre-Hardmode and Hardmode ore tiers
- World name, world ID, and seed metadata

### NPC and Housing State

- Town NPC roster, including mod town NPC type IDs
- NPC display names
- NPC position snapshots for respawn placement
- Saved housing assignments when the house survives through anchors or structure zones

### Player State Related to Regen

- Bed spawn when the bed survives
- Safe fallback to world spawn when the bed does not survive
- Post-load featherfall safety buffer
- Last-position save suppression during regen so the player is not pulled back to a stale spot

### Preserved World Data

- Exact anchored tiles, including walls, liquids, wires, slopes, and actuators
- Structure zones with full tile snapshots
- Chest and dresser contents from anchored tiles and structure zones
- Best-effort tile entity extra data on anchored tiles

## Mod Systems

These are the main runtime systems in the mod and what they are responsible for.

| System | File | Purpose |
| --- | --- | --- |
| `WorldProgressUtil` and `RoguelikeWorldSystem` | `DynamicWorlds/WorldProgress.cs` | Capture, save, load, and print the world progression snapshot. |
| `DynamicWorldRegenSystem` | `DynamicWorlds/RegenLoadingSystem.cs` | Handles the save-and-quit handoff, loading screen, background world generation, reload, and post-regen player placement. |
| `SingleplayerRegenHelper` | `DynamicWorlds/regenworldcommand.cs` | Builds the pending regen snapshot, applies preserved data back into the new world, and restores NPCs and housing. |
| `WorldRegenScheduler` | `DynamicWorlds/WorldRegenScheduler.cs` | Tracks the configured regen cycle, announces countdowns, and triggers automatic regen at midnight on regen day. |
| `AnchoredTileSystem` | `DynamicWorlds/AnchoredTiles.cs` | Stores anchored tiles, captures container contents, enforces anchor limits, and restores anchored data after regen. |
| `ErasedTileSystem` | `DynamicWorlds/ErasedTiles.cs` | Stores tiles marked for erasure and clears them before anchored and zoned content is restored. |
| `StructureAnchorSystem` | `DynamicWorlds/StructureAnchor.cs` | Stores structure zones, preserves full builds, translates them vertically to new ground, and keeps zone metadata up to date. |
| `BiomeDowserSystem` | `DynamicWorlds/BiomeDowser.cs` | **NEW:** Manages pylon zones, detects biome types, and intelligently relocates preserved pylon structures to matching biomes during regen. |
| `PylonRestoreHelper` | `DynamicWorlds/PylonRestoreHelper.cs` | Recreates missing vanilla pylon tile entities after restore and refreshes the vanilla pylon system. |
| `CalamityCompat` | `DynamicWorlds/CalamityCompat.cs` | **NEW:** Detects Calamity Mod and preserves boss progression, world events, crafting unlocks, and acid rain state across regen. |
| `WorldToolOverlayHelper` | `DynamicWorlds/WorldToolOverlayHelper.cs` | **NEW:** Unified overlay rendering system for all world tools. |

## Mod Compatibility

### Calamity Mod Support

Dynamic Worlds now detects and preserves **Calamity Mod** progression across world regenerations:

**Calamity Features Preserved:**
- All boss defeat flags and progression
- Difficulty modifiers (Revenge, Death, Armageddon modes)
- Acid Rain event state and progression
- Boss Rush mode state
- Recipe unlocks (Draedon's Arsenal, etc.)
- Special world conditions (Permafrost, Astral Biome presence)
- Counters and timers for ongoing events

When Calamity is detected, the mod automatically captures and restores these systems during regeneration, so your progression through Calamity's content is preserved alongside vanilla progression.

### General Modded Content Support

Dynamic Worlds is intentionally designed to be friendly to modded content:

- **Modded Tiles & Walls:** Anchored tiles store tile and wall IDs as `ushort`, so any modded tile or wall can be preserved if the mod is still installed.
- **Modded Items in Chests:** Container contents are serialized through tModLoader's item system, so modded items in preserved chests and dressers are intended to survive regen.
- **Modded Town NPCs:** Town NPC roster snapshots include modded NPC type IDs, so modded town NPCs can be respawned if their mod is present.
- **Tile Entity Extra Data:** Best-effort support for custom tile entity data restoration on anchored tiles.

**Practical Rule:** If the mod that added the item, tile, wall, or NPC is still installed after regen, Dynamic Worlds has a good chance of preserving it.

### Known Compatibility Cautions

Compatibility is weaker for:
- Mods with custom or unusual tile entities (beyond vanilla chest-like containers)
- Mods with custom pylon implementations
- Mods that heavily rewrite worldgen or post-gen placement
- Major worldgen-overhaul mods that depend on specific generation order
| `DynamicWorldsPlayer` | `DynamicWorlds/player.cs` | Gifts tools, stores safe player position data, and handles post-regen player entry behavior. |
| `GuideGlobalNPC` | `DynamicWorlds/GuideDialogue.cs` | Adds a Guide dialogue page that explains the `Reality Anchor`. |

## World Tools

By default, entering a world supplies any missing preservation tools. Disable `Automatically Give Tools` to opt out. You can also get them with `/dwtools` or individually with `/dwtool`, subject to the configured permissions.

Holding any one of the three tools shows all saved world overlays at once:

- anchored tiles
- erased tiles
- structure zones

### Reality Anchor

Use `Reality Anchor` when you want exact tiles to survive every regen.

How to use it:

- Left-click a tile to anchor or unanchor it.
- Click and drag to anchor or unanchor an entire rectangle.
- Right-click the item in your inventory to restore all anchored tiles immediately.

What it preserves:

- tiles
- walls
- liquids
- wires
- slopes and half blocks
- actuators
- container contents for anchored chests and dressers

Rules and limits:

- Tiles inside a structure zone cannot also be individually anchored.
- The anchor cap scales with progression from 5,000 to 100,000 tiles.
- `Anchor Tile Cap Override` in the server config can set a fixed limit from 1 to 1,000,000. Its default, 0, keeps progression limits. Lowering it preserves existing anchors and still permits removal; new additions wait until there is capacity. Only trusted players can change server config in multiplayer.
- Inventory right-click restore is gated by `Allow Cheats`.

Good use cases:

- beds
- wiring hubs
- farms with exact tile layouts
- chests with important items
- small handcrafted terrain details

### Reality Eraser

Use `Reality Eraser` when you want certain tiles to be empty space after every regen.

How to use it:

- Left-click a tile to mark or unmark it.
- Click and drag to mark or unmark a rectangle.
- Right-click the item in your inventory to clear all marked tiles immediately.

How it behaves:

- Erased tiles are cleared before anchored tiles are restored.
- If the same location is both erased and later restored by an anchor or structure zone, the preserved data wins because restoration happens afterward.

Rules and limits:

- The erasure cap scales from 50,000 to 1,000,000 tiles.
- Inventory right-click clear is gated by `Allow Cheats`.

Good use cases:

- clearing tunnels
- keeping arenas open
- forcing surface access points to stay empty
- clearing out naturally regenerated clutter near bases

### Structure Anchor

Use `Structure Anchor` when you want to preserve a full building or build area without anchoring every tile one by one.

How to use it:

- Click and drag to create a structure zone.
- Create as many separate structure zones as you want.
- Shift-click inside a structure zone to remove it.
- Use `/dwzone` and `/clearzones` for command-based zone management.

What it preserves:

- every tile inside the rectangle
- contained chest and dresser contents
- beds and housing geometry inside the zone
- surrounding build details included in the rectangle

How restoration works:

- The zone is restored as a unit.
- The build is translated vertically so its base settles onto the new ground at the same horizontal area.
- Small gaps under the restored footprint are bridged.
- Floating builds remain floating instead of getting a giant dirt pillar to the surface.

Rules:

- Structure zones cannot overlap other structure zones.
- Structure zones cannot overlap individually anchored tiles.

Good use cases:

- full houses
- village blocks
- hellevator entrance compounds
- sky builds
- biome outposts

### Biome Dowser

Use `Biome Dowser` when you want to preserve vanilla pylon structures and have them automatically relocate to matching biomes during regeneration.

How to use it:

- Click and drag to create a pylon zone around a vanilla pylon structure.
- Right-click the item in your inventory to cycle through placement modes: Surface → Underground → Floating → Surface.
- Create as many separate pylon zones as you want.
- Shift-click inside a pylon zone to remove it.
- Use `/dwzone` and `/clearzones` for command-based zone management.

What it preserves:

- the vanilla pylon and all connected tiles in the zone
- chest and dresser contents inside the zone
- the structure's original arrangement

How restoration works:

- The Biome Dowser scans the terrain around the original pylon location for a matching biome.
- It evaluates placement candidates based on the chosen mode (Surface = ground level, Underground = below surface, Floating = sky placement).
- It scores candidate locations by how centrally the pylon sits within the detected biome (more central = better stability).
- The structure is relocated to the best matching location, with the pylon remaining functional.
- If no valid matching biome is found, the pylon stays at its original location.

Supported pylon types and modes:

| Pylon Type | Surface | Underground | Floating |
| --- | --- | --- | --- |
| Surface Purity | ✓ | ✗ | ✓ |
| Jungle | ✓ | ✓ | ✗ |
| Hallow | ✓ | ✓ | ✓ |
| Underground | ✗ | ✓ | ✗ |
| Desert | ✓ | ✓ | ✗ |
| Snow | ✓ | ✓ | ✗ |
| Beach | ✓ | ✗ | ✗ |
| Glowing Mushroom | ✗ | ✓ | ✗ |
| Victory | ✓ | ✗ | ✓ |

Rules:

- Pylon zones must contain exactly one vanilla pylon.
- Pylon zones cannot overlap other structure zones.
- Pylon zones cannot overlap individually anchored tiles.

Good use cases:

- pylon outposts and towers
- biome-specific teleport networks
- sky-island pylon bases
- underground pylon systems
- preserving your pylon network across major terrain regenerations

## Commands

| Command | What it does | Notes |
| --- | --- | --- |
| `/regenworld [seed]` | Regenerates the world with an optional explicit seed. | Single-player uses the loading-screen flow. Multiplayer uses the server countdown, disconnect, regenerate, and reconnect flow. |
| `/multiregen <count> [seed]` | Runs multiple full regen cycles back-to-back. | Single-player only. Useful for testing and content creation. |
| `/snap` | Prints the current captured world progression snapshot. | Also prints the current scheduled regen status. |
| `/dwtools [player]` | Gives all Dynamic Worlds tools again. | Can target another player in multiplayer. |
| `/dwtool <anchor|eraser|structure|dowser|prefab|all> [player]` | Gives one specific tool or the full set. | Useful for server admins and testing. |
| `/dwinfo` | Prints counts and bounds for saved anchors, erasures, structure zones, and Biome Dowser zones. | Useful for sanity checking saved data. |
| `/dwzone` | Lists structure zones. | Equivalent to `/dwzone list`. |
| `/dwzone list` | Lists all saved structure zones. | Shows size and coordinates. |
| `/dwzone clear <id>` | Removes one structure zone by ID. | Also accepts `/dwzone remove <id>`. |
| `/dwzone clearall` | Removes every structure zone. | Leaves anchors, erasures, and Biome Dowser zones alone. |
| `/clearzones` | Removes all structure zones. | Multiplayer-safe when the caller has zone permission. |
| `/dwperm` | Views or changes Dynamic Worlds multiplayer permissions. | Controls who can edit tools, use regen, use cheats, and more. |
| `/dwregenconfig` | Views or changes multiplayer regen scheduler settings. | Lets admins change countdowns, scheduler mode, interval, and multiplayer scheduling without opening config UI. |
| `/revealmap` | Reveals the map for the current character. | Requires `Allow Cheats`. |
| `/hardmode [on|off]` | Forces Hardmode on or off. | Requires `Allow Cheats`. |
| `/down <bossOrEvent>` | Marks a boss or event as defeated. | Requires `Allow Cheats`. |
| `/killduplicatenpcs` | Removes duplicate town NPCs and keeps one of each type. | Cleanup tool. |

Supported `/down` targets:

```text
eye, evil, skeletron, queenbee, kingslime, deerclops,
mech1, mech2, mech3, plantera, golem, fishron, moonlord,
goblins, frost, pirates, martians, pumpkin, frostmoon
```

## Config Options

Dynamic Worlds currently uses the following server-side config values and planned config placeholders:

| Setting | What it means today | Notes |
| --- | --- | --- |
| `Enable Scheduled Regen` | Turns the automatic regen scheduler on or off. | When disabled, scheduled progress is paused. |
| `Scheduled Regen Interval (Days)` | Sets how many days pass between scheduled regens. | Used by both in-game-day and real-world-day scheduling. |
| `Scheduled Regen Mode` | Chooses whether scheduled regen uses Terraria in-game days or real-world days. | `InGameDays` is the original behavior. `RealWorldDays` is useful for servers. |
| `Enable Multiplayer Regen` | Allows manual multiplayer `/regenworld`. | Multiplayer regen uses the countdown/disconnect/reconnect flow. |
| `Enable Scheduled Multiplayer Regen` | Allows the automatic scheduler to queue multiplayer regens. | Lets servers keep manual regen only if they prefer. |
| `Multiplayer Regen Countdown (Seconds)` | Sets how long multiplayer regen warns players before disconnecting them. | `0` starts immediately. |
| `Multiplayer Regen Disconnect Timeout (Seconds)` | Sets how long the server waits for players to disconnect before cancelling regen. | Clamped to at least 5 seconds. |
| `Announce Multiplayer Regen Countdown` | Controls whether countdown updates are broadcast in chat. | Useful for quieter private servers. |
| `World Tool Edit Permission` | Controls who can place or remove anchors, erasures, structure zones, and Biome Dowser zones in multiplayer. | Uses the `Anyone / Trusted / Console` permission model. |
| `Zone Command Permission` | Controls who can run zone-management commands in multiplayer. | Applies to `/dwzone` and `/clearzones`. |
| `Tool Command Permission` | Controls who can use `/dwtools` and `/dwtool` in multiplayer. | Good for admin-only tool distribution. |
| `Regen Command Permission` | Controls who can run `/regenworld` and `/multiregen`. | Recommended `Console Only` on public servers. |
| `Cheat Command Permission` | Controls who can use cheat/admin commands in multiplayer. | Works together with `Allow Cheats`. |
| `Trusted Players` | List of player names treated as trusted for the permission system. | Name matching is case-insensitive. |
| `Allow Cheats` | Enables cheat-gated features such as `/hardmode`, `/down`, and inventory right-click actions on `Reality Anchor` and `Reality Eraser`. | Recommended off for normal play, on for testing/admin use. |
| `Biome Dowser Regen Chat Log` | Shows detailed Biome Dowser placement summaries after regen. | Useful for tuning pylon zone behavior. |
| `Regenerate World on Death` | Triggers regen automatically when the player dies. | Single-player only. |
| `Preserve Evil Type` | Keeps Crimson/Corruption matching the previous world after regen. | Turn this off if you want regen to roll a new evil. |
| `Preserve Dungeon Side` | Planned setting only. | Documented for future work, not enforced yet, and may not appear in every config UI until that implementation is finished. |
| `Preserve Biome Features` | Planned setting only. | Documented for future work, not enforced yet, and may not appear in every config UI until that implementation is finished. |
| `Randomize World Each Regen` | Chooses whether each regen uses a fresh seed or reuses the current world seed when available. | If you want repeatable regens, turn this off or pass an explicit seed. |

## Pylons

Dynamic Worlds now fully supports vanilla pylon preservation and relocation through the `Biome Dowser` system:

**How Biome Dowser Works:**
- Create a pylon zone by dragging around a vanilla pylon structure with the Biome Dowser tool.
- Choose a placement mode for that pylon (Surface, Underground, or Floating) based on where you want it to be able to relocate.
- During regen, the mod scans for a matching biome and intelligently places the pylon in a good location within that biome.
- The relocated pylon is re-registered and works immediately after regen.
- All tiles in the pylon zone (not just the pylon itself) are preserved as a unit.

**When a Pylon Cannot Relocate:**
- If no matching biome is found in the new world, the pylon stays at its original location and is still re-registered.
- If the original location happens to be valid for that pylon type in the new terrain, the pylon can still function there.

**Practical Benefits:**
- Run multiple regens without rebuilding your pylon network each time.
- Keep pylon coverage balanced across different biomes.
- Preserve sky-island pylon bases that would otherwise settle to the ground.
- Maintain biome-specific teleport routes and outposts.

**Compatibility:**
- Biome Dowser only works with vanilla pylons.
- Custom modded pylon implementations are not currently supported.
- For broader multiplayer/server setup guidance, see the [server admin guide](./SERVER_ADMIN_GUIDE.md).

## Compatibility

### Base Compatibility Expectations

Dynamic Worlds works best when:

- you keep the same mod list loaded before and after regen
- you use multiplayer as an admin-controlled maintenance flow rather than expecting seamless live in-world regeneration
- the other mods do not fundamentally break Terraria/tModLoader world generation or world reload flow

### Modded Tiles, Walls, Items, and NPCs

The mod is intentionally fairly friendly to content mods:

- Anchored tiles store tile IDs and wall IDs as `ushort`, so modded tiles and walls are part of the saved snapshot.
- Anchored and zoned container contents are serialized through tModLoader item data, so modded items inside chests and dressers are intended to survive.
- Town NPC snapshots include mod town NPC type IDs and names, so modded town NPCs can be respawned if the mod is still present.

Practical rule: if the same mod that added the item, tile, wall, or NPC is still installed after regen, Dynamic Worlds has a good chance of preserving it.

### Known Weaker Areas

Compatibility is weaker for:

- mods with unusual tile entities
- mods with custom pylons
- mods that depend on very specific worldgen placements
- mods that heavily rewrite worldgen flow or post-gen placement in ways Dynamic Worlds does not know about

Dynamic Worlds does generic best-effort tile entity extra-data restore for anchored tiles, but special-case repair currently only exists for vanilla pylons.

## FAQ

### Can I use this on multiplayer servers?

Yes, with an important distinction: multiplayer regen is handled as a server maintenance flow.

When a server admin runs `/regenworld`, Dynamic Worlds:

- snapshots the world and preserved data
- announces a countdown
- disconnects players
- regenerates and restores the world on the server
- lets players reconnect and returns them to a valid spawn

Tool syncing, zone syncing, permissions, and scheduled multiplayer regen are supported. `/multiregen` is still single-player only.

### Can I schedule regen by real-world days instead of Terraria days?

Yes. Set `Scheduled Regen Mode` to `RealWorldDays` and choose the interval with `Scheduled Regen Interval (Days)`.

That works in both single-player and multiplayer, though multiplayer also requires `Enable Scheduled Multiplayer Regen` if you want the automatic scheduler to queue server regens.

### Does regeneration force me out of the world, or does it happen while I keep playing?

Regen starts from in-game, but it does not continue in live gameplay. The mod saves and exits the world first, shows a loading screen, generates the new world there, then loads you back in automatically.

That means you are not walking around, mining, building, or breaking tiles while the actual regeneration is happening. This avoids the worst race-condition problems that a truly in-place live regen would create.

### Could moving around or breaking stuff during the process cause issues?

Once regen starts, the world is immediately saved and exited, so there is no live world to keep changing during generation. The bigger risks are not about movement during the load screen. They are:

- forgetting to anchor a critical tile or build
- assuming a pylon will automatically relocate to a valid biome
- expecting surrounding terrain to be rebuilt around a structure when only the structure itself was preserved

The command also refuses to start if a boss is alive.

### How does this work with pylons? Will it swap them to the appropriate biome?

Yes! The `Biome Dowser` system now handles intelligent pylon relocation. Create a pylon zone around your pylon structure, choose a placement mode (Surface/Underground/Floating), and the mod will attempt to relocate it to a matching biome during regen.

The relocation works by:
1. Scanning the terrain for a matching biome type around the new world location
2. Evaluating placement candidates based on your chosen mode
3. Scoring locations by how central the pylon is within that biome (more central = more stable)
4. Relocating the entire pylon zone to the best match
5. Re-registering the pylon so it functions immediately

If no matching biome is found, the pylon stays at its original location and is still re-registered to function. This means your pylon network is preserved across regens and automatically adapts to the new terrain.

### Does this work with older worlds, including worlds that began in 1.3?

If the world can be loaded in your current Terraria/tModLoader setup, Dynamic Worlds can still snapshot the current playthrough state and regenerate from there.

What it cannot do is reconstruct an original unknown 1.3 seed or reproduce old 1.3 generation exactly. Regeneration uses the current Terraria/tModLoader world generation pipeline, not historical 1.3 terrain generation.

If you imported an old world and do not know the original seed:

- the mod can still regen the world
- the preserved builds, progression, NPCs, anchors, and zones can still carry forward
- exact repeatability is not guaranteed unless you pass an explicit seed yourself

If you want repeatable future regens on an old world, run `/regenworld <yourchosenseed>` with a seed you want to keep using.

### Can I use regen to replace my world evil?

Yes, with an important caveat.

If `Preserve Evil Type` is enabled, the regen result is forced back to the original world's evil. If you turn that setting off, the regenerated world keeps the evil chosen by the new worldgen result.

So you can use Dynamic Worlds to reroll from Corruption to Crimson or the other way around, but:

- it is not currently a direct "pick Crimson" toggle
- you may want to use a known seed if you want a specific outcome
- preserved anchored or zoned areas keep the blocks you preserved, so old evil blocks inside protected regions stay as captured

### Does Structure Anchor generate new terrain around a house so it ends up buried or naturally embedded?

Not in a fully procedural "wrap the whole house in new terrain" way.

What `Structure Anchor` does today:

- moves the structure vertically so it settles at the new ground level
- bridges small support gaps under the footprint
- avoids filling dirt all the way down under floating-island placements

What it does not do:

- generate a large custom hill around the build
- fully bury or encase the structure in newly created terrain unless those surrounding terrain tiles were part of the saved zone

If you want a house to remain embedded in dirt, stone, or another surrounding material, include that surrounding terrain in the structure zone or anchor it separately.

### Does it work with mods, and will Reality Anchor preserve modded items in chests?

In many cases, yes.

Reality Anchor and Structure Anchor both preserve captured chest and dresser contents, and the save format is intended to support modded items through tModLoader item serialization. As long as the same content mods are still installed, modded items in preserved containers have a good chance of surviving regen correctly.

The same general rule applies to modded tiles, walls, and town NPCs: they are much more likely to work if the supplying mod is still present and compatible after the regen.

The main caution points are modded tile entities, custom pylons, and major worldgen-overhaul mods.

## Snapshot Files

World progression snapshots are written next to the world file when possible:

```text
YourWorld.wld
YourWorld_progress.json
```

If that path is unavailable, the mod falls back to:

```text
tModLoader/DynamicWorlds/WorldProgress.json
```

## Optional tool gifts

`Automatically Give Tools` is enabled by default and supplies missing preservation tools whenever a player enters a world. Disable it in the server config to opt out. Existing tools remain, and `/dwtools` still works under the configured tool-command permissions.

## Build Notes

For local source builds, place the source under:

```text
tModLoader/ModSources/DynamicWorlds
```

Then build from Mod Sources inside tModLoader.

If you are building from this repo directly, be aware that local `dotnet build` can fail if your installed tModLoader runtime references do not match the target framework configured by the project.
