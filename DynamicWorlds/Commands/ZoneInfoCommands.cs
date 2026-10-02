using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;

namespace DynamicWorlds
{
    /// <summary>
    /// /dwinfo — prints a summary of all saved anchored tiles, erased tiles, structure zones, and Biome Dowser zones.
    /// </summary>
    public class DwInfoCommand : ModCommand
    {
        public override CommandType Type => CommandType.Chat;
        public override string Command => "dwinfo";
        public override string Usage => "/dwinfo";
        public override string Description =>
            "Shows a summary of all anchored tiles, erased tiles, structure zones, and Biome Dowser zones saved for this world.";

        public override void Action(CommandCaller caller, string input, string[] args)
        {
            // ── Anchored tiles ────────────────────────────────────────────────
            int anchorCount = AnchoredTileSystem.AnchoredTiles.Count;
            int anchorCap   = AnchoredTileSystem.GetTileCap();
            Main.NewText($"[Anchored Tiles] {anchorCount} / {anchorCap}", 100, 200, 255);

            if (anchorCount > 0)
            {
                int minX = int.MaxValue, maxX = int.MinValue;
                int minY = int.MaxValue, maxY = int.MinValue;
                foreach (var pos in AnchoredTileSystem.AnchoredTiles.Keys)
                {
                    if (pos.X < minX) minX = pos.X;
                    if (pos.X > maxX) maxX = pos.X;
                    if (pos.Y < minY) minY = pos.Y;
                    if (pos.Y > maxY) maxY = pos.Y;
                }
                Main.NewText($"  Bounding box: ({minX},{minY}) → ({maxX},{maxY})", 80, 170, 220);
            }

            // ── Erased tiles ──────────────────────────────────────────────────
            int eraseCount = ErasedTileSystem.ErasedTiles.Count;
            int eraseCap   = ErasedTileSystem.GetErasureCap();
            Main.NewText($"[Erased Tiles]   {eraseCount} / {eraseCap}", 255, 120, 120);

            if (eraseCount > 0)
            {
                int minX = int.MaxValue, maxX = int.MinValue;
                int minY = int.MaxValue, maxY = int.MinValue;
                foreach (var pos in ErasedTileSystem.ErasedTiles)
                {
                    if (pos.X < minX) minX = pos.X;
                    if (pos.X > maxX) maxX = pos.X;
                    if (pos.Y < minY) minY = pos.Y;
                    if (pos.Y > maxY) maxY = pos.Y;
                }
                Main.NewText($"  Bounding box: ({minX},{minY}) → ({maxX},{maxY})", 220, 80, 80);
            }

            // ── Building zones ────────────────────────────────────────────────
            int zoneCount = StructureAnchorSystem.Zones.Count;
            Main.NewText($"[Structure Zones] {zoneCount} zone{(zoneCount == 1 ? "" : "s")}", 180, 255, 180);

            foreach (var kv in StructureAnchorSystem.Zones)
            {
                var z = kv.Value;
                string tileSummary = Main.netMode == NetmodeID.MultiplayerClient && z.Tiles.Count == 0
                    ? "synced metadata"
                    : $"{z.Tiles.Count} tiles";
                Main.NewText(
                    $"  Zone #{z.Id}: ({z.TopLeft.X},{z.TopLeft.Y}) → ({z.BottomRight.X},{z.BottomRight.Y})  " +
                    $"{z.Width}×{z.Height}  {tileSummary}  groundY={z.SavedGroundY}",
                    100, 220, 140);
            }

            int dowserZoneCount = BiomeDowserSystem.Zones.Count;
            Main.NewText($"[Biome Dowser Zones] {dowserZoneCount} zone{(dowserZoneCount == 1 ? "" : "s")}", 255, 215, 120);

            foreach (var kv in BiomeDowserSystem.Zones)
            {
                var z = kv.Value;
                string tileSummary = Main.netMode == NetmodeID.MultiplayerClient && z.Zone.Tiles.Count == 0
                    ? "synced metadata"
                    : $"{z.Zone.Tiles.Count} tiles";
                Main.NewText(
                    $"  Zone #{z.Id}: ({z.TopLeft.X},{z.TopLeft.Y}) → ({z.BottomRight.X},{z.BottomRight.Y})  " +
                    $"{z.Width}×{z.Height}  {tileSummary}  pylon={z.PylonType}  mode={BiomeDowserPlacementHelper.GetLabel(z.PlacementMode)}",
                    255, 200, 120);
            }

            if (anchorCount == 0 && eraseCount == 0 && zoneCount == 0 && dowserZoneCount == 0)
                Main.NewText("No Dynamic Worlds data saved for this world.", 180, 180, 180);
        }
    }

    /// <summary>
    /// /clearzones — removes all structure zones from the world.
    /// </summary>
    public class ClearZonesCommand : ModCommand
    {
        public override CommandType Type => CommandType.World | CommandType.Console;
        public override string Command => "clearzones";
        public override string Usage => "/clearzones";
        public override string Description =>
            "Removes all structure anchor zones from the world (does not affect anchored or erased tiles).";

        public override void Action(CommandCaller caller, string input, string[] args)
        {
            if (!DynamicWorldsPermissions.CanManageZones(caller, out string deniedReason))
            {
                DynamicWorldsPermissions.ReplyDenied(caller, deniedReason);
                return;
            }

            int count = StructureAnchorSystem.Zones.Count;
            if (count == 0)
            {
                caller.Reply("No structure zones to clear.", new Color(180, 180, 180));
                return;
            }

            StructureAnchorSystem.Zones.Clear();
            caller.Reply($"Cleared {count} structure zone{(count == 1 ? "" : "s")}.", new Color(255, 150, 100));
        }
    }
}
