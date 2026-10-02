# Dynamic Worlds Server Admin Guide

This guide covers the multiplayer/server side of `Dynamic Worlds`: permissions, manual regen, scheduled regen, backups, and what to expect from the reconnect flow.

## What Multiplayer Currently Supports

Dynamic Worlds now supports these multiplayer features:

- synced `Reality Anchor`, `Reality Eraser`, `Structure Anchor`, and `Biome Dowser` zones
- server-authoritative zone edits
- admin/console-controlled `/regenworld`
- configurable multiplayer regen countdown and disconnect timing
- optional scheduled multiplayer regen
- automatic pre-regen backups
- player reconnect handling after regen, including translated bed-spawn restore when possible

## What Multiplayer Does Not Do Yet

- `/multiregen` is still single-player only
- multiplayer regen is not seamless; players are disconnected and then reconnect after regen finishes
- there is still no cron-style scheduler with exact clock times; the built-in scheduler uses either in-game days or real-world days
- players do not vote on regen by default; server admins decide when it runs

## Recommended First-Time Setup

Open the Dynamic Worlds server config and decide these first:

- `Enable Scheduled Regen`
- `Scheduled Regen Interval (Days)`
- `Scheduled Regen Mode`
- `Enable Multiplayer Regen`
- `Enable Scheduled Multiplayer Regen`
- `Multiplayer Regen Countdown (Seconds)`
- `Multiplayer Disconnect Timeout (Seconds)`
- `Announce Multiplayer Regen Countdown`

Recommended safe starting values:

- `Enable Scheduled Regen`: `true`
- `Scheduled Regen Interval (Days)`: `7`
- `Scheduled Regen Mode`: `In-Game Days`
- `Enable Multiplayer Regen`: `true`
- `Enable Scheduled Multiplayer Regen`: `false`
- `Multiplayer Regen Countdown (Seconds)`: `30`
- `Multiplayer Disconnect Timeout (Seconds)`: `20`
- `Announce Multiplayer Regen Countdown`: `true`

That gives you manual server regen first, with enough time to warn players before trying scheduled server regen.

## Multiplayer Permission Model

Dynamic Worlds uses permission modes for multiplayer:

- `Anyone`
- `TrustedPlayers`
- `ConsoleOnly`

These apply separately to:

- world tool edits
- zone commands
- tool-give commands
- regen commands
- cheat commands

Trusted players are matched by name.

Useful permission commands:

- `/dwperm`
- `/dwperm tools trusted`
- `/dwperm zones trusted`
- `/dwperm give anyone`
- `/dwperm regen console`
- `/dwperm cheats console`
- `/dwperm trusted add PlayerName`
- `/dwperm trusted remove PlayerName`

Recommended server setup:

- `tools`: `trusted`
- `zones`: `trusted`
- `give`: `anyone` or `trusted`
- `regen`: `console`
- `cheats`: `console`

## Manual Multiplayer Regen

Run:

- `/regenworld`
- `/regenworld 12345`
- `/regenworld myseedname`

What happens:

1. Dynamic Worlds snapshots progression, preserved tiles, zones, housing, and player spawn data.
2. It saves the current world and creates a pre-regen backup.
3. Connected players are warned with the configured countdown.
4. The server disconnects players with a short “rejoin in a moment” message.
5. The server generates the new world, restores preserved data, and saves again.
6. Players reconnect.
7. Dynamic Worlds returns each player to their translated bed spawn if it still works, or world spawn if it does not.

This is intentionally a maintenance-style flow. It is much safer than trying to regenerate the world while everyone stays online.

## Scheduled Multiplayer Regen

The scheduler can now use either:

- `In-Game Days`
- `Real-World Days`

To enable scheduled server regen:

1. Turn on `Enable Scheduled Regen`
2. Set `Scheduled Regen Interval (Days)`
3. Choose `Scheduled Regen Mode`
4. Turn on `Enable Multiplayer Regen`
5. Turn on `Enable Scheduled Multiplayer Regen`

Once all of those are enabled, the scheduler:

- tracks either in-game days or real-world days on the server
- warns as the deadline approaches
- queues the normal multiplayer regen flow when the interval expires

If a boss is alive or another regen is already in progress, scheduled regen is delayed instead of forcing through.

## Multiplayer Regen Config Command

You can adjust the multiplayer regen flow from chat or console:

- `/dwregenconfig`
- `/dwregenconfig scheduler on`
- `/dwregenconfig interval 7`
- `/dwregenconfig clock real`
- `/dwregenconfig enable on`
- `/dwregenconfig schedule on`
- `/dwregenconfig countdown 30`
- `/dwregenconfig timeout 20`
- `/dwregenconfig announce off`

What the scheduler settings do:

- `scheduler`: turns the main automatic regen scheduler on or off
- `interval`: sets how many days each regen cycle lasts
- `clock`: switches the scheduler between `ingame` days and `real` days

What the multiplayer regen settings do:

- `enable`: turns manual multiplayer `/regenworld` on or off
- `schedule`: turns scheduled multiplayer regen on or off
- `countdown`: how long players are warned before disconnect
- `timeout`: how long the server waits for players to drop before cancelling regen
- `announce`: whether countdown updates are broadcast in chat

## Tool Commands

Give every tool:

- `/dwtools`
- `/dwtools PlayerName`

Give an individual tool:

- `/dwtool anchor`
- `/dwtool eraser`
- `/dwtool structure`
- `/dwtool dowser`
- `/dwtool prefab`
- `/dwtool all`

You can also target another player:

- `/dwtool structure PlayerName`

## Backups

Before regen starts, Dynamic Worlds keeps a rolling backup of the pre-regen world.

Backups include:

- the `.wld`
- the `.twld` when present
- the Dynamic Worlds progress sidecar
- a small info file describing the backup

Backups are kept in a per-world folder and pruned to a reasonable recent history automatically.

## Good Testing Checklist

Before enabling scheduled multiplayer regen on a live server, test this flow:

1. Join with two players.
2. Place a few anchors, erasures, and a structure zone.
3. Run `/regenworld`.
4. Confirm the countdown appears.
5. Confirm both players are disconnected.
6. Reconnect.
7. Confirm preserved data restored correctly.
8. Confirm players landed at a valid spawn.
9. Check the backup folder exists.

Then test scheduled regen separately by lowering the interval for a throwaway world.

## Practical Recommendations

- Keep `regen` permission at `console` unless you really trust players.
- Start with scheduled multiplayer regen disabled until you have manually tested `/regenworld`.
- Use a longer countdown on public servers so players have time to get somewhere safe.
- Avoid triggering regen during major boss/event sessions even though the mod already blocks active bosses.
- Keep an eye on the backup folder when testing large modpacks.

## Current Caveats

- `/multiregen` is still single-player only.
- Multiplayer regen is safest when the same mod list stays installed before and after regen.
- Very unusual modded tile entities and custom worldgen systems can still need mod-specific compatibility work.
- Dynamic Worlds is currently built around admin-controlled maintenance regen, not fully live persistent-world regeneration.
