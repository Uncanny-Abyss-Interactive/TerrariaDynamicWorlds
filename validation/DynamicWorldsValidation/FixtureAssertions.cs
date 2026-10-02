using System.Linq;
using DynamicWorlds;
using Terraria;
using Terraria.ID;

namespace DynamicWorldsValidation;

internal static class FixtureAssertions
{
    // These coordinates and items are a deterministic specification, independent of
    // production snapshot data, so a corrupt saved snapshot cannot validate itself.
    internal static bool VerifySavedStructure(out string detail)
    {
        if (StructureAnchorSystem.Zones.Count != 1)
        {
            detail = "Expected exactly one persisted structure fixture.";
            return false;
        }
        var zone = StructureAnchorSystem.Zones.Values.Single();
        if (zone.Width != 7 || zone.Height != 5 || zone.Tiles.Count != 35)
        {
            detail = "Structure dimensions or captured cell count changed.";
            return false;
        }
        for (int x = 0; x < 7; x++)
        {
            for (int y = 0; y < 5; y++)
            {
                int expected = y == 4 ? TileID.BlueDungeonBrick
                    : y == 3 && x == 0 ? TileID.GoldBrick
                    : y == 3 && x == 6 ? TileID.SilverBrick
                    : (x == 2 || x == 3) && (y == 2 || y == 3) ? TileID.Containers : -1;
                Tile tile = Framing.GetTileSafely(zone.TopLeft.X + x, zone.TopLeft.Y + y);
                if (tile.HasTile != (expected >= 0) || (expected >= 0 && tile.TileType != expected))
                {
                    detail = $"Structure relative cell ({x},{y}) mismatched at ({zone.TopLeft.X + x},{zone.TopLeft.Y + y}).";
                    return false;
                }
            }
        }
        int chestIndex = Chest.FindChest(zone.TopLeft.X + 2, zone.TopLeft.Y + 2);
        if (chestIndex < 0 || Main.chest[chestIndex] == null)
        {
            detail = "Translated structure chest is not registered.";
            return false;
        }
        var items = Main.chest[chestIndex].item;
        bool valid = items[0].type == ItemID.Torch && items[0].stack == 37
            && items[1].type == ItemID.IronPickaxe && items[1].stack == 1 && items[1].prefix == PrefixID.Light;
        detail = valid ? $"All 35 structure cells and latest chest contents match at {zone.TopLeft}."
            : "Translated chest item type, stack, or Light prefix changed.";
        return valid;
    }

    internal static bool VerifyUnprotectedSentinelRemoved()
    {
        Tile tile = Framing.GetTileSafely(270, 110);
        return !tile.HasTile || tile.TileType != TileID.RainbowBrick;
    }
}
