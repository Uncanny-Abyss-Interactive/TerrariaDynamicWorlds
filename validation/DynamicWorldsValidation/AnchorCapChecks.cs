using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DynamicWorlds;
using Newtonsoft.Json;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.ModLoader.Config;
using Terraria.ModLoader.IO;

namespace DynamicWorldsValidation;

// Run on the guarded dedicated-server update thread before or after the normal fixtures.
// This exercises production rectangle admission and serialization without modifying tiles,
// generating terrain, saving a world, or allocating thousands of live tile snapshots.
internal static class AnchorCapChecks
{
    private static readonly string[] CapFlagNames =
    {
        nameof(NPC.downedSlimeKing), nameof(NPC.downedBoss1), nameof(NPC.downedBoss2),
        nameof(NPC.downedBoss3), nameof(NPC.downedQueenBee), nameof(NPC.downedDeerclops),
        nameof(NPC.downedMechBoss1), nameof(NPC.downedMechBoss2), nameof(NPC.downedMechBoss3),
        nameof(NPC.downedPlantBoss), nameof(NPC.downedGolemBoss), nameof(NPC.downedFishron),
        nameof(NPC.downedEmpressOfLight), nameof(NPC.downedAncientCultist), nameof(NPC.downedMoonlord)
    };

    internal static Dictionary<string, bool> ServerChecks()
    {
        if (!ValidationGuard.TryGet(out _, out string reason))
            throw new InvalidOperationException("Anchor-cap checks require the disposable server guard: " + reason);

        var checks = new Dictionary<string, bool>();
        var config = ModContent.GetInstance<DynamicWorldsConfig>();
        int previousOverride = config.AnchorTileCapOverride;
        List<string> previousTrustedPlayers = config.TrustedPlayers;
        bool previousHardMode = Main.hardMode;
        var flags = CapFlagNames.Select(name => typeof(NPC).GetField(name, BindingFlags.Public | BindingFlags.Static)
            ?? throw new MissingFieldException(typeof(NPC).FullName, name)).ToArray();
        bool[] previousFlags = flags.Select(field => (bool)field.GetValue(null)).ToArray();
        var previousAnchors = new Dictionary<Point16, AnchoredTileData>(AnchoredTileSystem.AnchoredTiles);
        var previousChests = new Dictionary<Point16, SavedChestContents>(AnchoredTileSystem.AnchoredChests);
        int playerSlot = -1;
        Player previousPlayer = null;
        try
        {
            // Populate an already nondefault instance to prove a missing new field obtains
            // its documented default through tML's actual serializer settings.
            var legacyConfig = new DynamicWorldsConfig { AnchorTileCapOverride = 17 };
            JsonConvert.PopulateObject("{\"AllowCheats\":true}", legacyConfig, ConfigManager.serializerSettings);
            checks["microfix.anchor_legacy_config_defaults_to_progression"] = legacyConfig.AnchorTileCapOverride == 0;

            foreach (FieldInfo field in flags) field.SetValue(null, false);
            Main.hardMode = false;
            config.AnchorTileCapOverride = 0;
            bool baseCap = AnchoredTileSystem.GetTileCap() == 5_000;
            NPC.downedBoss1 = true;
            bool eyeCap = AnchoredTileSystem.GetTileCap() == 5_500;
            foreach (FieldInfo field in flags) field.SetValue(null, true);
            Main.hardMode = true;
            bool finalCap = AnchoredTileSystem.GetTileCap() == 100_000;
            checks["microfix.anchor_zero_preserves_boss_progression"] = baseCap && eyeCap && finalCap;
            config.AnchorTileCapOverride = 3;
            checks["microfix.anchor_positive_override_is_exact"] = AnchoredTileSystem.GetTileCap() == 3;

            foreach (FieldInfo field in flags) field.SetValue(null, false);
            Main.hardMode = false;
            config.AnchorTileCapOverride = -17;
            bool negativeUsesProgression = AnchoredTileSystem.GetTileCap() == 5_000;
            config.AnchorTileCapOverride = int.MaxValue;
            bool excessiveIsBounded = AnchoredTileSystem.GetTileCap() == 1_000_000;
            checks["microfix.anchor_override_clamped"] = negativeUsesProgression && excessiveIsBounded;

            // A short noncontainer span cannot affect chest contents, tile entities or zones.
            Point16 start = FindFixtureSpan();
            Point16 end = new(start.X + 4, start.Y);
            Point16 extra = new(start.X + 5, start.Y);
            AnchoredTileSystem.AnchoredTiles.Clear();
            AnchoredTileSystem.AnchoredChests.Clear();
            config.AnchorTileCapOverride = 3;
            RectangleResult added = Apply(start, end, removing: false);
            checks["microfix.anchor_rectangle_honors_override"] = added.Changed == 3 && added.Skipped == 2
                && added.Cap == 3 && added.Total == 3 && AnchoredTileSystem.AnchoredTiles.Count == 3;
            RectangleResult repeated = Apply(start, end, removing: false);
            checks["microfix.anchor_full_rectangle_is_idempotent"] = repeated.Changed == 0
                && repeated.Skipped == 2 && AnchoredTileSystem.AnchoredTiles.Count == 3;
            RectangleResult removed = Apply(start, start, removing: true);
            RectangleResult refilled = Apply(extra, extra, removing: false);
            checks["microfix.anchor_removal_frees_capacity"] = removed.Changed == 1 && removed.Total == 2
                && refilled.Changed == 1 && refilled.Total == 3 && AnchoredTileSystem.AnchoredTiles.ContainsKey(extra)
                && !AnchoredTileSystem.AnchoredTiles.ContainsKey(start);

            var retained = new Dictionary<Point16, AnchoredTileData>(AnchoredTileSystem.AnchoredTiles);
            config.AnchorTileCapOverride = 1;
            RectangleResult lowered = Apply(start, start, removing: false);
            checks["microfix.anchor_lowering_retains_existing"] = lowered.Cap == 1 && lowered.Changed == 0
                && lowered.Skipped == 1 && SameAnchors(retained);
            var tag = new TagCompound();
            var system = ModContent.GetInstance<AnchoredTileSystem>();
            system.SaveWorldData(tag);
            AnchoredTileSystem.AnchoredTiles.Clear();
            system.LoadWorldData(tag);
            checks["microfix.anchor_lowering_preserves_saved_anchors"] = SameAnchors(retained);
            RectangleResult removedWhileOver = Apply(extra, extra, removing: true);
            checks["microfix.anchor_over_limit_removal_allowed"] = removedWhileOver.Changed == 1
                && removedWhileOver.Total == retained.Count - 1;

            // Prepare a bounded 5,000-entry count fixture from ONE real captured tile. The
            // synthetic entries are never restored into the world or saved to disk. Their
            // coordinates (y>=64) cannot overlap the real admission fixture (y<=31).
            AnchoredTileSystem.AnchoredTiles.Clear();
            AnchoredTileSystem.AnchoredChests.Clear();
            AnchoredTileData sample = AnchoredTileData.CaptureFromWorld(start.X, start.Y);
            for (int i = 0; i < 5_000; i++)
            {
                var pos = new Point16(64 + i % 200, 64 + i / 200);
                var entry = sample;
                entry.Position = pos;
                AnchoredTileSystem.AnchoredTiles.Add(pos, entry);
            }
            config.AnchorTileCapOverride = 0;
            RectangleResult blockedAtBase = Apply(start, start, removing: false);
            config.AnchorTileCapOverride = 6_000;
            RectangleResult admittedAboveBase = Apply(start, start, removing: false);
            checks["microfix.anchor_override_exceeds_progression_limit"] = blockedAtBase.Changed == 0
                && blockedAtBase.Skipped == 1 && blockedAtBase.Cap == 5_000
                && admittedAboveBase.Changed == 1 && admittedAboveBase.Cap == 6_000
                && admittedAboveBase.Total == 5_001 && AnchoredTileSystem.AnchoredTiles.ContainsKey(start);

            // Exercise the actual server-side config permission boundary. Replacing an
            // inactive slot briefly avoids changing any connected player's identity/state.
            playerSlot = Enumerable.Range(0, Math.Min(Main.maxPlayers, Main.player.Length))
                .FirstOrDefault(index => Main.player[index] == null || !Main.player[index].active, -1);
            if (playerSlot < 0) throw new InvalidOperationException("No inactive slot available for config-permission check.");
            previousPlayer = Main.player[playerSlot];
            Main.player[playerSlot] = new Player
            {
                whoAmI = playerSlot, active = true, name = "DWValidation Untrusted Cap Editor"
            };
            config.TrustedPlayers = new List<string> { "DWValidation Trusted Admin" };
            var pending = (DynamicWorldsConfig)config.Clone();
            pending.AnchorTileCapOverride = 900_000;
            NetworkText denied = NetworkText.FromLiteral(string.Empty);
            bool accepted = config.AcceptClientChanges(pending, playerSlot, ref denied);
            checks["microfix.anchor_untrusted_config_change_rejected"] = !accepted
                && config.AnchorTileCapOverride == 6_000 && !string.IsNullOrWhiteSpace(denied?.ToString());
            checks["microfix.anchor_checks_completed"] = true;
        }
        catch (Exception ex)
        {
            checks["microfix.anchor_checks_completed"] = false;
            ModContent.GetInstance<global::DynamicWorlds.DynamicWorlds>().Logger.Error("Anchor cap regression checks failed.", ex);
        }
        finally
        {
            config.AnchorTileCapOverride = previousOverride;
            config.TrustedPlayers = previousTrustedPlayers;
            Main.hardMode = previousHardMode;
            for (int i = 0; i < flags.Length; i++) flags[i].SetValue(null, previousFlags[i]);
            if (playerSlot >= 0) Main.player[playerSlot] = previousPlayer;
            AnchoredTileSystem.AnchoredTiles.Clear();
            foreach (var pair in previousAnchors) AnchoredTileSystem.AnchoredTiles.Add(pair.Key, pair.Value);
            AnchoredTileSystem.AnchoredChests.Clear();
            foreach (var pair in previousChests) AnchoredTileSystem.AnchoredChests.Add(pair.Key, pair.Value);
        }
        checks["microfix.anchor_fixture_state_restored"] = SameAnchors(previousAnchors)
            && AnchoredTileSystem.AnchoredChests.Count == previousChests.Count
            && previousChests.All(pair => AnchoredTileSystem.AnchoredChests.TryGetValue(pair.Key, out var value)
                && ReferenceEquals(value, pair.Value))
            && config.AnchorTileCapOverride == previousOverride && ReferenceEquals(config.TrustedPlayers, previousTrustedPlayers)
            && Main.hardMode == previousHardMode
            && flags.Select((field, index) => (bool)field.GetValue(null) == previousFlags[index]).All(value => value)
            && (playerSlot < 0 || ReferenceEquals(Main.player[playerSlot], previousPlayer));
        return checks;
    }

    private static bool SameAnchors(Dictionary<Point16, AnchoredTileData> expected) =>
        AnchoredTileSystem.AnchoredTiles.Count == expected.Count && expected.All(pair =>
            AnchoredTileSystem.AnchoredTiles.TryGetValue(pair.Key, out var actual) && actual.Equals(pair.Value));

    private static Point16 FindFixtureSpan()
    {
        for (int y = 20; y <= 31; y++)
        {
            for (int x = 320; x < Math.Min(Main.maxTilesX - 16, 640); x += 8)
            {
                bool clear = true;
                for (int dx = 0; dx < 6; dx++)
                {
                    var pos = new Point16(x + dx, y);
                    Tile tile = Framing.GetTileSafely(pos.X, pos.Y);
                    if (!WorldGen.InWorld(pos.X, pos.Y, 10) || StructureAnchorSystem.TryGetZoneAt(pos, out _)
                        || TileEntity.ByPosition.ContainsKey(pos)
                        || (tile.HasTile && (TileID.Sets.BasicChest[tile.TileType] || TileID.Sets.BasicDresser[tile.TileType])))
                    { clear = false; break; }
                }
                if (clear) return new Point16(x, y);
            }
        }
        throw new InvalidOperationException("Could not find a noncontainer span for anchor-cap checks.");
    }

    private readonly record struct RectangleResult(int Changed, int Skipped, int Cap, int Total);

    private static RectangleResult Apply(Point16 start, Point16 end, bool removing)
    {
        MethodInfo method = typeof(AnchoredTileSystem).GetMethod("ApplyRectangleWithResult", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingMethodException(typeof(AnchoredTileSystem).FullName, "ApplyRectangleWithResult");
        object result = method.Invoke(null, new object[] { start, end, removing, false })
            ?? throw new InvalidOperationException("Anchor rectangle returned no result.");
        int Read(string name) => (int)(result.GetType().GetField(name, BindingFlags.Public | BindingFlags.Instance)
            ?? throw new MissingFieldException(result.GetType().FullName, name)).GetValue(result);
        return new RectangleResult(Read("ChangedCount"), Read("SkippedCount"), Read("Cap"), Read("TotalAnchoredCount"));
    }
}
