using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;

namespace DynamicWorlds
{
    internal enum DynamicWorldsPacketType : byte
    {
        RequestFullSync,
        SyncAnchorDelta,
        SyncEraseDelta,
        SyncStructureZoneUpsert,
        SyncStructureZoneRemove,
        SyncBiomeZoneUpsert,
        SyncBiomeZoneRemove,
        RequestApplyAnchorRectangle,
        RequestApplyEraseRectangle,
        RequestCreateStructureZone,
        RequestRemoveStructureZoneAt,
        RequestRemoveStructureZoneById,
        RequestClearStructureZones,
        RequestCreateBiomeZone,
        RequestRemoveBiomeZoneAt,
        ShowMessage,
    }

    internal static class DynamicWorldsNet
    {
        private const int MaxPointChunkSize = 3500;

        internal static void RequestFullSync()
        {
            if (Main.netMode != NetmodeID.MultiplayerClient)
                return;

            ModPacket packet = CreatePacket(DynamicWorldsPacketType.RequestFullSync);
            packet.Send();
        }

        internal static void RequestAnchorRectangle(Point16 start, Point16 end, bool removing)
        {
            if (Main.netMode != NetmodeID.MultiplayerClient)
                return;

            ModPacket packet = CreatePacket(DynamicWorldsPacketType.RequestApplyAnchorRectangle);
            WritePoint16(packet, start);
            WritePoint16(packet, end);
            packet.Write(removing);
            packet.Send();
        }

        internal static void RequestEraseRectangle(Point16 start, Point16 end, bool removing)
        {
            if (Main.netMode != NetmodeID.MultiplayerClient)
                return;

            ModPacket packet = CreatePacket(DynamicWorldsPacketType.RequestApplyEraseRectangle);
            WritePoint16(packet, start);
            WritePoint16(packet, end);
            packet.Write(removing);
            packet.Send();
        }

        internal static void RequestStructureZoneCreate(Point16 topLeft, Point16 bottomRight)
        {
            if (Main.netMode != NetmodeID.MultiplayerClient)
                return;

            ModPacket packet = CreatePacket(DynamicWorldsPacketType.RequestCreateStructureZone);
            WritePoint16(packet, topLeft);
            WritePoint16(packet, bottomRight);
            packet.Send();
        }

        internal static void RequestStructureZoneRemoveAt(Point16 position)
        {
            if (Main.netMode != NetmodeID.MultiplayerClient)
                return;

            ModPacket packet = CreatePacket(DynamicWorldsPacketType.RequestRemoveStructureZoneAt);
            WritePoint16(packet, position);
            packet.Send();
        }

        internal static void RequestRemoveStructureZoneById(int zoneId)
        {
            if (Main.netMode != NetmodeID.MultiplayerClient)
                return;

            ModPacket packet = CreatePacket(DynamicWorldsPacketType.RequestRemoveStructureZoneById);
            packet.Write(zoneId);
            packet.Send();
        }

        internal static void RequestClearStructureZones()
        {
            if (Main.netMode != NetmodeID.MultiplayerClient)
                return;

            ModPacket packet = CreatePacket(DynamicWorldsPacketType.RequestClearStructureZones);
            packet.Send();
        }

        internal static void RequestBiomeZoneCreate(
            Point16 topLeft,
            Point16 bottomRight,
            Dictionary<TeleportPylonType, BiomeDowserPylonPreferences> preferences)
        {
            if (Main.netMode != NetmodeID.MultiplayerClient)
                return;

            ModPacket packet = CreatePacket(DynamicWorldsPacketType.RequestCreateBiomeZone);
            WritePoint16(packet, topLeft);
            WritePoint16(packet, bottomRight);
            packet.Write((byte)preferences.Count);
            foreach (var kv in preferences)
            {
                packet.Write((int)kv.Key);
                WriteBiomePreferences(packet, kv.Value);
            }
            packet.Send();
        }

        internal static void RequestBiomeZoneRemoveAt(Point16 position)
        {
            if (Main.netMode != NetmodeID.MultiplayerClient)
                return;

            ModPacket packet = CreatePacket(DynamicWorldsPacketType.RequestRemoveBiomeZoneAt);
            WritePoint16(packet, position);
            packet.Send();
        }

        internal static void SendFullSyncToClient(int toClient)
        {
            if (Main.netMode != NetmodeID.Server || toClient < 0)
                return;

            SendPointChunks(DynamicWorldsPacketType.SyncAnchorDelta, AnchoredTileSystem.AnchoredTiles.Keys, false, toClient);
            SendPointChunks(DynamicWorldsPacketType.SyncEraseDelta, ErasedTileSystem.ErasedTiles, false, toClient);

            foreach (BuildingZone zone in StructureAnchorSystem.Zones.Values)
                SendStructureZoneUpsert(zone, toClient);

            foreach (BiomeDowserZone zone in BiomeDowserSystem.Zones.Values)
                SendBiomeZoneUpsert(zone, toClient);
        }

        internal static void BroadcastAnchorDelta(List<Point16> changedPositions, bool removing)
        {
            if (Main.netMode != NetmodeID.Server || changedPositions == null || changedPositions.Count == 0)
                return;

            SendPointChunks(DynamicWorldsPacketType.SyncAnchorDelta, changedPositions, removing, -1);
        }

        internal static void BroadcastEraseDelta(List<Point16> changedPositions, bool removing)
        {
            if (Main.netMode != NetmodeID.Server || changedPositions == null || changedPositions.Count == 0)
                return;

            SendPointChunks(DynamicWorldsPacketType.SyncEraseDelta, changedPositions, removing, -1);
        }

        internal static void SendStructureZoneUpsert(BuildingZone zone, int toClient = -1)
        {
            if (zone == null || Main.netMode == NetmodeID.SinglePlayer)
                return;

            ModPacket packet = CreatePacket(DynamicWorldsPacketType.SyncStructureZoneUpsert);
            packet.Write(zone.Id);
            WritePoint16(packet, zone.TopLeft);
            WritePoint16(packet, zone.BottomRight);
            packet.Write(zone.SavedGroundY);
            WritePoint16(packet, zone.SavedSpawn);
            packet.Send(toClient);
        }

        internal static void SendStructureZoneRemove(int zoneId, int toClient = -1)
        {
            if (Main.netMode == NetmodeID.SinglePlayer)
                return;

            ModPacket packet = CreatePacket(DynamicWorldsPacketType.SyncStructureZoneRemove);
            packet.Write(zoneId);
            packet.Send(toClient);
        }

        internal static void SendBiomeZoneUpsert(BiomeDowserZone zone, int toClient = -1)
        {
            if (zone == null || Main.netMode == NetmodeID.SinglePlayer)
                return;

            ModPacket packet = CreatePacket(DynamicWorldsPacketType.SyncBiomeZoneUpsert);
            packet.Write(zone.Id);
            WritePoint16(packet, zone.Zone.TopLeft);
            WritePoint16(packet, zone.Zone.BottomRight);
            packet.Write(zone.Zone.SavedGroundY);
            WritePoint16(packet, zone.Zone.SavedSpawn);
            packet.Write((int)zone.PylonType);
            WritePoint16(packet, zone.PylonOffset);
            packet.Write((int)zone.PlacementMode);
            packet.Write(zone.FloatingYOffsetFromSurface);
            packet.Write(zone.UndergroundYOffsetFromSurface);
            packet.Write((int)zone.OceanPlacement);
            packet.Write(zone.PreferSkyIslandSurface);
            packet.Write(zone.PreferAetherCavern);
            packet.Send(toClient);
        }

        internal static void SendBiomeZoneRemove(int zoneId, int toClient = -1)
        {
            if (Main.netMode == NetmodeID.SinglePlayer)
                return;

            ModPacket packet = CreatePacket(DynamicWorldsPacketType.SyncBiomeZoneRemove);
            packet.Write(zoneId);
            packet.Send(toClient);
        }

        internal static void SendClientMessage(int toClient, string message, Color color)
        {
            if (Main.netMode != NetmodeID.Server || toClient < 0 || string.IsNullOrWhiteSpace(message))
                return;

            ModPacket packet = CreatePacket(DynamicWorldsPacketType.ShowMessage);
            packet.Write(message);
            packet.Write(color.R);
            packet.Write(color.G);
            packet.Write(color.B);
            packet.Send(toClient);
        }

        private static ModPacket CreatePacket(DynamicWorldsPacketType packetType)
        {
            ModPacket packet = ModContent.GetInstance<DynamicWorlds>().GetPacket();
            packet.Write((byte)packetType);
            return packet;
        }

        private static void SendPointChunks(DynamicWorldsPacketType packetType, IEnumerable<Point16> positions, bool removing, int toClient)
        {
            List<Point16> chunk = new List<Point16>(MaxPointChunkSize);
            foreach (Point16 position in positions)
            {
                chunk.Add(position);
                if (chunk.Count < MaxPointChunkSize)
                    continue;

                SendPointChunk(packetType, chunk, removing, toClient);
                chunk.Clear();
            }

            if (chunk.Count > 0)
                SendPointChunk(packetType, chunk, removing, toClient);
        }

        private static void SendPointChunk(DynamicWorldsPacketType packetType, List<Point16> positions, bool removing, int toClient)
        {
            ModPacket packet = CreatePacket(packetType);
            packet.Write(removing);
            packet.Write(positions.Count);
            foreach (Point16 position in positions)
                WritePoint16(packet, position);

            packet.Send(toClient);
        }

        private static void WritePoint16(BinaryWriter writer, Point16 point)
        {
            writer.Write(point.X);
            writer.Write(point.Y);
        }

        internal static Point16 ReadPoint16(BinaryReader reader)
        {
            return new Point16(reader.ReadInt16(), reader.ReadInt16());
        }

        private static void WriteBiomePreferences(BinaryWriter writer, BiomeDowserPylonPreferences preferences)
        {
            writer.Write((int)preferences.PlacementMode);
            writer.Write(preferences.FloatingYOffsetFromSurface);
            writer.Write(preferences.UndergroundYOffsetFromSurface);
            writer.Write((int)preferences.OceanPlacement);
            writer.Write(preferences.PreferSkyIslandSurface);
            writer.Write(preferences.PreferAetherCavern);
        }

        private static BiomeDowserPylonPreferences ReadBiomePreferences(BinaryReader reader)
        {
            return new BiomeDowserPylonPreferences
            {
                PlacementMode = (BiomeDowserPlacementMode)reader.ReadInt32(),
                FloatingYOffsetFromSurface = reader.ReadInt32(),
                UndergroundYOffsetFromSurface = reader.ReadInt32(),
                OceanPlacement = (BiomeDowserOceanPlacement)reader.ReadInt32(),
                PreferSkyIslandSurface = reader.ReadBoolean(),
                PreferAetherCavern = reader.ReadBoolean(),
            };
        }

        internal static Dictionary<TeleportPylonType, BiomeDowserPylonPreferences> ReadBiomePreferenceMap(BinaryReader reader)
        {
            int count = reader.ReadByte();
            var preferences = new Dictionary<TeleportPylonType, BiomeDowserPylonPreferences>(count);
            for (int i = 0; i < count; i++)
            {
                TeleportPylonType pylonType = (TeleportPylonType)reader.ReadInt32();
                BiomeDowserPylonPreferences prefs = ReadBiomePreferences(reader);
                prefs.PlacementMode = BiomeDowserPlacementHelper.SanitizeForPylon(pylonType, prefs.PlacementMode);
                preferences[pylonType] = prefs;
            }

            return preferences;
        }

        internal static void ApplyAnchorDeltaFromServer(BinaryReader reader)
        {
            bool removing = reader.ReadBoolean();
            int count = reader.ReadInt32();
            var changedPositions = new List<Point16>(count);
            for (int i = 0; i < count; i++)
                changedPositions.Add(ReadPoint16(reader));

            AnchoredTileSystem.ApplySyncedDelta(changedPositions, removing);
        }

        internal static void ApplyEraseDeltaFromServer(BinaryReader reader)
        {
            bool removing = reader.ReadBoolean();
            int count = reader.ReadInt32();
            var changedPositions = new List<Point16>(count);
            for (int i = 0; i < count; i++)
                changedPositions.Add(ReadPoint16(reader));

            ErasedTileSystem.ApplySyncedDelta(changedPositions, removing);
        }

        internal static void ApplyStructureZoneUpsertFromServer(BinaryReader reader)
        {
            int zoneId = reader.ReadInt32();
            Point16 topLeft = ReadPoint16(reader);
            Point16 bottomRight = ReadPoint16(reader);
            int savedGroundY = reader.ReadInt32();
            Point16 savedSpawn = ReadPoint16(reader);

            var syncedZone = new BuildingZone
            {
                Id = zoneId,
                TopLeft = topLeft,
                BottomRight = bottomRight,
                SavedGroundY = savedGroundY,
                SavedSpawn = savedSpawn,
            };

            StructureAnchorSystem.UpsertSyncedZone(syncedZone);
        }

        internal static void ApplyBiomeZoneUpsertFromServer(BinaryReader reader)
        {
            int zoneId = reader.ReadInt32();
            Point16 topLeft = ReadPoint16(reader);
            Point16 bottomRight = ReadPoint16(reader);
            int savedGroundY = reader.ReadInt32();
            Point16 savedSpawn = ReadPoint16(reader);
            TeleportPylonType pylonType = (TeleportPylonType)reader.ReadInt32();
            Point16 pylonOffset = ReadPoint16(reader);
            BiomeDowserPlacementMode placementMode = (BiomeDowserPlacementMode)reader.ReadInt32();
            int floatingOffset = reader.ReadInt32();
            int undergroundOffset = reader.ReadInt32();
            BiomeDowserOceanPlacement oceanPlacement = (BiomeDowserOceanPlacement)reader.ReadInt32();
            bool preferSkyIsland = reader.ReadBoolean();
            bool preferAether = reader.ReadBoolean();

            var syncedZone = new BiomeDowserZone
            {
                Zone = new BuildingZone
                {
                    Id = zoneId,
                    TopLeft = topLeft,
                    BottomRight = bottomRight,
                    SavedGroundY = savedGroundY,
                    SavedSpawn = savedSpawn,
                },
                PylonType = pylonType,
                PylonOffset = pylonOffset,
                PlacementMode = placementMode,
                FloatingYOffsetFromSurface = floatingOffset,
                UndergroundYOffsetFromSurface = undergroundOffset,
                OceanPlacement = oceanPlacement,
                PreferSkyIslandSurface = preferSkyIsland,
                PreferAetherCavern = preferAether,
            };

            BiomeDowserSystem.UpsertSyncedZone(syncedZone);
        }
    }

    public partial class DynamicWorlds
    {
        public override void HandlePacket(BinaryReader reader, int whoAmI)
        {
            DynamicWorldsPacketType packetType = (DynamicWorldsPacketType)reader.ReadByte();
            switch (packetType)
            {
                case DynamicWorldsPacketType.RequestFullSync:
                    if (Main.netMode == NetmodeID.Server)
                        DynamicWorldsNet.SendFullSyncToClient(whoAmI);
                    break;

                case DynamicWorldsPacketType.SyncAnchorDelta:
                    if (Main.netMode != NetmodeID.MultiplayerClient)
                        break;
                    DynamicWorldsNet.ApplyAnchorDeltaFromServer(reader);
                    break;

                case DynamicWorldsPacketType.SyncEraseDelta:
                    if (Main.netMode != NetmodeID.MultiplayerClient)
                        break;
                    DynamicWorldsNet.ApplyEraseDeltaFromServer(reader);
                    break;

                case DynamicWorldsPacketType.SyncStructureZoneUpsert:
                    if (Main.netMode != NetmodeID.MultiplayerClient)
                        break;
                    DynamicWorldsNet.ApplyStructureZoneUpsertFromServer(reader);
                    break;

                case DynamicWorldsPacketType.SyncStructureZoneRemove:
                    if (Main.netMode != NetmodeID.MultiplayerClient)
                        break;
                    StructureAnchorSystem.RemoveSyncedZone(reader.ReadInt32());
                    break;

                case DynamicWorldsPacketType.SyncBiomeZoneUpsert:
                    if (Main.netMode != NetmodeID.MultiplayerClient)
                        break;
                    DynamicWorldsNet.ApplyBiomeZoneUpsertFromServer(reader);
                    break;

                case DynamicWorldsPacketType.SyncBiomeZoneRemove:
                    if (Main.netMode != NetmodeID.MultiplayerClient)
                        break;
                    BiomeDowserSystem.RemoveSyncedZone(reader.ReadInt32());
                    break;

                case DynamicWorldsPacketType.RequestApplyAnchorRectangle:
                    if (Main.netMode == NetmodeID.Server)
                    {
                        if (!DynamicWorldsPermissions.CanEditWorldTools(whoAmI, out string deniedReason))
                        {
                            DynamicWorldsNet.SendClientMessage(whoAmI, deniedReason, new Color(255, 120, 120));
                            break;
                        }

                        Point16 start = DynamicWorldsNet.ReadPoint16(reader);
                        Point16 end = DynamicWorldsNet.ReadPoint16(reader);
                        bool removing = reader.ReadBoolean();
                        AnchoredTileSystem.AnchorRectangleResult result = AnchoredTileSystem.ApplyRectangleWithResult(start, end, removing, announce: false);
                        DynamicWorldsNet.BroadcastAnchorDelta(result.ChangedPositions, removing);

                        if (removing)
                        {
                            DynamicWorldsNet.SendClientMessage(whoAmI, $"Unanchored {result.Width}×{result.Height} region.", new Color(255, 100, 100));
                        }
                        else
                        {
                            DynamicWorldsNet.SendClientMessage(
                                whoAmI,
                                $"Anchored {result.ChangedCount} tile{(result.ChangedCount == 1 ? "" : "s")} in {result.Width}×{result.Height} region. ({result.TotalAnchoredCount}/{result.Cap} used)",
                                new Color(100, 255, 100));
                            if (result.BlockedByZones > 0)
                            {
                                DynamicWorldsNet.SendClientMessage(
                                    whoAmI,
                                    $"{result.BlockedByZones} tile{(result.BlockedByZones == 1 ? "" : "s")} skipped — structure zones already protect those spaces.",
                                    new Color(255, 140, 100));
                            }
                            if (result.SkippedCount > 0)
                            {
                                DynamicWorldsNet.SendClientMessage(
                                    whoAmI,
                                    $"{result.SkippedCount} tile{(result.SkippedCount == 1 ? "" : "s")} skipped — anchor cap reached. {AnchoredTileSystem.GetCapHint()}",
                                    new Color(255, 200, 80));
                            }
                        }
                    }
                    break;

                case DynamicWorldsPacketType.RequestApplyEraseRectangle:
                    if (Main.netMode == NetmodeID.Server)
                    {
                        if (!DynamicWorldsPermissions.CanEditWorldTools(whoAmI, out string deniedReason))
                        {
                            DynamicWorldsNet.SendClientMessage(whoAmI, deniedReason, new Color(255, 120, 120));
                            break;
                        }

                        Point16 start = DynamicWorldsNet.ReadPoint16(reader);
                        Point16 end = DynamicWorldsNet.ReadPoint16(reader);
                        bool removing = reader.ReadBoolean();
                        ErasedTileSystem.EraseRectangleResult result = ErasedTileSystem.ApplyRectangleWithResult(start, end, removing, announce: false);
                        DynamicWorldsNet.BroadcastEraseDelta(result.ChangedPositions, removing);

                        if (removing)
                        {
                            DynamicWorldsNet.SendClientMessage(whoAmI, $"Unmarked {result.Width}×{result.Height} region for erasure.", new Color(255, 150, 100));
                        }
                        else
                        {
                            DynamicWorldsNet.SendClientMessage(
                                whoAmI,
                                $"Marked {result.ChangedCount} tile{(result.ChangedCount == 1 ? "" : "s")} for erasure in {result.Width}×{result.Height} region. ({result.TotalErasedCount}/{result.Cap} used)",
                                new Color(255, 120, 60));
                            if (result.SkippedCount > 0)
                            {
                                DynamicWorldsNet.SendClientMessage(
                                    whoAmI,
                                    $"{result.SkippedCount} tile{(result.SkippedCount == 1 ? "" : "s")} skipped — erasure cap reached. Defeat more bosses to expand your limit.",
                                    new Color(255, 200, 80));
                            }
                        }
                    }
                    break;

                case DynamicWorldsPacketType.RequestCreateStructureZone:
                    if (Main.netMode == NetmodeID.Server)
                    {
                        if (!DynamicWorldsPermissions.CanEditWorldTools(whoAmI, out string deniedReason))
                        {
                            DynamicWorldsNet.SendClientMessage(whoAmI, deniedReason, new Color(255, 120, 120));
                            break;
                        }

                        Point16 topLeft = DynamicWorldsNet.ReadPoint16(reader);
                        Point16 bottomRight = DynamicWorldsNet.ReadPoint16(reader);
                        if (StructureAnchorSystem.TryCreateZone(topLeft, bottomRight, out BuildingZone zone, out string message))
                        {
                            DynamicWorldsNet.SendStructureZoneUpsert(zone);
                            DynamicWorldsNet.SendClientMessage(whoAmI, message, new Color(100, 200, 255));
                        }
                        else
                        {
                            DynamicWorldsNet.SendClientMessage(whoAmI, message, new Color(255, 120, 120));
                        }
                    }
                    break;

                case DynamicWorldsPacketType.RequestRemoveStructureZoneAt:
                    if (Main.netMode == NetmodeID.Server)
                    {
                        if (!DynamicWorldsPermissions.CanEditWorldTools(whoAmI, out string deniedReason))
                        {
                            DynamicWorldsNet.SendClientMessage(whoAmI, deniedReason, new Color(255, 120, 120));
                            break;
                        }

                        Point16 clickPos = DynamicWorldsNet.ReadPoint16(reader);
                        if (StructureAnchorSystem.RemoveZoneAt(clickPos, out int removedZoneId, out string message))
                        {
                            DynamicWorldsNet.SendStructureZoneRemove(removedZoneId);
                            DynamicWorldsNet.SendClientMessage(whoAmI, message, new Color(255, 150, 100));
                        }
                        else
                        {
                            DynamicWorldsNet.SendClientMessage(whoAmI, message, new Color(255, 200, 80));
                        }
                    }
                    break;

                case DynamicWorldsPacketType.RequestRemoveStructureZoneById:
                    if (Main.netMode == NetmodeID.Server)
                    {
                        int zoneId = reader.ReadInt32();
                        if (!DynamicWorldsPermissions.CanManageZones(whoAmI, out string deniedReason))
                        {
                            DynamicWorldsNet.SendClientMessage(whoAmI, deniedReason, new Color(255, 120, 120));
                        }
                        else if (StructureAnchorSystem.RemoveZoneById(zoneId, out string message))
                        {
                            DynamicWorldsNet.SendStructureZoneRemove(zoneId);
                            DynamicWorldsNet.SendClientMessage(whoAmI, message, new Color(255, 150, 100));
                        }
                        else
                        {
                            DynamicWorldsNet.SendClientMessage(whoAmI, message, Color.Red);
                        }
                    }
                    break;

                case DynamicWorldsPacketType.RequestClearStructureZones:
                    if (Main.netMode == NetmodeID.Server)
                    {
                        if (!DynamicWorldsPermissions.CanManageZones(whoAmI, out string deniedReason))
                        {
                            DynamicWorldsNet.SendClientMessage(whoAmI, deniedReason, new Color(255, 120, 120));
                        }
                        else
                        {
                            List<int> zoneIds = new List<int>(StructureAnchorSystem.Zones.Keys);
                            int removedCount = StructureAnchorSystem.ClearAllZones();
                            foreach (int zoneId in zoneIds)
                                DynamicWorldsNet.SendStructureZoneRemove(zoneId);

                            string message = removedCount == 0
                                ? "No zones to clear."
                                : $"Cleared all {removedCount} structure zone{(removedCount == 1 ? "" : "s")}.";
                            DynamicWorldsNet.SendClientMessage(
                                whoAmI,
                                message,
                                removedCount == 0 ? Color.Yellow : new Color(255, 150, 100));
                        }
                    }
                    break;

                case DynamicWorldsPacketType.RequestCreateBiomeZone:
                    if (Main.netMode == NetmodeID.Server)
                    {
                        if (!DynamicWorldsPermissions.CanEditWorldTools(whoAmI, out string deniedReason))
                        {
                            DynamicWorldsNet.SendClientMessage(whoAmI, deniedReason, new Color(255, 120, 120));
                            break;
                        }

                        Point16 topLeft = DynamicWorldsNet.ReadPoint16(reader);
                        Point16 bottomRight = DynamicWorldsNet.ReadPoint16(reader);
                        Dictionary<TeleportPylonType, BiomeDowserPylonPreferences> preferences = DynamicWorldsNet.ReadBiomePreferenceMap(reader);
                        if (BiomeDowserSystem.TryCreateZone(
                            topLeft,
                            bottomRight,
                            pylonType => preferences.TryGetValue(pylonType, out BiomeDowserPylonPreferences prefs)
                                ? prefs
                                : BiomeDowserPylonPreferences.DefaultFor(pylonType),
                            out BiomeDowserZone zone,
                            out string message))
                        {
                            DynamicWorldsNet.SendBiomeZoneUpsert(zone);
                            DynamicWorldsNet.SendClientMessage(whoAmI, message, new Color(255, 215, 120));
                        }
                        else
                        {
                            DynamicWorldsNet.SendClientMessage(whoAmI, message, new Color(255, 200, 80));
                        }
                    }
                    break;

                case DynamicWorldsPacketType.RequestRemoveBiomeZoneAt:
                    if (Main.netMode == NetmodeID.Server)
                    {
                        if (!DynamicWorldsPermissions.CanEditWorldTools(whoAmI, out string deniedReason))
                        {
                            DynamicWorldsNet.SendClientMessage(whoAmI, deniedReason, new Color(255, 120, 120));
                            break;
                        }

                        Point16 clickPos = DynamicWorldsNet.ReadPoint16(reader);
                        if (BiomeDowserSystem.RemoveZoneAt(clickPos, out int removedZoneId, out string message))
                        {
                            DynamicWorldsNet.SendBiomeZoneRemove(removedZoneId);
                            DynamicWorldsNet.SendClientMessage(whoAmI, message, new Color(255, 180, 120));
                        }
                        else
                        {
                            DynamicWorldsNet.SendClientMessage(whoAmI, message, new Color(255, 220, 120));
                        }
                    }
                    break;

                case DynamicWorldsPacketType.ShowMessage:
                    if (Main.netMode != NetmodeID.MultiplayerClient)
                        break;
                    string messageText = reader.ReadString();
                    Color messageColor = new Color(reader.ReadByte(), reader.ReadByte(), reader.ReadByte());
                    if (!Main.dedServ)
                        Main.NewText(messageText, messageColor);
                    break;
            }
        }
    }
}
