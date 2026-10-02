using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DynamicWorlds;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace DynamicWorldsValidation;

// Draft regression helper. Call synchronously on the harness's world update thread,
// before regeneration. This exercises the production receiver, not network transport.
internal static class PacketDirectionChecks
{
    // These are protocol fixtures, intentionally independent of the internal enum.
    private const byte AnchorSync = 1, EraseSync = 2, StructureUpsert = 3,
        StructureRemove = 4, BiomeUpsert = 5, BiomeRemove = 6, ShowMessage = 15;
    private const int ExistingStructure = 411, NewStructure = 412, ExistingBiome = 421, NewBiome = 422;
    private static readonly Point16 ExistingPoint = new(100, 100), NewPoint = new(101, 100);
    private static readonly Point16 TopLeft = new(120, 130), BottomRight = new(125, 135), Spawn = new(122, 132);

    internal static Dictionary<string, bool> ServerChecks()
    {
        RequireGuard(server: true);
        using var restore = new PreservationScope();
        var checks = new Dictionary<string, bool>();
        Mod target = Target();
        RestrictPermissions();

        Record(checks, "server_rejects_anchor_sync", () =>
            Rejected(target, Delta(AnchorSync, false, NewPoint)) &
            Rejected(target, Delta(AnchorSync, true, ExistingPoint)));
        Record(checks, "server_rejects_erase_sync", () =>
            Rejected(target, Delta(EraseSync, false, NewPoint)) &
            Rejected(target, Delta(EraseSync, true, ExistingPoint)));
        Record(checks, "server_rejects_structure_upsert", () => Rejected(target, StructurePacket(NewStructure)));
        Record(checks, "server_rejects_structure_remove", () => Rejected(target, Remove(StructureRemove, ExistingStructure)));
        Record(checks, "server_rejects_biome_upsert", () => Rejected(target, BiomePacket(NewBiome)));
        Record(checks, "server_rejects_biome_remove", () => Rejected(target, Remove(BiomeRemove, ExistingBiome)));
        Record(checks, "server_rejects_sync_before_body", () =>
        {
            bool passed = true;
            foreach (byte type in new byte[] { AnchorSync, EraseSync, StructureUpsert, StructureRemove, BiomeUpsert, BiomeRemove, ShowMessage })
                passed &= Rejected(target, new[] { type });
            return passed;
        });
        Record(checks, "server_rejects_invalid_counts_before_body", () =>
        {
            bool passed = true;
            foreach (byte type in new[] { AnchorSync, EraseSync })
                passed &= Rejected(target, Packet(type, writer => { writer.Write(false); writer.Write(-1); }));
            return passed;
        });
        Record(checks, "server_ignores_unknown_type", () => Rejected(target, new byte[] { 255, 1, 2, 3 }));
        return checks;
    }

    internal static Dictionary<string, bool> ClientChecks()
    {
        RequireGuard(server: false);
        using var restore = new PreservationScope();
        var checks = new Dictionary<string, bool>();
        Mod target = Target();
        RestrictPermissions();
        // No yield, game update or socket operation occurs while the mode is changed.
        // The scope restores the single-player mode even if any assertion throws.
        Main.netMode = NetmodeID.MultiplayerClient;

        Record(checks, "client_anchor_sync", () =>
        {
            SeedFixtures();
            byte[] baseline = Fingerprint();
            DispatchFully(target, Delta(AnchorSync, false, NewPoint));
            bool added = AnchoredTileSystem.AnchoredTiles.TryGetValue(NewPoint, out var tile) &&
                tile.Active && tile.Position.Equals(NewPoint) && AnchoredTileSystem.AnchoredTiles.Count == 2;
            DispatchFully(target, Delta(AnchorSync, true, NewPoint));
            bool roundTrip = SameState(baseline);
            DispatchFully(target, Delta(AnchorSync, true, ExistingPoint));
            return added && roundTrip && !AnchoredTileSystem.AnchoredTiles.ContainsKey(ExistingPoint) &&
                !AnchoredTileSystem.AnchoredChests.ContainsKey(ExistingPoint);
        });
        Record(checks, "client_erase_sync", () =>
        {
            SeedFixtures();
            byte[] baseline = Fingerprint();
            DispatchFully(target, Delta(EraseSync, false, NewPoint));
            bool added = ErasedTileSystem.ErasedTiles.SetEquals(new[] { ExistingPoint, NewPoint });
            DispatchFully(target, Delta(EraseSync, true, NewPoint));
            bool roundTrip = SameState(baseline);
            DispatchFully(target, Delta(EraseSync, true, ExistingPoint));
            return added && roundTrip && ErasedTileSystem.ErasedTiles.Count == 0;
        });
        Record(checks, "client_structure_upsert", () =>
        {
            SeedFixtures();
            DispatchFully(target, StructurePacket(NewStructure));
            bool inserted = StructureAnchorSystem.Zones.TryGetValue(NewStructure, out var zone) &&
                ZoneMatches(zone, NewStructure, 136) && StructureAnchorSystem.Zones.Count == 2;
            DispatchFully(target, StructurePacket(NewStructure, ground: 140));
            return inserted && ZoneMatches(StructureAnchorSystem.Zones[NewStructure], NewStructure, 140);
        });
        Record(checks, "client_structure_remove", () =>
        {
            SeedFixtures();
            DispatchFully(target, Remove(StructureRemove, ExistingStructure));
            return StructureAnchorSystem.Zones.Count == 0 && BiomeDowserSystem.Zones.ContainsKey(ExistingBiome);
        });
        Record(checks, "client_biome_upsert", () =>
        {
            SeedFixtures();
            DispatchFully(target, BiomePacket(NewBiome));
            bool inserted = BiomeDowserSystem.Zones.TryGetValue(NewBiome, out var zone) &&
                BiomeMatches(zone, NewBiome, 136) && BiomeDowserSystem.Zones.Count == 2;
            DispatchFully(target, BiomePacket(NewBiome, ground: 140));
            return inserted && BiomeMatches(BiomeDowserSystem.Zones[NewBiome], NewBiome, 140);
        });
        Record(checks, "client_biome_remove", () =>
        {
            SeedFixtures();
            DispatchFully(target, Remove(BiomeRemove, ExistingBiome));
            return BiomeDowserSystem.Zones.Count == 0 && StructureAnchorSystem.Zones.ContainsKey(ExistingStructure);
        });
        Record(checks, "client_ignores_requests_before_body", () =>
        {
            bool passed = true;
            foreach (byte type in new byte[] { 0, 7, 8, 9, 10, 11, 12, 13, 14 })
                passed &= Rejected(target, new[] { type });
            return passed;
        });
        Record(checks, "client_truncated_sync_is_atomic", () =>
        {
            bool passed = true;
            // Each accepted-direction packet is one byte short. A decoder may reject
            // silently or throw EndOfStreamException; neither may partially apply it.
            foreach (byte[] complete in new[]
            {
                Delta(AnchorSync, false, NewPoint, new Point16(102, 100)),
                Delta(EraseSync, false, NewPoint, new Point16(102, 100)),
                StructurePacket(NewStructure), Remove(StructureRemove, ExistingStructure),
                BiomePacket(NewBiome), Remove(BiomeRemove, ExistingBiome)
            })
            {
                SeedFixtures();
                byte[] baseline = Fingerprint();
                try { Dispatch(target, complete.Take(complete.Length - 1).ToArray()); }
                catch (EndOfStreamException) { }
                passed &= SameState(baseline);
            }
            return passed;
        });
        Record(checks, "singleplayer_ignores_sync_before_body", () =>
        {
            Main.netMode = NetmodeID.SinglePlayer;
            try
            {
                bool passed = true;
                foreach (byte type in new byte[] { AnchorSync, EraseSync, StructureUpsert, StructureRemove, BiomeUpsert, BiomeRemove, ShowMessage })
                    passed &= Rejected(target, new[] { type });
                return passed;
            }
            finally { Main.netMode = NetmodeID.MultiplayerClient; }
        });
        return checks;
    }

    private static void RequireGuard(bool server)
    {
        if (!ValidationGuard.TryGet(out _, out string reason, requireWorld: true, requireServer: server))
            throw new InvalidOperationException("Packet validation guard failed: " + reason);
        if (!server && (Main.dedServ || Main.netMode != NetmodeID.SinglePlayer || Main.gameMenu ||
            Environment.GetEnvironmentVariable("DW_VALIDATION_MODE") != "client"))
            throw new InvalidOperationException("Client packet checks require the loaded disposable single-player fixture.");
        if (DynamicWorldRegenSystem.IsBusy || MultiplayerRegenSystem.IsBusy)
            throw new InvalidOperationException("Packet checks cannot run during regeneration.");
    }

    private static Mod Target() => ModLoader.GetMod("DynamicWorlds") ??
        throw new InvalidOperationException("The production Dynamic Worlds mod is not loaded.");

    private static void RestrictPermissions()
    {
        var config = ModContent.GetInstance<DynamicWorldsConfig>();
        config.WorldToolEditPermission = DynamicWorldsPermissionMode.ConsoleOnly;
        config.ZoneCommandPermission = DynamicWorldsPermissionMode.ConsoleOnly;
    }

    private static void Record(Dictionary<string, bool> checks, string suffix, Func<bool> test)
    {
        string name = "microfix.packet_" + suffix;
        try { checks[name] = test(); }
        catch (Exception ex)
        {
            checks[name] = false;
            Target().Logger.Warn(name + ": " + ex);
        }
    }

    private static bool Rejected(Mod target, byte[] bytes)
    {
        SeedFixtures();
        byte[] baseline = Fingerprint();
        // Position 1 proves that the direction gate preceded every body read.
        return Dispatch(target, bytes) == 1 && SameState(baseline);
    }

    private static long Dispatch(Mod target, byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        using var reader = new BinaryReader(stream);
        target.HandlePacket(reader, whoAmI: 0);
        return stream.Position;
    }

    private static void DispatchFully(Mod target, byte[] bytes)
    {
        if (Dispatch(target, bytes) != bytes.Length)
            throw new InvalidOperationException("A legitimate synchronization packet was not fully consumed.");
    }

    private static byte[] Packet(byte type, Action<BinaryWriter> body)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(type);
        body(writer);
        writer.Flush();
        return stream.ToArray();
    }

    private static byte[] Delta(byte type, bool removing, params Point16[] points) => Packet(type, writer =>
    {
        writer.Write(removing);
        writer.Write(points.Length);
        foreach (Point16 point in points) WritePoint(writer, point);
    });

    private static byte[] Remove(byte type, int id) => Packet(type, writer => writer.Write(id));
    private static byte[] StructurePacket(int id, int ground = 136) =>
        Packet(StructureUpsert, writer => WriteZone(writer, id, ground));

    private static byte[] BiomePacket(int id, int ground = 136) => Packet(BiomeUpsert, writer =>
    {
        WriteZone(writer, id, ground);
        writer.Write((int)TeleportPylonType.Jungle);
        WritePoint(writer, new Point16(2, 1));
        writer.Write((int)BiomeDowserPlacementMode.Floating);
        writer.Write(-29);
        writer.Write(65);
        writer.Write((int)BiomeDowserOceanPlacement.Boat);
        writer.Write(true);
        writer.Write(false);
    });

    private static void WriteZone(BinaryWriter writer, int id, int ground)
    {
        writer.Write(id);
        WritePoint(writer, TopLeft);
        WritePoint(writer, BottomRight);
        writer.Write(ground);
        WritePoint(writer, Spawn);
    }

    private static void WritePoint(BinaryWriter writer, Point16 point)
    {
        writer.Write(point.X);
        writer.Write(point.Y);
    }

    private static bool ZoneMatches(BuildingZone zone, int id, int ground) => zone != null &&
        zone.Id == id && zone.TopLeft.Equals(TopLeft) && zone.BottomRight.Equals(BottomRight) &&
        zone.SavedGroundY == ground && zone.SavedSpawn.Equals(Spawn) && zone.Tiles.Count == 0 && zone.Chests.Count == 0;

    private static bool BiomeMatches(BiomeDowserZone zone, int id, int ground) => zone != null &&
        ZoneMatches(zone.Zone, id, ground) && zone.PylonType == TeleportPylonType.Jungle &&
        zone.PylonOffset.Equals(new Point16(2, 1)) && zone.PlacementMode == BiomeDowserPlacementMode.Floating &&
        zone.FloatingYOffsetFromSurface == -29 && zone.UndergroundYOffsetFromSurface == 65 &&
        zone.OceanPlacement == BiomeDowserOceanPlacement.Boat && zone.PreferSkyIslandSurface && !zone.PreferAetherCavern;

    private static void SeedFixtures()
    {
        ClearCollections();
        var tile = new AnchoredTileData
        {
            Position = ExistingPoint, Active = true, TileType = TileID.GoldBrick,
            WallType = WallID.Stone, HalfBlock = true, WireRed = true, HasActuator = true
        };
        var items = new Item[Chest.maxItems];
        items[0] = new Item();
        items[0].SetDefaults(ItemID.Torch);
        items[0].stack = 23;
        items[1] = new Item();
        items[1].SetDefaults(ItemID.IronPickaxe);
        items[1].Prefix(PrefixID.Light);
        var chest = new SavedChestContents { Position = ExistingPoint, Items = items };
        AnchoredTileSystem.AnchoredTiles.Add(ExistingPoint, tile);
        AnchoredTileSystem.AnchoredChests.Add(ExistingPoint, chest);
        ErasedTileSystem.ErasedTiles.Add(ExistingPoint);
        BuildingZone MakeZone(int id) => new()
        {
            Id = id, TopLeft = TopLeft, BottomRight = BottomRight, SavedGroundY = 136, SavedSpawn = Spawn,
            Tiles = new Dictionary<Point16, AnchoredTileData> { [ExistingPoint] = tile },
            Chests = new Dictionary<Point16, SavedChestContents> { [ExistingPoint] = chest }
        };
        StructureAnchorSystem.Zones.Add(ExistingStructure, MakeZone(ExistingStructure));
        BiomeDowserSystem.Zones.Add(ExistingBiome, new BiomeDowserZone
        {
            Zone = MakeZone(ExistingBiome), PylonType = TeleportPylonType.SurfacePurity,
            PylonOffset = new Point16(1, 1), PreferSkyIslandSurface = true
        });
    }

    private static byte[] Fingerprint()
    {
        // Compare full serialized snapshots and chest items, not only counts or IDs.
        var state = new TagCompound
        {
            ["anchors"] = AnchoredTileSystem.AnchoredTiles.OrderBy(pair => pair.Key.X).ThenBy(pair => pair.Key.Y)
                .Select(pair => pair.Value.ToTag()).ToList(),
            ["chests"] = AnchoredTileSystem.AnchoredChests.OrderBy(pair => pair.Key.X).ThenBy(pair => pair.Key.Y)
                .Select(pair => pair.Value.ToTag()).ToList(),
            ["erased"] = ErasedTileSystem.ErasedTiles.OrderBy(point => point.X).ThenBy(point => point.Y)
                .Select(point => new TagCompound { ["x"] = point.X, ["y"] = point.Y }).ToList(),
            ["structures"] = StructureAnchorSystem.Zones.OrderBy(pair => pair.Key).Select(pair => pair.Value.ToTag()).ToList(),
            ["biomes"] = BiomeDowserSystem.Zones.OrderBy(pair => pair.Key).Select(pair => pair.Value.ToTag()).ToList()
        };
        using var stream = new MemoryStream();
        TagIO.ToStream(state, stream, compress: false);
        return stream.ToArray();
    }

    private static bool SameState(byte[] expected) => expected.SequenceEqual(Fingerprint());

    private static void ClearCollections()
    {
        AnchoredTileSystem.AnchoredTiles.Clear();
        AnchoredTileSystem.AnchoredChests.Clear();
        ErasedTileSystem.ErasedTiles.Clear();
        StructureAnchorSystem.Zones.Clear();
        BiomeDowserSystem.Zones.Clear();
    }

    private sealed class PreservationScope : IDisposable
    {
        // Originals are removed before dispatch, so handlers can only modify synthetic
        // objects. Restoring the original references also preserves nested snapshots.
        private readonly Dictionary<Point16, AnchoredTileData> _anchors = new(AnchoredTileSystem.AnchoredTiles);
        private readonly Dictionary<Point16, SavedChestContents> _chests = new(AnchoredTileSystem.AnchoredChests);
        private readonly HashSet<Point16> _erased = new(ErasedTileSystem.ErasedTiles);
        private readonly Dictionary<int, BuildingZone> _structures = new(StructureAnchorSystem.Zones);
        private readonly Dictionary<int, BiomeDowserZone> _biomes = new(BiomeDowserSystem.Zones);
        private readonly int _netMode = Main.netMode;
        private readonly DynamicWorldsConfig _config = ModContent.GetInstance<DynamicWorldsConfig>();
        private readonly DynamicWorldsPermissionMode _toolPermission = ModContent.GetInstance<DynamicWorldsConfig>().WorldToolEditPermission;
        private readonly DynamicWorldsPermissionMode _zonePermission = ModContent.GetInstance<DynamicWorldsConfig>().ZoneCommandPermission;

        public void Dispose()
        {
            try
            {
                ClearCollections();
                foreach (var pair in _anchors) AnchoredTileSystem.AnchoredTiles.Add(pair.Key, pair.Value);
                foreach (var pair in _chests) AnchoredTileSystem.AnchoredChests.Add(pair.Key, pair.Value);
                foreach (Point16 point in _erased) ErasedTileSystem.ErasedTiles.Add(point);
                foreach (var pair in _structures) StructureAnchorSystem.Zones.Add(pair.Key, pair.Value);
                foreach (var pair in _biomes) BiomeDowserSystem.Zones.Add(pair.Key, pair.Value);
            }
            finally
            {
                _config.WorldToolEditPermission = _toolPermission;
                _config.ZoneCommandPermission = _zonePermission;
                Main.netMode = _netMode;
            }
        }
    }
}
