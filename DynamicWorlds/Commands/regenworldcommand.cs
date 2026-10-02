using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.WorldBuilding;

namespace DynamicWorlds
{
    internal sealed class RegenExecutionResult
    {
        public bool HadSavedSpawn;
        public bool UsePersonalSpawn;
        public int SpawnTileX;
        public int SpawnTileY;
        public int RespawnedNpcCount;
        public int RestoredHousingCount;
    }

    internal readonly struct NpcRestoreSummary
    {
        public readonly int RespawnedCount;
        public readonly int RestoredHousingCount;

        public NpcRestoreSummary(int respawnedCount, int restoredHousingCount)
        {
            RespawnedCount = respawnedCount;
            RestoredHousingCount = restoredHousingCount;
        }
    }

    public static class SingleplayerRegenHelper
    {
        public static void RegenerateWorldWithProgress(
            string seedOverride = null,
            int cycleIndex = 1,
            int cycleCount = 1,
            string snapshotFolderPath = null)
        {
            if (DynamicWorldRegenSystem.IsBusy || MultiplayerRegenSystem.IsBusy)
            {
                Main.NewText("World regeneration is already in progress.", 255, 200, 50);
                return;
            }

            if (Main.netMode != NetmodeID.SinglePlayer)
            {
                Main.NewText("This command only works in single player.", 255, 80, 80);
                return;
            }

            if (Main.npc.Any(n => n.active && n.boss))
            {
                Main.NewText("You cannot regenerate the world while a boss is alive.", 255, 80, 80);
                return;
            }

            cycleCount = Math.Max(1, cycleCount);
            cycleIndex = Utils.Clamp(cycleIndex, 1, cycleCount);

            PendingRegenContext pending = CreatePendingRegenContext(seedOverride, cycleIndex, cycleCount, snapshotFolderPath);
            if (pending == null)
                return;

            if (cycleCount > 1)
            {
                if (cycleIndex == 1)
                {
                    Main.NewText(
                        $"Saving current world and opening regen cycle {cycleIndex}/{cycleCount}...",
                        180,
                        220,
                        255);
                    Main.NewText(
                        $"Queued {cycleCount} full regeneration cycle{(cycleCount == 1 ? "" : "s")} in a row.",
                        150,
                        210,
                        255);
                }
                else
                {
                    Main.NewText($"Starting regen cycle {cycleIndex}/{cycleCount}...", 180, 220, 255);
                }

                if (cycleIndex == 1 && !string.IsNullOrWhiteSpace(pending.SnapshotFolderPath))
                {
                    Main.NewText(
                        $"Cycle screenshots will be saved in {Path.GetFileName(pending.SnapshotFolderPath)}.",
                        180,
                        255,
                        180);
                }
            }
            else
            {
                Main.NewText("Saving current world and opening the regeneration screen...", 180, 220, 255);
            }

            DynamicWorldRegenSystem.QueueRegen(pending);
        }

        internal static PendingRegenContext CreatePendingRegenContext(
            string seedOverride,
            int cycleIndex = 1,
            int cycleCount = 1,
            string snapshotFolderPath = null)
        {
            var mod = ModContent.GetInstance<DynamicWorlds>();
            mod.Logger.Info("=== WORLD REGENERATION STARTED ===");
            mod.Logger.Info("Capturing world state for menu-based regeneration...");

            WorldProgressSnapshot before = WorldProgressUtil.Capture();
            if (Main.netMode == NetmodeID.SinglePlayer)
                WorldProgressUtil.PrintSnapshotToChat("Before regen", before);

            AnchoredTileSystem.RefreshAllChestSnapshots();
            StructureAnchorSystem.RefreshAllChestSnapshots();
            BiomeDowserSystem.RefreshAllChestSnapshots();

            Player player = Main.netMode == NetmodeID.SinglePlayer ? Main.LocalPlayer : null;
            int newSeed = ResolveNewSeed(seedOverride, out string seedLabel);
            string resolvedSnapshotFolderPath = ResolveOrCreateMultiRegenSnapshotFolder(snapshotFolderPath, cycleIndex, cycleCount);

            mod.Logger.Info(
                $"Queued menu regen cycle {cycleIndex}/{cycleCount} with seed {newSeed} and {AnchoredTileSystem.AnchoredTiles.Count} anchored tiles.");
            if (!string.IsNullOrWhiteSpace(resolvedSnapshotFolderPath))
                mod.Logger.Info($"Multiregen cycle screenshots will be saved to: {resolvedSnapshotFolderPath}");

            return new PendingRegenContext
            {
                Snapshot = CloneSnapshot(before),
                NewSeed = newSeed,
                SeedLabel = seedLabel,
                SavedSpawnX = player?.SpawnX ?? -1,
                SavedSpawnY = player?.SpawnY ?? -1,
                AnchoredTiles = new Dictionary<Point16, AnchoredTileData>(AnchoredTileSystem.AnchoredTiles),
                AnchoredChests = CloneAnchoredChests(),
                ErasedTiles = new HashSet<Point16>(ErasedTileSystem.ErasedTiles),
                BuildingZones = CloneBuildingZones(),
                BiomeDowserZones = CloneBiomeDowserZones(),
                CycleIndex = cycleIndex,
                CycleCount = cycleCount,
                SeedOverride = seedOverride,
                SnapshotFolderPath = resolvedSnapshotFolderPath,
                SourceWorldPath = Main.ActiveWorldFileData?.Path,
            };
        }

        internal static RegenExecutionResult ExecutePendingRegen(PendingRegenContext pending)
        {
            if (pending == null)
                return new RegenExecutionResult();

            var mod = ModContent.GetInstance<DynamicWorlds>();
            mod.Logger.Info("Applying preserved state to regenerated world...");

            CopyPendingDataToLiveSystems(pending);

            GenerationProgress progress = pending.Progress;
            if (progress != null)
            {
                progress.TotalWeight += 7d;
                WorldGenerator.CurrentGenerationProgress = progress;
            }

            RunProgressStep(progress, "Restoring world progression...", 1d, () =>
            {
                WorldProgressUtil.Apply(
                    pending.Snapshot,
                    ModContent.GetInstance<DynamicWorldsConfig>().PreserveEvilType);
            });

            RunProgressStep(progress, "Applying world settings...", 0.5d, () =>
            {
                ApplyConfigurableWorldSettings(pending.Snapshot, announce: false);
            });

            RunProgressStep(progress, "Clearing erased tiles...", 0.75d, () =>
            {
                ErasedTileSystem.ClearAllErasedTiles(announce: false);
            });

            RunProgressStep(progress, "Restoring structure zones...", 1.25d, () =>
            {
                StructureAnchorSystem.RestoreAllZones(announce: false);
            });

            RunProgressStep(progress, "Relocating biome dowser zones...", 1.25d, () =>
            {
                BiomeDowserSystem.RestoreAllZones(announce: false);
            });

            RunProgressStep(progress, "Restoring anchored tiles...", 1.25d, () =>
            {
                AnchoredTileSystem.RestoreAllAnchoredTiles(announce: false);
            });

            RegenExecutionResult result = DeterminePlayerPlacement(pending);

            RunProgressStep(progress, "Restoring town NPCs and housing...", 0.75d, () =>
            {
                NpcRestoreSummary summary = RespawnTownNPCsAtOriginalPositions(pending.Snapshot, announce: false);
                result.RespawnedNpcCount = summary.RespawnedCount;
                result.RestoredHousingCount = summary.RestoredHousingCount;
            });

            RunProgressStep(progress, "Finalizing regenerated world...", 0.25d, () =>
            {
                WorldProgressUtil.SaveToFile();
            });

            WorldRegenScheduler.NotifyRegenCompleted();
            mod.Logger.Info("Preserved world state applied successfully.");
            return result;
        }

        private static WorldProgressSnapshot CloneSnapshot(WorldProgressSnapshot source)
        {
            if (source == null)
                return null;

            return new WorldProgressSnapshot
            {
                worldName = source.worldName,
                worldId = source.worldId,
                worldSeed = source.worldSeed,
                hardMode = source.hardMode,
                crimson = source.crimson,
                gameMode = source.gameMode,
                downedBoss1 = source.downedBoss1,
                downedBoss2 = source.downedBoss2,
                downedBoss3 = source.downedBoss3,
                downedQueenBee = source.downedQueenBee,
                downedSlimeKing = source.downedSlimeKing,
                downedDeerclops = source.downedDeerclops,
                downedMech1 = source.downedMech1,
                downedMech2 = source.downedMech2,
                downedMech3 = source.downedMech3,
                downedPlantera = source.downedPlantera,
                downedGolem = source.downedGolem,
                downedFishron = source.downedFishron,
                downedQueenSlime = source.downedQueenSlime,
                downedEmpressOfLight = source.downedEmpressOfLight,
                downedAncientCultist = source.downedAncientCultist,
                downedTowerSolar = source.downedTowerSolar,
                downedTowerVortex = source.downedTowerVortex,
                downedTowerNebula = source.downedTowerNebula,
                downedTowerStardust = source.downedTowerStardust,
                downedMoonLord = source.downedMoonLord,
                downedGoblins = source.downedGoblins,
                downedFrostLegion = source.downedFrostLegion,
                downedPirates = source.downedPirates,
                downedMartians = source.downedMartians,
                downedPumpkinMoonKing = source.downedPumpkinMoonKing,
                downedPumpkinMoonTree = source.downedPumpkinMoonTree,
                downedFrostMoonIceQueen = source.downedFrostMoonIceQueen,
                downedFrostMoonSantank = source.downedFrostMoonSantank,
                downedFrostMoonTree = source.downedFrostMoonTree,
                downedClown = source.downedClown,
                copperTier = source.copperTier,
                ironTier = source.ironTier,
                silverTier = source.silverTier,
                goldTier = source.goldTier,
                cobaltTier = source.cobaltTier,
                mythrilTier = source.mythrilTier,
                adamantiteTier = source.adamantiteTier,
                combatBookWasUsed = source.combatBookWasUsed,
                combatBookVolumeTwoWasUsed = source.combatBookVolumeTwoWasUsed,
                shadowOrbSmashed = source.shadowOrbSmashed,
                shadowOrbCount = source.shadowOrbCount,
                spawnMeteor = source.spawnMeteor,
                altarCount = source.altarCount,
                meteoriteTileCount = source.meteoriteTileCount,
                lunarApocalypseIsUp = source.lunarApocalypseIsUp,
                towerActiveSolar = source.towerActiveSolar,
                towerActiveVortex = source.towerActiveVortex,
                towerActiveNebula = source.towerActiveNebula,
                towerActiveStardust = source.towerActiveStardust,
                shieldStrengthTowerSolar = source.shieldStrengthTowerSolar,
                shieldStrengthTowerVortex = source.shieldStrengthTowerVortex,
                shieldStrengthTowerNebula = source.shieldStrengthTowerNebula,
                shieldStrengthTowerStardust = source.shieldStrengthTowerStardust,
                moonLordCountdown = source.moonLordCountdown,
                maxMoonLordCountdown = source.maxMoonLordCountdown,
                collectedNpcTypes = source.collectedNpcTypes != null ? new List<int>(source.collectedNpcTypes) : new List<int>(),
                npcPositions = source.npcPositions != null
                    ? new List<(int type, float x, float y, string displayName, string species)>(source.npcPositions)
                    : new List<(int type, float x, float y, string displayName, string species)>(),
                npcHousing = source.npcHousing != null
                    ? new List<(int type, int homeX, int homeY)>(source.npcHousing)
                    : new List<(int type, int homeX, int homeY)>(),
                calamity = source.calamity?.Clone(),
            };
        }

        private static Dictionary<Point16, SavedChestContents> CloneAnchoredChests()
        {
            var clone = new Dictionary<Point16, SavedChestContents>();
            foreach (var kv in AnchoredTileSystem.AnchoredChests)
                clone[kv.Key] = CloneChestContents(kv.Value);

            return clone;
        }

        private static SavedChestContents CloneChestContents(SavedChestContents source)
        {
            var clone = new SavedChestContents
            {
                Position = source.Position,
                Items = new Item[Chest.maxItems]
            };

            for (int i = 0; i < Chest.maxItems; i++)
                clone.Items[i] = source.Items != null && i < source.Items.Length && source.Items[i] != null
                    ? source.Items[i].Clone()
                    : new Item();

            return clone;
        }

        private static Dictionary<int, BuildingZone> CloneBuildingZones()
        {
            var clone = new Dictionary<int, BuildingZone>();
            foreach (var kv in StructureAnchorSystem.Zones)
                clone[kv.Key] = BuildingZone.FromTag(kv.Value.ToTag());

            return clone;
        }

        private static Dictionary<int, BiomeDowserZone> CloneBiomeDowserZones()
        {
            var clone = new Dictionary<int, BiomeDowserZone>();
            foreach (var kv in BiomeDowserSystem.Zones)
                clone[kv.Key] = BiomeDowserZone.FromTag(kv.Value.ToTag());

            return clone;
        }

        private static int ResolveNewSeed(string seedOverride, out string seedLabel)
        {
            var config = ModContent.GetInstance<DynamicWorldsConfig>();
            bool announceLocally = Main.netMode == NetmodeID.SinglePlayer;

            if (!string.IsNullOrWhiteSpace(seedOverride))
            {
                int newSeed = int.TryParse(seedOverride, out int parsedSeed)
                    ? parsedSeed & 0x7FFFFFFF
                    : Math.Abs(seedOverride.GetHashCode()) & 0x7FFFFFFF;

                seedLabel = seedOverride;
                if (announceLocally)
                    Main.NewText($"Using seed: {seedOverride} -> {newSeed}", 180, 180, 255);
                return newSeed;
            }

            if (!config.RandomizeSeedEachRegen)
            {
                string currentSeedText = Main.ActiveWorldFileData?.SeedText ?? string.Empty;
                int newSeed = int.TryParse(currentSeedText, out int parsedSeed)
                    ? parsedSeed & 0x7FFFFFFF
                    : Math.Abs(currentSeedText.GetHashCode()) & 0x7FFFFFFF;

                seedLabel = string.IsNullOrWhiteSpace(currentSeedText) ? newSeed.ToString() : currentSeedText;
                if (announceLocally)
                    Main.NewText($"Reusing world seed for consistent layout: {seedLabel}", 180, 180, 255);
                return newSeed;
            }

            int randomSeed = (int)(DateTime.Now.Ticks & 0x7FFFFFFF);
            seedLabel = randomSeed.ToString();
            if (announceLocally)
                Main.NewText($"Using random regen seed: {randomSeed}", 180, 180, 255);
            return randomSeed;
        }

        private static string ResolveOrCreateMultiRegenSnapshotFolder(string snapshotFolderPath, int cycleIndex, int cycleCount)
        {
            if (cycleCount <= 1)
                return null;

            if (!string.IsNullOrWhiteSpace(snapshotFolderPath))
            {
                Directory.CreateDirectory(snapshotFolderPath);
                return snapshotFolderPath;
            }

            if (cycleIndex != 1)
                return null;

            try
            {
                string baseFolder = Path.Combine(Main.SavePath, "DynamicWorlds", "MultiregenSnapshots");
                Directory.CreateDirectory(baseFolder);

                string worldName = SanitizeFileNamePart(string.IsNullOrWhiteSpace(Main.worldName) ? "World" : Main.worldName);
                string folderName = $"{worldName}_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}";
                string folderPath = Path.Combine(baseFolder, folderName);
                Directory.CreateDirectory(folderPath);
                return folderPath;
            }
            catch (Exception ex)
            {
                ModContent.GetInstance<DynamicWorlds>().Logger.Warn("[Regen] Failed to create multiregen screenshot folder.", ex);
                Main.NewText("Could not create the multiregen screenshot folder. Repeating regen will continue without snapshots.", 255, 200, 100);
                return null;
            }
        }

        private static string SanitizeFileNamePart(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "World";

            char[] chars = value.Trim().ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                if (Array.IndexOf(Path.GetInvalidFileNameChars(), chars[i]) >= 0 || char.IsWhiteSpace(chars[i]))
                    chars[i] = '_';
            }

            string sanitized = new string(chars).Trim('_');
            if (string.IsNullOrWhiteSpace(sanitized))
                sanitized = "World";

            return sanitized.Length > 48 ? sanitized.Substring(0, 48) : sanitized;
        }

        private static void CopyPendingDataToLiveSystems(PendingRegenContext pending)
        {
            AnchoredTileSystem.AnchoredTiles.Clear();
            foreach (var kv in pending.AnchoredTiles)
                AnchoredTileSystem.AnchoredTiles[kv.Key] = kv.Value;

            AnchoredTileSystem.AnchoredChests.Clear();
            foreach (var kv in pending.AnchoredChests)
                AnchoredTileSystem.AnchoredChests[kv.Key] = CloneChestContents(kv.Value);

            ErasedTileSystem.ErasedTiles.Clear();
            foreach (Point16 pos in pending.ErasedTiles)
                ErasedTileSystem.ErasedTiles.Add(pos);

            StructureAnchorSystem.Zones.Clear();
            foreach (var kv in pending.BuildingZones)
                StructureAnchorSystem.Zones[kv.Key] = kv.Value;
            StructureAnchorSystem.RecalculateNextId();

            BiomeDowserSystem.Zones.Clear();
            foreach (var kv in pending.BiomeDowserZones)
                BiomeDowserSystem.Zones[kv.Key] = kv.Value;
            BiomeDowserSystem.RecalculateNextId();
        }

        private static RegenExecutionResult DeterminePlayerPlacement(PendingRegenContext pending)
        {
            return DeterminePlacementForSavedSpawn(pending.SavedSpawnX, pending.SavedSpawnY);
        }

        internal static RegenExecutionResult DeterminePlacementForSavedSpawn(int savedSpawnX, int savedSpawnY)
        {
            var result = new RegenExecutionResult
            {
                HadSavedSpawn = savedSpawnX >= 0 && savedSpawnY >= 0,
                SpawnTileX = Main.spawnTileX,
                SpawnTileY = Main.spawnTileY
            };

            if (!result.HadSavedSpawn)
                return result;

            Point16 savedSpawn = new Point16(savedSpawnX, savedSpawnY);
            Point16 effectiveSpawn = savedSpawn;

            if (StructureAnchorSystem.TryTranslateSavedPoint(savedSpawn, out Point16 translatedSpawn))
                effectiveSpawn = translatedSpawn;
            else if (BiomeDowserSystem.TryTranslateSavedPoint(savedSpawn, out Point16 translatedDowserSpawn))
                effectiveSpawn = translatedDowserSpawn;

            if (effectiveSpawn.X >= 0 && effectiveSpawn.Y >= 0 && Player.CheckSpawn(effectiveSpawn.X, effectiveSpawn.Y))
            {
                result.UsePersonalSpawn = true;
                result.SpawnTileX = effectiveSpawn.X;
                result.SpawnTileY = effectiveSpawn.Y;
            }

            return result;
        }

        /// <summary>
        /// Respawn town NPCs and restore preserved housing assignments.
        /// </summary>
        private static NpcRestoreSummary RespawnTownNPCsAtOriginalPositions(WorldProgressSnapshot before, bool announce)
        {
            if (before?.collectedNpcTypes == null || before.collectedNpcTypes.Count == 0)
                return new NpcRestoreSummary(0, 0);

            int respawnedCount = 0;
            int restoredHousingCount = 0;

            foreach (int npcType in before.collectedNpcTypes)
            {
                int npcSlot = FindExistingNpcSlot(npcType);
                bool createdNpc = false;

                if (npcSlot == -1)
                {
                    npcSlot = FindInactiveNpcSlot();
                    createdNpc = npcSlot != -1;
                }

                if (npcSlot == -1)
                {
                    if (announce)
                        Main.NewText("Could not respawn NPC: no free NPC slots.", 255, 150, 100);

                    continue;
                }

                Vector2 spawnPos = new Vector2(Main.spawnTileX * 16, Main.spawnTileY * 16 - 48);
                string displayName = "";
                bool restoredHousing = TryResolvePreservedHousing(before, npcType, out Point16 resolvedHome);

                if (createdNpc)
                    Main.npc[npcSlot] = new NPC();

                Main.npc[npcSlot].SetDefaults(npcType);
                Main.npc[npcSlot].whoAmI = npcSlot;

                if (before.npcPositions != null)
                {
                    var posData = before.npcPositions.FirstOrDefault(p => p.type == npcType);
                    if (posData != default)
                    {
                        int targetCenterTileX = (int)Math.Floor((posData.x + (Main.npc[npcSlot].width * 0.5f)) / 16f);
                        int targetGroundTileY = (int)Math.Floor((posData.y + Main.npc[npcSlot].height) / 16f);
                        spawnPos = FindSafeNPCSpawnLocation(
                            Main.npc[npcSlot],
                            targetCenterTileX,
                            targetGroundTileY,
                            requireWallBackdrop: false);
                        displayName = posData.displayName;
                    }
                }

                if (restoredHousing)
                    spawnPos = FindSafeNPCSpawnLocation(
                        Main.npc[npcSlot],
                        resolvedHome.X,
                        resolvedHome.Y,
                        requireWallBackdrop: true);

                Main.npc[npcSlot].position = spawnPos;
                Main.npc[npcSlot].active = true;

                if (!string.IsNullOrEmpty(displayName))
                    Main.npc[npcSlot].GivenName = displayName;

                if (restoredHousing)
                {
                    Main.npc[npcSlot].homeTileX = resolvedHome.X;
                    Main.npc[npcSlot].homeTileY = resolvedHome.Y;
                    Main.npc[npcSlot].homeless = false;
                    WorldGen.TownManager.KickOut(Main.npc[npcSlot].type);
                    WorldGen.TownManager.SetRoom(Main.npc[npcSlot].type, resolvedHome.X, resolvedHome.Y);
                    restoredHousingCount++;
                }
                else
                {
                    Main.npc[npcSlot].homeTileX = -1;
                    Main.npc[npcSlot].homeTileY = -1;
                    Main.npc[npcSlot].homeless = true;
                    WorldGen.TownManager.KickOut(Main.npc[npcSlot].type);
                }

                Main.npc[npcSlot].netUpdate = true;
                respawnedCount++;
            }

            if (announce && respawnedCount > 0)
            {
                Main.NewText($"Respawned {respawnedCount} town NPC{(respawnedCount == 1 ? "" : "s")}.", 150, 200, 255);
                if (restoredHousingCount > 0)
                    Main.NewText($"Reassigned {restoredHousingCount} preserved home{(restoredHousingCount == 1 ? "" : "s")}.", 180, 255, 180);
            }

            return new NpcRestoreSummary(respawnedCount, restoredHousingCount);
        }

        private static int FindExistingNpcSlot(int npcType)
        {
            for (int i = 0; i < Main.npc.Length; i++)
            {
                if (Main.npc[i].active && Main.npc[i].type == npcType && Main.npc[i].townNPC)
                    return i;
            }

            return -1;
        }

        private static int FindInactiveNpcSlot()
        {
            for (int i = 0; i < Main.npc.Length; i++)
            {
                if (!Main.npc[i].active)
                    return i;
            }

            return -1;
        }

        private static bool TryResolvePreservedHousing(WorldProgressSnapshot before, int npcType, out Point16 resolvedHome)
        {
            resolvedHome = default;

            if (before?.npcHousing == null || before.npcHousing.Count == 0)
                return false;

            var housingData = before.npcHousing.FirstOrDefault(h => h.type == npcType);
            if (housingData == default || housingData.homeX < 0 || housingData.homeY < 0)
                return false;

            var candidateHomes = new HashSet<Point16>();
            Point16 savedHome = new Point16(housingData.homeX, housingData.homeY);

            if (StructureAnchorSystem.TryTranslateSavedPoint(savedHome, out Point16 translatedZoneHome))
                candidateHomes.Add(translatedZoneHome);

            if (BiomeDowserSystem.TryTranslateSavedPoint(savedHome, out Point16 translatedDowserHome))
                candidateHomes.Add(translatedDowserHome);

            if (IsAnchoredHousingCandidate(savedHome))
                candidateHomes.Add(savedHome);

            foreach (Point16 candidate in candidateHomes)
            {
                if (TryValidateHousingCandidate(npcType, candidate, out resolvedHome))
                    return true;
            }

            return false;
        }

        private static bool IsAnchoredHousingCandidate(Point16 savedHome)
        {
            for (int dx = -8; dx <= 8; dx++)
            {
                for (int dy = -6; dy <= 2; dy++)
                {
                    var check = new Point16(savedHome.X + dx, savedHome.Y + dy);
                    if (AnchoredTileSystem.AnchoredTiles.ContainsKey(check))
                        return true;
                }
            }

            return false;
        }

        private static bool TryValidateHousingCandidate(int npcType, Point16 candidateHome, out Point16 resolvedHome)
        {
            resolvedHome = default;

            int roomCheckX = candidateHome.X;
            int roomCheckY = candidateHome.Y - 1;

            if (!WorldGen.InWorld(roomCheckX, roomCheckY, 10))
                return false;

            if (!WorldGen.StartRoomCheck(roomCheckX, roomCheckY))
                return false;

            if (!WorldGen.RoomNeeds(npcType))
                return false;

            WorldGen.ScoreRoom(-1, npcType);
            if (WorldGen.hiScore <= 0)
                return false;

            resolvedHome = new Point16((short)WorldGen.bestX, (short)WorldGen.bestY);
            return true;
        }

        /// <summary>
        /// Find a safe spawn location for an NPC near the target home/target tile.
        /// The returned vector is the NPC's top-left world position.
        /// </summary>
        private static Vector2 FindSafeNPCSpawnLocation(NPC npc, int targetTileX, int targetTileY, bool requireWallBackdrop)
        {
            if (TryFindStandingSpotNear(npc.width, npc.height, targetTileX, targetTileY, 4, 3, requireWallBackdrop, out Vector2 closeSpot))
                return closeSpot;

            if (TryFindStandingSpotNear(npc.width, npc.height, targetTileX, targetTileY, 8, 5, requireWallBackdrop, out Vector2 mediumSpot))
                return mediumSpot;

            if (TryFindStandingSpotNear(npc.width, npc.height, targetTileX, targetTileY, 14, 8, requireWallBackdrop, out Vector2 farSpot))
                return farSpot;

            if (requireWallBackdrop)
            {
                if (TryFindStandingSpotNear(npc.width, npc.height, targetTileX, targetTileY, 20, 10, requireWallBackdrop: false, out Vector2 emergencyIndoorFallback))
                    return emergencyIndoorFallback;
            }

            return new Vector2(Main.spawnTileX * 16f, Main.spawnTileY * 16f - npc.height);
        }

        private static bool TryFindStandingSpotNear(
            int npcWidthPixels,
            int npcHeightPixels,
            int targetTileX,
            int targetTileY,
            int horizontalRadius,
            int verticalRadius,
            bool requireWallBackdrop,
            out Vector2 worldPosition)
        {
            for (int radius = 0; radius <= Math.Max(horizontalRadius, verticalRadius); radius++)
            {
                int minX = Math.Max(-radius, -horizontalRadius);
                int maxX = Math.Min(radius, horizontalRadius);
                int minY = Math.Max(-radius, -verticalRadius);
                int maxY = Math.Min(radius, verticalRadius);

                for (int dy = minY; dy <= maxY; dy++)
                {
                    for (int dx = minX; dx <= maxX; dx++)
                    {
                        if (Math.Abs(dx) != radius && Math.Abs(dy) != radius)
                            continue;

                        int groundTileX = targetTileX + dx;
                        int groundTileY = targetTileY + dy;
                        if (!TryGetStandingWorldPosition(
                                groundTileX,
                                groundTileY,
                                npcWidthPixels,
                                npcHeightPixels,
                                requireWallBackdrop,
                                out worldPosition))
                            continue;

                        return true;
                    }
                }
            }

            worldPosition = Vector2.Zero;
            return false;
        }

        private static bool TryGetStandingWorldPosition(
            int groundTileX,
            int groundTileY,
            int npcWidthPixels,
            int npcHeightPixels,
            bool requireWallBackdrop,
            out Vector2 worldPosition)
        {
            float centerWorldX = groundTileX * 16f + 8f;
            float worldX = centerWorldX - (npcWidthPixels * 0.5f);
            float worldY = groundTileY * 16f - npcHeightPixels;

            int leftX = (int)Math.Floor(worldX / 16f);
            int rightX = (int)Math.Floor((worldX + npcWidthPixels - 1) / 16f);
            int topY = (int)Math.Floor(worldY / 16f);

            if (!WorldGen.InWorld(leftX, groundTileY, 5) || !WorldGen.InWorld(rightX, groundTileY, 5))
            {
                worldPosition = Vector2.Zero;
                return false;
            }

            for (int x = leftX; x <= rightX; x++)
            {
                Tile floorTile = Framing.GetTileSafely(x, groundTileY);
                bool floorOkay = floorTile.HasTile && (Main.tileSolid[floorTile.TileType] || TileID.Sets.Platforms[floorTile.TileType]);
                if (!floorOkay)
                {
                    worldPosition = Vector2.Zero;
                    return false;
                }
            }

            for (int x = leftX; x <= rightX; x++)
            {
                for (int y = topY; y < groundTileY; y++)
                {
                    if (!WorldGen.InWorld(x, y, 5))
                    {
                        worldPosition = Vector2.Zero;
                        return false;
                    }

                    Tile tile = Framing.GetTileSafely(x, y);
                    if (tile.HasTile && Main.tileSolid[tile.TileType] && !tile.IsActuated)
                    {
                        worldPosition = Vector2.Zero;
                        return false;
                    }
                }
            }

            if (requireWallBackdrop && !HasEnoughHousingBackdrop(leftX, rightX, topY, groundTileY - 1))
            {
                worldPosition = Vector2.Zero;
                return false;
            }

            worldPosition = new Vector2(worldX, worldY);
            return true;
        }

        private static bool HasEnoughHousingBackdrop(int leftX, int rightX, int topY, int bottomY)
        {
            int width = rightX - leftX + 1;
            int columnsWithWalls = 0;

            for (int x = leftX; x <= rightX; x++)
            {
                bool columnHasWall = false;
                for (int y = topY; y <= bottomY; y++)
                {
                    if (!WorldGen.InWorld(x, y, 5))
                        return false;

                    if (Framing.GetTileSafely(x, y).WallType > 0)
                    {
                        columnHasWall = true;
                        break;
                    }
                }

                if (columnHasWall)
                    columnsWithWalls++;
            }

            return columnsWithWalls >= Math.Max(1, (width / 2) + 1);
        }

        /// <summary>
        /// Apply config-aware world settings after regeneration.
        /// Allows player to choose whether to preserve evil type, dungeon side, etc.
        /// </summary>
        private static void ApplyConfigurableWorldSettings(WorldProgressSnapshot before, bool announce)
        {
            if (before == null)
                return;

            var config = ModContent.GetInstance<DynamicWorldsConfig>();

            // Preserve evil type (Crimson vs Corruption) if configured
            if (config.PreserveEvilType)
            {
                WorldGen.crimson = before.crimson;
                if (announce)
                    Main.NewText("Evil type preserved from previous world.", 150, 200, 255);
            }

            // Note: Dungeon side and biome features are baked into terrain during generation.
            // These cannot be changed after world generation without major restructuring.
            // The PreserveDungeonSide and PreserveBiomeFeatures settings are documented
            // as aspirational features that would require custom world generation code.
            // For now, they serve as markers for future implementation.
            
            if (announce && config.PreserveDungeonSide)
            {
                Main.NewText("Dungeon side preservation is not implemented yet.", 255, 200, 100);
            }

            if (announce && config.PreserveBiomeFeatures)
            {
                Main.NewText("Biome feature preservation is not implemented yet.", 255, 200, 100);
            }
        }

        private static void RunProgressStep(GenerationProgress progress, string message, double weight, Action action)
        {
            if (progress == null)
            {
                action();
                return;
            }

            progress.Message = message;
            progress.Start(weight);
            progress.Set(0.05d);
            action();
            progress.Set(1d);
            progress.End();
        }
    }

    public class RegenWorldCommand : ModCommand
    {
        public override CommandType Type => CommandType.World | CommandType.Console;
        public override string Command => "regenworld";
        public override string Usage => "/regenworld [seed]";
        public override string Description =>
            "Regenerates the world layout while keeping Hardmode, ores, bosses, invasions, etc. " +
            "Optionally pass a seed: /regenworld 12345 or /regenworld myseedname.";

        public override void Action(CommandCaller caller, string input, string[] args)
        {
            if (!DynamicWorldsPermissions.CanRunRegen(caller, out string deniedReason))
            {
                DynamicWorldsPermissions.ReplyDenied(caller, deniedReason);
                return;
            }

            if (DynamicWorldRegenSystem.IsBusy || MultiplayerRegenSystem.IsBusy)
            {
                caller.Reply("World regeneration is already in progress.", new Color(255, 200, 80));
                return;
            }

            string seed = args.Length > 0 ? args[0] : null;

            if (Main.netMode == NetmodeID.SinglePlayer)
            {
                SingleplayerRegenHelper.RegenerateWorldWithProgress(seed);
                return;
            }

            if (!ModContent.GetInstance<DynamicWorldsConfig>().EnableMultiplayerRegen)
            {
                caller.Reply("Multiplayer regen is disabled in the Dynamic Worlds config.", new Color(255, 200, 80));
                return;
            }

            if (MultiplayerRegenSystem.QueueRegen(seed, caller, out string queueMessage))
                caller.Reply(queueMessage, new Color(180, 220, 255));
            else
                caller.Reply(queueMessage, new Color(255, 120, 120));
        }
    }

    public class MultiRegenWorldCommand : ModCommand
    {
        public override CommandType Type => CommandType.World | CommandType.Console;
        public override string Command => "multiregen";
        public override string Usage => "/multiregen <count> [seed]";
        public override string Description =>
            "Runs the full loading-screen world regen flow multiple times in a row, reloading the player into the world between cycles. Single-player only.";

        public override void Action(CommandCaller caller, string input, string[] args)
        {
            if (!DynamicWorldsPermissions.CanRunRegen(caller, out string deniedReason))
            {
                DynamicWorldsPermissions.ReplyDenied(caller, deniedReason);
                return;
            }

            if (Main.netMode != NetmodeID.SinglePlayer)
            {
                caller.Reply("Full multiplayer regen is not implemented yet. Use /multiregen in single player for now.", new Color(255, 200, 80));
                return;
            }

            if (args.Length == 0)
            {
                caller.Reply("Usage: /multiregen <count> [seed]", new Color(255, 230, 150));
                return;
            }

            if (!int.TryParse(args[0], out int cycleCount) || cycleCount <= 0)
            {
                caller.Reply("Regen count must be a positive whole number.", new Color(255, 80, 80));
                return;
            }

            string seed = args.Length > 1 ? args[1] : null;
            SingleplayerRegenHelper.RegenerateWorldWithProgress(seed, 1, cycleCount);
        }
    }
    
    // /hardmode and /down commands exactly as we had them before:
    // (no changes needed to integrate with world-load printing / saving)



    //snap command that just prints the current snapshot
    public class SnapshotCommand : ModCommand
    {
        public override CommandType Type => CommandType.Chat;
        public override string Command => "snap";
        public override string Usage => "/snap";
        public override string Description =>
            "Prints the current world progression snapshot and scheduled regen status.";

        public override void Action(CommandCaller caller, string input, string[] args)
        {
            var snap = WorldProgressUtil.Capture();
            WorldProgressUtil.PrintSnapshotToChat("Snapshot", snap);

            Main.NewText(WorldRegenScheduler.GetStatusText(), 200, 80, 255);
        }
    }
}
