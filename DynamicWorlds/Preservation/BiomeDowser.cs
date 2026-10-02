using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.DataStructures;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using Terraria.ObjectData;
using Terraria.Utilities;
using DynamicWorlds.UI;

namespace DynamicWorlds
{
    public enum BiomeDowserPlacementMode
    {
        Surface,
        Underground,
        Floating,
    }

    public enum BiomeDowserOceanPlacement
    {
        OceanFloor,
        DryBeach,
        Boat,
        Submarine,
    }

    public sealed class PylonRegenStrategy
    {
        public TeleportPylonType PylonType { get; init; }
        public BiomeDowserPlacementMode[] Modes { get; init; } = Array.Empty<BiomeDowserPlacementMode>();
        public BiomeDowserOceanPlacement[] OceanPlacements { get; init; } = Array.Empty<BiomeDowserOceanPlacement>();
        public bool SupportsSkyIsland { get; init; }
        public bool SupportsAether { get; init; }
    }

    public struct BiomeDowserPylonPreferences
    {
        public BiomeDowserPlacementMode PlacementMode;
        public int FloatingYOffsetFromSurface;
        public int UndergroundYOffsetFromSurface;
        public BiomeDowserOceanPlacement OceanPlacement;
        public bool PreferSkyIslandSurface;
        public bool PreferAetherCavern;

        public static BiomeDowserPylonPreferences DefaultFor(TeleportPylonType pylonType)
        {
            return new BiomeDowserPylonPreferences
            {
                PlacementMode = BiomeDowserPlacementHelper.GetSupportedModes(pylonType).FirstOrDefault(BiomeDowserPlacementMode.Surface),
                FloatingYOffsetFromSurface = -32,
                UndergroundYOffsetFromSurface = 60,
                OceanPlacement = BiomeDowserOceanPlacement.OceanFloor,
                PreferSkyIslandSurface = false,
                PreferAetherCavern = false,
            };
        }
    }

    internal static class BiomeDowserPlacementHelper
    {
        public static string GetLabel(BiomeDowserPlacementMode mode)
        {
            return mode switch
            {
                BiomeDowserPlacementMode.Underground => "underground",
                BiomeDowserPlacementMode.Floating => "floating",
                _ => "surface",
            };
        }

        public static BiomeDowserPlacementMode[] GetSupportedModes(TeleportPylonType pylonType)
        {
            return pylonType switch
            {
                TeleportPylonType.SurfacePurity => new[] { BiomeDowserPlacementMode.Surface, BiomeDowserPlacementMode.Floating },
                TeleportPylonType.Jungle => new[] { BiomeDowserPlacementMode.Surface, BiomeDowserPlacementMode.Underground, BiomeDowserPlacementMode.Floating },
                TeleportPylonType.Hallow => new[] { BiomeDowserPlacementMode.Surface, BiomeDowserPlacementMode.Underground, BiomeDowserPlacementMode.Floating },
                TeleportPylonType.Underground => new[] { BiomeDowserPlacementMode.Underground },
                TeleportPylonType.Desert => new[] { BiomeDowserPlacementMode.Surface, BiomeDowserPlacementMode.Underground, BiomeDowserPlacementMode.Floating },
                TeleportPylonType.Snow => new[] { BiomeDowserPlacementMode.Surface, BiomeDowserPlacementMode.Underground, BiomeDowserPlacementMode.Floating },
                TeleportPylonType.Beach => new[] { BiomeDowserPlacementMode.Surface },
                TeleportPylonType.GlowingMushroom => new[] { BiomeDowserPlacementMode.Underground },
                TeleportPylonType.Victory => new[] { BiomeDowserPlacementMode.Surface, BiomeDowserPlacementMode.Underground, BiomeDowserPlacementMode.Floating },
                _ => new[] { BiomeDowserPlacementMode.Surface, BiomeDowserPlacementMode.Floating },
            };
        }

        public static PylonRegenStrategy GetRegenStrategy(TeleportPylonType pylonType)
        {
            return pylonType switch
            {
                TeleportPylonType.SurfacePurity => new PylonRegenStrategy
                {
                    PylonType = pylonType,
                    Modes = new[] { BiomeDowserPlacementMode.Surface, BiomeDowserPlacementMode.Floating },
                    SupportsSkyIsland = true,
                },
                TeleportPylonType.Jungle => new PylonRegenStrategy
                {
                    PylonType = pylonType,
                    Modes = new[] { BiomeDowserPlacementMode.Surface, BiomeDowserPlacementMode.Underground, BiomeDowserPlacementMode.Floating },
                },
                TeleportPylonType.Hallow => new PylonRegenStrategy
                {
                    PylonType = pylonType,
                    Modes = new[] { BiomeDowserPlacementMode.Surface, BiomeDowserPlacementMode.Underground, BiomeDowserPlacementMode.Floating },
                },
                TeleportPylonType.Underground => new PylonRegenStrategy
                {
                    PylonType = pylonType,
                    Modes = new[] { BiomeDowserPlacementMode.Underground },
                    SupportsAether = true,
                },
                TeleportPylonType.Desert => new PylonRegenStrategy
                {
                    PylonType = pylonType,
                    Modes = new[] { BiomeDowserPlacementMode.Surface, BiomeDowserPlacementMode.Underground, BiomeDowserPlacementMode.Floating },
                },
                TeleportPylonType.Snow => new PylonRegenStrategy
                {
                    PylonType = pylonType,
                    Modes = new[] { BiomeDowserPlacementMode.Surface, BiomeDowserPlacementMode.Underground, BiomeDowserPlacementMode.Floating },
                },
                TeleportPylonType.Beach => new PylonRegenStrategy
                {
                    PylonType = pylonType,
                    Modes = new[] { BiomeDowserPlacementMode.Surface },
                    OceanPlacements = new[]
                    {
                        BiomeDowserOceanPlacement.OceanFloor,
                        BiomeDowserOceanPlacement.DryBeach,
                        BiomeDowserOceanPlacement.Boat,
                        BiomeDowserOceanPlacement.Submarine,
                    },
                },
                TeleportPylonType.GlowingMushroom => new PylonRegenStrategy
                {
                    PylonType = pylonType,
                    Modes = new[] { BiomeDowserPlacementMode.Underground },
                },
                TeleportPylonType.Victory => new PylonRegenStrategy
                {
                    PylonType = pylonType,
                    Modes = new[] { BiomeDowserPlacementMode.Surface, BiomeDowserPlacementMode.Underground, BiomeDowserPlacementMode.Floating },
                    SupportsSkyIsland = true,
                    SupportsAether = true,
                },
                _ => new PylonRegenStrategy
                {
                    PylonType = pylonType,
                    Modes = new[] { BiomeDowserPlacementMode.Surface, BiomeDowserPlacementMode.Floating },
                },
            };
        }

        public static bool SupportsMode(TeleportPylonType pylonType, BiomeDowserPlacementMode mode)
        {
            return GetSupportedModes(pylonType).Contains(mode);
        }

        public static BiomeDowserPlacementMode SanitizeForPylon(TeleportPylonType pylonType, BiomeDowserPlacementMode requested)
        {
            BiomeDowserPlacementMode[] supportedModes = GetSupportedModes(pylonType);
            if (supportedModes.Length == 0)
                return BiomeDowserPlacementMode.Surface;

            return supportedModes.Contains(requested)
                ? requested
                : supportedModes[0];
        }

        public static IEnumerable<BiomeDowserPlacementMode> EnumeratePlacementAttempts(TeleportPylonType pylonType, BiomeDowserPlacementMode preferred)
        {
            BiomeDowserPlacementMode[] supportedModes = GetSupportedModes(pylonType);
            if (supportedModes.Length == 0)
                yield break;

            BiomeDowserPlacementMode first = SanitizeForPylon(pylonType, preferred);
            yield return first;

            foreach (BiomeDowserPlacementMode mode in supportedModes)
            {
                if (mode != first)
                    yield return mode;
            }
        }
    }

    public readonly struct ZoneTileBounds
    {
        public readonly Point16 TopLeft;
        public readonly Point16 BottomRight;

        public ZoneTileBounds(Point16 topLeft, Point16 bottomRight)
        {
            TopLeft = topLeft;
            BottomRight = bottomRight;
        }

        public bool Contains(Point16 point) =>
            point.X >= TopLeft.X && point.X <= BottomRight.X &&
            point.Y >= TopLeft.Y && point.Y <= BottomRight.Y;

        public bool Overlaps(ZoneTileBounds other) =>
            TopLeft.X <= other.BottomRight.X && BottomRight.X >= other.TopLeft.X &&
            TopLeft.Y <= other.BottomRight.Y && BottomRight.Y >= other.TopLeft.Y;
    }

    public sealed class BiomeDowserZone
    {
        // Slightly lower so saved zones with dense custom mushrooms still qualify
        private const int GlowingMushroomTileThreshold = 80;
        private const ushort VanillaMushroomVinesTileId = 528;
        private static readonly Point ApproximateSceneMetricsZoneScanSize = new Point(169, 124);

        public BuildingZone Zone = new();
        public TeleportPylonType PylonType;
        public Point16 PylonOffset;
        public BiomeDowserPlacementMode PlacementMode = BiomeDowserPlacementMode.Surface;
        public int FloatingYOffsetFromSurface = -32;
        public int UndergroundYOffsetFromSurface = 60;
        public BiomeDowserOceanPlacement OceanPlacement;
        public bool PreferSkyIslandSurface;
        public bool PreferAetherCavern;
    public string LastRestoreSummary;

        public int Id => Zone.Id;
        public Point16 TopLeft => Zone.TopLeft;
        public Point16 BottomRight => Zone.BottomRight;
        public int Width => Zone.Width;
        public int Height => Zone.Height;

        public Point16 GetPylonTopLeft()
        {
            return new Point16(
                (short)(Zone.TopLeft.X + PylonOffset.X),
                (short)(Zone.TopLeft.Y + PylonOffset.Y));
        }

        public void RefreshChestSnapshots()
        {
            var chestPositions = new List<Point16>(Zone.Chests.Keys);
            foreach (Point16 chestPos in chestPositions)
                Zone.Chests[chestPos] = SavedChestContents.CaptureFromWorld(chestPos);
        }

        public bool TryRestoreToMatchingBiome(IEnumerable<ZoneTileBounds> reservedBounds, out string failureReason)
        {
            foreach (BiomeDowserPlacementMode mode in BiomeDowserPlacementHelper.EnumeratePlacementAttempts(PylonType, PlacementMode))
            {
                if (TryRestoreWithMode(reservedBounds, mode))
                {
                    LastRestoreSummary = FormatRestoreSummary(mode);
                    failureReason = null;
                    return true;
                }
            }

            failureReason =
                $"No valid {GetPylonTypeName(PylonType)} biome placement was found for Biome Dowser zone #{Id} using a {BiomeDowserPlacementHelper.GetLabel(PlacementMode)} preference.";
            LastRestoreSummary = failureReason;
            return false;
        }

        private string FormatRestoreSummary(BiomeDowserPlacementMode usedMode)
        {
            string modeLabel = BiomeDowserPlacementHelper.GetLabel(usedMode);
            string oceanLabel = PylonType == TeleportPylonType.Beach
                ? $" ({OceanPlacement})"
                : string.Empty;

            return $"Zone #{Id} {GetPylonTypeName(PylonType)} -> {modeLabel}{oceanLabel} at ({Zone.TopLeft.X}, {Zone.TopLeft.Y})";
        }

        public TagCompound ToTag()
        {
            return new TagCompound
            {
                ["zone"] = Zone.ToTag(),
                ["pylonType"] = (int)PylonType,
                ["pylonOffsetX"] = (int)PylonOffset.X,
                ["pylonOffsetY"] = (int)PylonOffset.Y,
                ["placementMode"] = (int)PlacementMode,
                ["floatingYOffsetFromSurface"] = FloatingYOffsetFromSurface,
                ["undergroundYOffsetFromSurface"] = UndergroundYOffsetFromSurface,
                ["oceanPlacement"] = (int)OceanPlacement,
                ["preferSkyIsland"] = PreferSkyIslandSurface,
                ["preferAether"] = PreferAetherCavern,
            };
        }

        public static BiomeDowserZone FromTag(TagCompound tag)
        {
            return new BiomeDowserZone
            {
                Zone = BuildingZone.FromTag(tag.Get<TagCompound>("zone")),
                PylonType = (TeleportPylonType)tag.GetInt("pylonType"),
                PylonOffset = new Point16(
                    (short)tag.GetInt("pylonOffsetX"),
                    (short)tag.GetInt("pylonOffsetY")),
                PlacementMode = tag.ContainsKey("placementMode")
                    ? (BiomeDowserPlacementMode)tag.GetInt("placementMode")
                    : BiomeDowserPlacementMode.Surface,
                FloatingYOffsetFromSurface = tag.ContainsKey("floatingYOffsetFromSurface")
                    ? tag.GetInt("floatingYOffsetFromSurface")
                    : -96,
                UndergroundYOffsetFromSurface = tag.ContainsKey("undergroundYOffsetFromSurface")
                    ? tag.GetInt("undergroundYOffsetFromSurface")
                    : 60,
                OceanPlacement = tag.ContainsKey("oceanPlacement")
                    ? (BiomeDowserOceanPlacement)tag.GetInt("oceanPlacement")
                    : BiomeDowserOceanPlacement.OceanFloor,
                PreferSkyIslandSurface = tag.ContainsKey("preferSkyIsland")
                    ? tag.GetBool("preferSkyIsland")
                    : false,
                PreferAetherCavern = tag.ContainsKey("preferAether")
                    ? tag.GetBool("preferAether")
                    : false,
            };
        }

        public static bool TryCapture(
            Point16 topLeft,
            Point16 bottomRight,
            int id,
            Func<TeleportPylonType, BiomeDowserPylonPreferences> preferPreferences,
            out BiomeDowserZone zone,
            out string errorMessage)
        {
            zone = null;
            List<Point16> pylons = FindContainedVanillaPylons(topLeft, bottomRight);

            if (pylons.Count == 0)
            {
                errorMessage = "Biome Dowser zones must fully contain exactly one vanilla pylon.";
                return false;
            }

            if (pylons.Count > 1)
            {
                errorMessage = "Biome Dowser zones can only contain one pylon. Shrink the selection until only one remains.";
                return false;
            }

            Point16 pylonTopLeft = pylons[0];
            TeleportPylonType pylonType = GetPylonType(pylonTopLeft);
            BuildingZone capturedZone = BuildingZone.CaptureConnected(
                topLeft,
                bottomRight,
                EnumerateFootprintTiles(pylonTopLeft),
                id);

            BiomeDowserPylonPreferences requestedPrefs = preferPreferences?.Invoke(pylonType)
                ?? BiomeDowserPylonPreferences.DefaultFor(pylonType);
            requestedPrefs.PlacementMode = BiomeDowserPlacementHelper.SanitizeForPylon(pylonType, requestedPrefs.PlacementMode);
            zone = new BiomeDowserZone
            {
                Zone = capturedZone,
                PylonType = pylonType,
                PylonOffset = new Point16(
                    (short)(pylonTopLeft.X - capturedZone.TopLeft.X),
                    (short)(pylonTopLeft.Y - capturedZone.TopLeft.Y)),
                PlacementMode = requestedPrefs.PlacementMode,
                FloatingYOffsetFromSurface = requestedPrefs.PlacementMode == BiomeDowserPlacementMode.Floating
                    ? requestedPrefs.FloatingYOffsetFromSurface
                    : Math.Min(-48, (int)Math.Round(pylonTopLeft.Y - Main.worldSurface)),
                UndergroundYOffsetFromSurface = requestedPrefs.UndergroundYOffsetFromSurface,
                OceanPlacement = requestedPrefs.OceanPlacement,
                PreferSkyIslandSurface = requestedPrefs.PreferSkyIslandSurface,
                PreferAetherCavern = requestedPrefs.PreferAetherCavern,
            };

            errorMessage = null;
            return true;
        }

        private bool TryRestoreWithMode(IEnumerable<ZoneTileBounds> reservedBounds, BiomeDowserPlacementMode mode)
        {
            var validCandidates = new List<(ZoneRestorePlacement placement, int centerX)>();

            foreach (int candidateCenterX in EnumerateCandidateCenterXs())
            {
                ZoneRestorePlacement placement = PredictRestorePlacement(candidateCenterX, mode);
                if (!IsPlacementUsable(placement))
                    continue;

                var placementBounds = new ZoneTileBounds(placement.TopLeft, placement.BottomRight);
                if (OverlapsReserved(placementBounds, reservedBounds))
                    continue;

                if (ContainsAnchoredTile(placementBounds))
                    continue;

                if (IsDungeonOrTempleArea(placementBounds))
                    continue;

                Point16 pylonTopLeft = new Point16(
                    (short)(placement.TopLeft.X + PylonOffset.X),
                    (short)(placement.TopLeft.Y + PylonOffset.Y));

                if (!MatchesPlacementMode(mode, pylonTopLeft, PylonType, OceanPlacement))
                    continue;

                if (!MatchesPylonBiome(pylonTopLeft))
                    continue;

                validCandidates.Add((placement, candidateCenterX));
            }

            if (validCandidates.Count == 0)
                return false;

            int bestScore = int.MinValue;
            var scoredCandidates = new List<(ZoneRestorePlacement placement, int score)>(validCandidates.Count);
            foreach (var candidate in validCandidates)
            {
                int score = ScorePlacement(candidate.placement, candidate.centerX, mode);
                scoredCandidates.Add((candidate.placement, score));
                bestScore = Math.Max(bestScore, score);
            }

            int scoreThreshold = bestScore <= 0
                ? bestScore
                : Math.Max(1, (int)Math.Ceiling(bestScore * 0.8f));

            var preferredCandidates = scoredCandidates
                .Where(candidate => candidate.score >= scoreThreshold)
                .ToList();

            int chosenIndex = (WorldGen.genRand ?? Main.rand).Next(preferredCandidates.Count);
            Zone.RestoreToPlacement(preferredCandidates[chosenIndex].placement, "[BiomeDowser]");
            return true;
        }

        private int ScorePlacement(ZoneRestorePlacement placement, int candidateCenterX, BiomeDowserPlacementMode mode)
        {
            Point16 pylonTopLeft = new Point16(
                (short)(placement.TopLeft.X + PylonOffset.X),
                (short)(placement.TopLeft.Y + PylonOffset.Y));

            int score = ScoreBiomeCentrality(candidateCenterX, mode);
            score += ScoreFeatureProximity(pylonTopLeft);
            return score;
        }

        private ZoneRestorePlacement PredictRestorePlacement(int targetCenterX, BiomeDowserPlacementMode mode)
        {
            if (PylonType == TeleportPylonType.Beach)
                return PredictBeachPlacement(targetCenterX);

            if (mode == BiomeDowserPlacementMode.Floating)
                return PredictFloatingPlacement(targetCenterX);

            int groundSearchStart = 0;
            if (mode == BiomeDowserPlacementMode.Underground)
            {
                if (PreferAetherCavern)
                {
                    // Start the search well into the rock layer so we land inside the stone shell around the Aether.
                    groundSearchStart = Math.Max((int)Main.rockLayer + 60, 0);
                }
                else
                {
                    int desiredY = (int)Math.Round(Main.worldSurface + UndergroundYOffsetFromSurface);
                    groundSearchStart = Math.Max(0, desiredY);
                }
            }
            return Zone.PredictRestorePlacement(targetCenterX, groundSearchStart);
        }

        private ZoneRestorePlacement PredictBeachPlacement(int targetCenterX)
        {
            return OceanPlacement switch
            {
                BiomeDowserOceanPlacement.DryBeach => PredictDryBeachPlacement(targetCenterX),
                BiomeDowserOceanPlacement.Boat => PredictBoatPlacement(targetCenterX),
                BiomeDowserOceanPlacement.Submarine => PredictSubmarinePlacement(targetCenterX),
                _ => PredictOceanFloorPlacement(targetCenterX),
            };
        }

        private ZoneRestorePlacement PredictDryBeachPlacement(int targetCenterX)
        {
            targetCenterX = ClampToOceanBand(targetCenterX);
            return Zone.PredictRestorePlacement(targetCenterX, Math.Max(10, (int)Main.worldSurface - 24));
        }

        private ZoneRestorePlacement PredictBoatPlacement(int targetCenterX)
        {
            targetCenterX = SnapToNearestOceanWaterColumn(targetCenterX, Math.Max(6, Math.Min(18, Zone.Height / 2)));
            int waterSurface = FindWaterSurfaceY(targetCenterX);
            int boatFloorRelativeY = FindBoatFloorRelativeY();
            int targetTopY = Math.Max(12, waterSurface - boatFloorRelativeY - 1);
            return BuildPlacementFromTopY(targetCenterX, targetTopY, skipSupportBridging: true);
        }

        private ZoneRestorePlacement PredictSubmarinePlacement(int targetCenterX)
        {
            targetCenterX = SnapToNearestOceanWaterColumn(targetCenterX, Math.Max(10, Zone.Height + 4));
            int waterSurface = FindWaterSurfaceY(targetCenterX);
            int oceanFloor = FindOceanFloorY(targetCenterX);
            int minTopY = Math.Max(20, waterSurface + 8);
            int maxTopY = Math.Max(minTopY, oceanFloor - Zone.Height - 3);
            int targetTopY = Math.Min(minTopY + 18, maxTopY);
            return BuildPlacementFromTopY(targetCenterX, targetTopY, skipSupportBridging: true);
        }

        private ZoneRestorePlacement PredictOceanFloorPlacement(int targetCenterX)
        {
            targetCenterX = SnapToNearestOceanWaterColumn(targetCenterX, Math.Max(10, Zone.Height + 4));
            int floor = FindOceanFloorY(targetCenterX);
            return BuildPlacementFromBottomY(targetCenterX, floor - 1);
        }

        private ZoneRestorePlacement PredictFloatingPlacement(int targetCenterX)
        {
            int deltaX = targetCenterX - Zone.CenterX;
            int targetPylonY = Math.Clamp(
                (int)Math.Round(Main.worldSurface + FloatingYOffsetFromSurface),
                48,
                Math.Max(48, (int)Main.worldSurface - 24));
            Point16 currentPylon = GetPylonTopLeft();
            int deltaY = targetPylonY - currentPylon.Y;

            Point16 newTopLeft = new Point16(
                (short)(Zone.TopLeft.X + deltaX),
                (short)(Zone.TopLeft.Y + deltaY));
            Point16 newBottomRight = new Point16(
                (short)(Zone.BottomRight.X + deltaX),
                (short)(Zone.BottomRight.Y + deltaY));

            return new ZoneRestorePlacement(newTopLeft, newBottomRight, deltaX, deltaY, newBottomRight.Y);
        }

        private int ScoreBiomeCentrality(int candidateCenterX, BiomeDowserPlacementMode mode)
        {
            return ScoreBiomeSpanDirection(candidateCenterX, mode, -1)
                + ScoreBiomeSpanDirection(candidateCenterX, mode, 1);
        }

        private int ScoreFeatureProximity(Point16 pylonTopLeft)
        {
            int score = 0;

            if (PylonType == TeleportPylonType.GlowingMushroom)
                score += ScoreMushroomPlacement(pylonTopLeft);

            if (PreferAetherCavern)
                score += ScoreAetherPlacement(pylonTopLeft);

            return score;
        }

        private int ScoreMushroomPlacement(Point16 pylonTopLeft)
        {
            ZoneTileBounds zoneBounds = GetRestoredZoneBounds(pylonTopLeft);
            Rectangle broadArea = CreateArea(
                zoneBounds.TopLeft.X - 28,
                zoneBounds.TopLeft.Y - 20,
                zoneBounds.BottomRight.X + 28,
                zoneBounds.BottomRight.Y + 20);
            Rectangle tightArea = CreateArea(
                pylonTopLeft.X - 24,
                pylonTopLeft.Y - 18,
                pylonTopLeft.X + 24,
                pylonTopLeft.Y + 22);

            int broadMushroomTiles = CountNearbyGlowingMushroomTiles(broadArea);
            int tightMushroomTiles = CountNearbyGlowingMushroomTiles(tightArea);
            int giantMushrooms = CountNearbyGiantGlowingMushrooms(broadArea);

            return broadMushroomTiles * 2
                + tightMushroomTiles * 4
                + giantMushrooms * 90;
        }

        private int ScoreAetherPlacement(Point16 pylonTopLeft)
        {
            EnsureAetherSample();
            if (_cachedAetherCenter == null)
                return 0;

            ZoneTileBounds zoneBounds = GetRestoredZoneBounds(pylonTopLeft);
            Point16 shimmerCenter = _cachedAetherCenter.Value;
            int dx = DistanceToRange(shimmerCenter.X, zoneBounds.TopLeft.X, zoneBounds.BottomRight.X);
            int dy = DistanceToRange(shimmerCenter.Y, zoneBounds.TopLeft.Y, zoneBounds.BottomRight.Y);

            int proximityScore = Math.Max(0, 420 - (dx * 4) - (dy * 2));

            bool preferLeftSide = Zone.CenterX <= shimmerCenter.X;
            int sideGap = preferLeftSide
                ? Math.Max(0, shimmerCenter.X - zoneBounds.BottomRight.X)
                : Math.Max(0, zoneBounds.TopLeft.X - shimmerCenter.X);
            int targetGap = Math.Max(3, Math.Min(8, Zone.Width / 5));
            int clearanceScore = Math.Max(0, 160 - Math.Abs(sideGap - targetGap) * 16);

            return proximityScore + clearanceScore;
        }

        private int ScoreBiomeSpanDirection(int candidateCenterX, BiomeDowserPlacementMode mode, int direction)
        {
            int sampleStep = GetCandidateStep();
            const int maxSamplesPerSide = 12;

            int score = 0;
            for (int sampleIndex = 1; sampleIndex <= maxSamplesPerSide; sampleIndex++)
            {
                int neighborCenterX = candidateCenterX + (direction * sampleStep * sampleIndex);
                ZoneRestorePlacement placement = PredictRestorePlacement(neighborCenterX, mode);
                if (!IsPlacementUsable(placement))
                    break;

                Point16 pylonTopLeft = new Point16(
                    (short)(placement.TopLeft.X + PylonOffset.X),
                    (short)(placement.TopLeft.Y + PylonOffset.Y));

                if (!MatchesPlacementMode(mode, pylonTopLeft, PylonType, OceanPlacement) || !MatchesPylonBiome(pylonTopLeft))
                    break;

                score += maxSamplesPerSide - sampleIndex + 1;
            }

            return score;
        }

        private static bool IsPlacementUsable(ZoneRestorePlacement placement)
        {
            return WorldGen.InWorld(placement.TopLeft.X, placement.TopLeft.Y, 5)
                && WorldGen.InWorld(placement.BottomRight.X, placement.BottomRight.Y, 5);
        }

        private static bool OverlapsReserved(ZoneTileBounds placementBounds, IEnumerable<ZoneTileBounds> reservedBounds)
        {
            if (reservedBounds == null)
                return false;

            foreach (ZoneTileBounds reserved in reservedBounds)
            {
                if (placementBounds.Overlaps(reserved))
                    return true;
            }

            return false;
        }

        private static bool ContainsAnchoredTile(ZoneTileBounds placementBounds)
        {
            foreach (Point16 anchoredTile in AnchoredTileSystem.AnchoredTiles.Keys)
            {
                if (placementBounds.Contains(anchoredTile))
                    return true;
            }

            return false;
        }

        private static bool IsDungeonOrTempleArea(ZoneTileBounds bounds)
        {
            for (int x = bounds.TopLeft.X; x <= bounds.BottomRight.X; x += 3)
            {
                for (int y = bounds.TopLeft.Y; y <= bounds.BottomRight.Y; y += 3)
                {
                    Tile t = Framing.GetTileSafely(x, y);
                    if (IsDungeonTile(t) || IsTempleTile(t))
                        return true;
                }
            }

            return false;
        }

        private static bool IsDungeonTile(Tile t)
        {
            return (t.HasTile && (t.TileType == TileID.BlueDungeonBrick || t.TileType == TileID.GreenDungeonBrick || t.TileType == TileID.PinkDungeonBrick))
                || (t.WallType > 0 && Main.wallDungeon[t.WallType]);
        }

        private static bool IsTempleTile(Tile t)
        {
            return t.HasTile && t.TileType == TileID.LihzahrdBrick;
        }

        private int GetCandidateStep()
        {
            return PylonType switch
            {
                TeleportPylonType.GlowingMushroom => 4,
                TeleportPylonType.Beach => 8,
                _ => 16,
            };
        }

        private IEnumerable<int> EnumerateCandidateCenterXs()
        {
            int candidateStep = GetCandidateStep();
            const int edgeBuffer = 180; // keep regen away from world edges to avoid ocean/void issues
            int minCenterX = Math.Max(12 + edgeBuffer, GetZoneLeftSpanWithPadding());
            int maxCenterX = Math.Min(Main.maxTilesX - 12 - edgeBuffer, Main.maxTilesX - GetZoneRightSpanWithPadding() - 1);

            if (minCenterX > maxCenterX)
                yield break;

            if (PreferAetherCavern)
                EnsureAetherSample();

            if (PreferAetherCavern && _cachedAetherCenter.HasValue)
            {
                int aetherX = Math.Clamp(FindPreferredAetherPlacementCenterX(_cachedAetherCenter.Value), minCenterX, maxCenterX);
                yield return aetherX;
            }

            if (PylonType == TeleportPylonType.Beach)
            {
                bool preferLeftOcean = Zone.CenterX < Main.maxTilesX / 2;
                foreach (int candidateX in EnumerateOceanCandidateCenterXs(candidateStep, preferLeftOcean))
                    yield return candidateX;

                foreach (int candidateX in EnumerateOceanCandidateCenterXs(candidateStep, !preferLeftOcean))
                    yield return candidateX;

                yield break;
            }

            int preferredCenterX = Math.Clamp(Zone.CenterX, minCenterX, maxCenterX);
            yield return preferredCenterX;

            for (int offset = candidateStep; ; offset += candidateStep)
            {
                bool added = false;
                int left = preferredCenterX - offset;
                int right = preferredCenterX + offset;

                if (left >= minCenterX)
                {
                    yield return left;
                    added = true;
                }

                if (right <= maxCenterX)
                {
                    yield return right;
                    added = true;
                }

                if (!added)
                    yield break;
            }
        }

        private int GetRandomUndergroundStartY(int sampleX)
        {
            int minY = Math.Max(10, (int)(Main.worldSurface * 0.9));
            int maxY = (int)Math.Min(Main.maxTilesY - 120, Main.rockLayer + 200);
            if (maxY <= minY)
                return Math.Max(10, (int)Main.worldSurface + 16);

            UnifiedRandom rand = WorldGen.genRand ?? Main.rand;
            int candidate = rand.Next(minY, maxY);

            // Bias slightly toward the biome we're scanning: resample if the random depth
            // lands inside the dungeon/temple column to avoid repeated failures.
            if (IsDungeonColumn(sampleX) || IsTempleColumn(sampleX))
                candidate = Math.Max(minY, Math.Min(maxY, candidate + 80));

            return candidate;
        }

        private static bool IsDungeonColumn(int x)
        {
            for (int y = 20; y < Main.maxTilesY - 120; y += 6)
            {
                Tile t = Framing.GetTileSafely(x, y);
                if (IsDungeonTile(t))
                    return true;
            }
            return false;
        }

        private static bool IsTempleColumn(int x)
        {
            for (int y = (int)Main.rockLayer; y < Main.maxTilesY - 120; y += 6)
            {
                Tile t = Framing.GetTileSafely(x, y);
                if (IsTempleTile(t))
                    return true;
            }
            return false;
        }

        private static int FindWaterSurfaceY(int x)
        {
            int scanStart = 10;
            for (int y = scanStart; y < Main.maxTilesY - 20; y++)
            {
                Tile t = Framing.GetTileSafely(x, y);
                if (t.LiquidAmount > 200 && t.LiquidType == LiquidID.Water)
                {
                    // Walk upward to the surface of this water column
                    int surface = y;
                    while (surface > 10 && Framing.GetTileSafely(x, surface - 1).LiquidAmount > 0)
                        surface--;
                    return surface;
                }
            }

            return (int)Main.worldSurface;
        }

        private static int GetWaterDepth(int x)
        {
            int surface = FindWaterSurfaceY(x);
            int depth = 0;

            for (int y = surface; y < Math.Min(Main.maxTilesY - 20, surface + 320); y++)
            {
                Tile t = Framing.GetTileSafely(x, y);
                if (t.LiquidAmount > 140 && t.LiquidType == LiquidID.Water)
                {
                    depth++;
                    continue;
                }

                if (depth > 0)
                {
                    if (t.HasTile && !t.IsActuated)
                        break;

                    if (t.LiquidAmount <= 20)
                        break;
                }
                else if (t.HasTile && !t.IsActuated)
                {
                    return 0;
                }
            }

            return depth;
        }

        private static int FindNearestWaterColumn(int targetX, int minX, int maxX, int minimumWaterDepth)
        {
            const int step = 2;
            int searchRadius = Math.Max(220, maxX - minX);

            if (HasWaterColumn(targetX, minimumWaterDepth))
                return targetX;

            for (int dist = step; dist <= searchRadius; dist += step)
            {
                int left = targetX - dist;
                int right = targetX + dist;

                if (left >= minX && HasWaterColumn(left, minimumWaterDepth))
                    return left;
                if (right <= maxX && HasWaterColumn(right, minimumWaterDepth))
                    return right;
            }

            return targetX;
        }

        private static bool HasWaterColumn(int x, int minimumWaterDepth = 4)
        {
            return GetWaterDepth(x) >= minimumWaterDepth;
        }

        private static int FindOceanFloorY(int x)
        {
            int surface = FindWaterSurfaceY(x);
            for (int y = surface; y < Math.Min(Main.maxTilesY - 40, surface + 260); y++)
            {
                Tile t = Framing.GetTileSafely(x, y);
                if (t.HasTile && !t.IsActuated)
                    return y;
            }

            return Math.Min(Main.maxTilesY - 80, surface + 60);
        }

        private bool MatchesPlacementMode(
            BiomeDowserPlacementMode mode,
            Point16 pylonTopLeft,
            TeleportPylonType pylonType,
            BiomeDowserOceanPlacement oceanPlacement)
        {
            if (pylonType == TeleportPylonType.Beach)
            {
                return MatchesSpecificOceanPlacement(pylonTopLeft, oceanPlacement);
            }

            return mode switch
            {
                BiomeDowserPlacementMode.Underground => !IsSurfaceLikePlacement(pylonTopLeft),
                BiomeDowserPlacementMode.Floating => pylonTopLeft.Y < Main.worldSurface - 24,
                _ => IsSurfaceLikePlacement(pylonTopLeft),
            };
        }

        private bool IsSurfaceLikePlacement(Point16 pylonTopLeft)
        {
            const int maxDistanceFromLocalGround = 14;
            const int sampleSpacing = 1;
            int pylonBottomY = pylonTopLeft.Y + 3;
            int successfulSamples = 0;
            int totalSamples = 0;

            for (int offsetX = 0; offsetX <= 2; offsetX += sampleSpacing)
            {
                int sampleX = pylonTopLeft.X + offsetX;
                if (!WorldGen.InWorld(sampleX, pylonBottomY, 10))
                    continue;

                totalSamples++;
                int localGroundY = BuildingZone.FindGroundY(sampleX, 10);
                if (Math.Abs(localGroundY - pylonBottomY) <= maxDistanceFromLocalGround)
                    successfulSamples++;
            }

            return totalSamples > 0 && successfulSamples >= Math.Max(1, totalSamples - 1);
        }

        private static IEnumerable<Point16> EnumerateFootprintTiles(Point16 pylonTopLeft)
        {
            Tile tile = Framing.GetTileSafely(pylonTopLeft.X, pylonTopLeft.Y);
            TileObjectData data = TileObjectData.GetTileData(tile);
            if (data == null)
            {
                yield return pylonTopLeft;
                yield break;
            }

            for (int x = pylonTopLeft.X; x < pylonTopLeft.X + data.Width; x++)
            {
                for (int y = pylonTopLeft.Y; y < pylonTopLeft.Y + data.Height; y++)
                    yield return new Point16((short)x, (short)y);
            }
        }

        private static List<Point16> FindContainedVanillaPylons(Point16 topLeft, Point16 bottomRight)
        {
            var pylons = new List<Point16>();
            var seen = new HashSet<Point16>();

            for (int x = topLeft.X; x <= bottomRight.X; x++)
            {
                for (int y = topLeft.Y; y <= bottomRight.Y; y++)
                {
                    if (!WorldGen.InWorld(x, y, 1))
                        continue;

                    Tile tile = Framing.GetTileSafely(x, y);
                    if (!tile.HasTile || tile.TileType != TileID.TeleportationPylon)
                        continue;

                    Point16 pylonTopLeft = TileObjectData.TopLeft(x, y);
                    if (!seen.Add(pylonTopLeft))
                        continue;

                    if (IsContainedPylon(pylonTopLeft, topLeft, bottomRight))
                        pylons.Add(pylonTopLeft);
                }
            }

            return pylons;
        }

        private static bool IsContainedPylon(Point16 pylonTopLeft, Point16 selectionTopLeft, Point16 selectionBottomRight)
        {
            if (!WorldGen.InWorld(pylonTopLeft.X, pylonTopLeft.Y, 1))
                return false;

            Tile tile = Framing.GetTileSafely(pylonTopLeft.X, pylonTopLeft.Y);
            if (!tile.HasTile || tile.TileType != TileID.TeleportationPylon)
                return false;

            TileObjectData data = TileObjectData.GetTileData(tile);
            if (data == null)
                return false;

            int pylonBottomX = pylonTopLeft.X + data.Width - 1;
            int pylonBottomY = pylonTopLeft.Y + data.Height - 1;

            return pylonTopLeft.X >= selectionTopLeft.X
                && pylonTopLeft.Y >= selectionTopLeft.Y
                && pylonBottomX <= selectionBottomRight.X
                && pylonBottomY <= selectionBottomRight.Y;
        }

        private static TeleportPylonType GetPylonType(Point16 pylonTopLeft)
        {
            Tile tile = Framing.GetTileSafely(pylonTopLeft.X, pylonTopLeft.Y);
            int style = tile.TileFrameX / 54;
            return (TeleportPylonType)style;
        }

        internal static string GetPylonTypeName(TeleportPylonType pylonType)
        {
            return pylonType switch
            {
                TeleportPylonType.SurfacePurity => "surface",
                TeleportPylonType.Jungle => "jungle",
                TeleportPylonType.Hallow => "hallow",
                TeleportPylonType.Underground => "underground",
                TeleportPylonType.Desert => "desert",
                TeleportPylonType.Snow => "snow",
                TeleportPylonType.Beach => "beach",
                TeleportPylonType.GlowingMushroom => "glowing mushroom",
                TeleportPylonType.Victory => "universal",
                _ => pylonType.ToString(),
            };
        }

        private bool MatchesPylonBiome(Point16 pylonTopLeft)
        {
            if (!TryScanSceneMetricsAt(pylonTopLeft, out SceneMetrics sceneMetrics))
                return false;

            if (sceneMetrics.EnoughTilesForCrimson || sceneMetrics.EnoughTilesForCorruption)
                return false;

            if (PylonType == TeleportPylonType.GlowingMushroom)
                return MatchesGlowingMushroomBiome(pylonTopLeft);

            bool requireAether = PreferAetherCavern && _cachedAetherCenter != null;
            bool result = PylonType switch
            {
                TeleportPylonType.SurfacePurity =>
                    IsSurfaceLikePlacement(pylonTopLeft) &&
                    !IsBeachCandidate(pylonTopLeft) &&
                    !sceneMetrics.EnoughTilesForJungle &&
                    !sceneMetrics.EnoughTilesForSnow &&
                    !sceneMetrics.EnoughTilesForDesert &&
                    !sceneMetrics.EnoughTilesForGlowingMushroom &&
                    !sceneMetrics.EnoughTilesForHallow &&
                    (!PreferSkyIslandSurface || IsSkyIslandSurface(pylonTopLeft)),
                TeleportPylonType.Jungle => sceneMetrics.EnoughTilesForJungle,
                TeleportPylonType.Hallow => sceneMetrics.EnoughTilesForHallow,
                TeleportPylonType.Underground => pylonTopLeft.Y >= Main.worldSurface && (!requireAether || IsNearAether(pylonTopLeft)),
                TeleportPylonType.Desert => sceneMetrics.EnoughTilesForDesert,
                TeleportPylonType.Snow => sceneMetrics.EnoughTilesForSnow,
                TeleportPylonType.Beach => MatchesOceanPreference(pylonTopLeft),
                TeleportPylonType.Victory => true,
                _ => false,
            };

            return result;
        }

        private bool MatchesGlowingMushroomBiome(Point16 pylonTopLeft)
        {
            if (Main.remixWorld && pylonTopLeft.Y >= Main.maxTilesY - 200)
                return false;

            // First try a few local scene-metric probes around the pylon footprint
            (int dx, int dy)[] scanOffsets =
            {
                (0, 0),
                (4, 2),
                (-6, 2),
                (10, 4),
                (-12, 4),
                (0, 10),
                (0, -6),
            };

            foreach ((int dx, int dy) in scanOffsets)
            {
                var scanTile = new Point16(
                    (short)(pylonTopLeft.X + dx),
                    (short)(pylonTopLeft.Y + dy));

                if (TryScanSceneMetricsAt(scanTile, out SceneMetrics sceneMetrics) &&
                    sceneMetrics.EnoughTilesForGlowingMushroom)
                {
                    return true;
                }
            }

            Rectangle scanArea = GetSceneMetricsTileArea(pylonTopLeft);
            int nearbyMushroomTiles = CountNearbyGlowingMushroomTiles(scanArea);
            if (nearbyMushroomTiles >= GlowingMushroomTileThreshold)
                return true;

            // Heuristic: clusters of giant glowing mushrooms (tile 626) strongly signal the biome
            int nearbyGiantMushrooms = CountNearbyGiantGlowingMushrooms(scanArea);
            if (nearbyGiantMushrooms >= 3)
                return true;

            int savedMushroomTiles = CountSavedGlowingMushroomTiles(scanArea, pylonTopLeft);
            return nearbyMushroomTiles + savedMushroomTiles >= GlowingMushroomTileThreshold;
        }

        private static bool TryScanSceneMetricsAt(Point16 scanTile, out SceneMetrics sceneMetrics)
        {
            sceneMetrics = null;
            if (!WorldGen.InWorld(scanTile.X, scanTile.Y, 10))
                return false;

            sceneMetrics = new SceneMetrics();
            sceneMetrics.ScanAndExportToMain(new SceneMetricsScanSettings
            {
                BiomeScanCenterPositionInWorld = scanTile.ToWorldCoordinates(),
            });
            return true;
        }

        private static Rectangle GetSceneMetricsTileArea(Point16 centerTile)
        {
            int halfWidth = ApproximateSceneMetricsZoneScanSize.X / 2;
            int halfHeight = ApproximateSceneMetricsZoneScanSize.Y / 2;

            int left = Math.Max(0, centerTile.X - halfWidth);
            int right = Math.Min(Main.maxTilesX - 1, centerTile.X + halfWidth);
            int top = Math.Max(0, centerTile.Y - halfHeight);
            int bottom = Math.Min(Main.maxTilesY - 1, centerTile.Y + halfHeight);

            return new Rectangle(left, top, right - left + 1, bottom - top + 1);
        }

        private static int CountNearbyGlowingMushroomTiles(Rectangle scanArea)
        {
            int count = 0;

            for (int x = scanArea.Left; x < scanArea.Right; x++)
            {
                for (int y = scanArea.Top; y < scanArea.Bottom; y++)
                {
                    if (!WorldGen.InWorld(x, y, 1))
                        continue;

                    Tile tile = Framing.GetTileSafely(x, y);
                    if (!tile.HasTile || tile.IsActuated)
                        continue;

                    if (IsGlowingMushroomBiomeTile(tile.TileType))
                        count++;
                }
            }

            return count;
        }

        private static int CountNearbyGiantGlowingMushrooms(Rectangle scanArea)
        {
            int count = 0;

            for (int x = scanArea.Left; x < scanArea.Right; x++)
            {
                for (int y = scanArea.Top; y < scanArea.Bottom; y++)
                {
                    if (!WorldGen.InWorld(x, y, 1))
                        continue;

                    Tile tile = Framing.GetTileSafely(x, y);
                    if (!tile.HasTile || tile.IsActuated)
                        continue;

                    if (tile.TileType == 626) // Giant Glowing Mushroom
                        count++;
                }
            }

            return count;
        }

        private int CountSavedGlowingMushroomTiles(Rectangle scanArea, Point16 restoredPylonTopLeft)
        {
            int count = 0;
            Point16 restoredZoneTopLeft = new Point16(
                (short)(restoredPylonTopLeft.X - PylonOffset.X),
                (short)(restoredPylonTopLeft.Y - PylonOffset.Y));

            foreach (var kv in Zone.Tiles)
            {
                AnchoredTileData tileData = kv.Value;
                if (!tileData.Active || !IsGlowingMushroomBiomeTile(tileData.TileType))
                    continue;

                Point16 relativeTile = new Point16(
                    (short)(kv.Key.X - Zone.TopLeft.X),
                    (short)(kv.Key.Y - Zone.TopLeft.Y));
                Point16 translatedTile = new Point16(
                    (short)(restoredZoneTopLeft.X + relativeTile.X),
                    (short)(restoredZoneTopLeft.Y + relativeTile.Y));

                if (scanArea.Contains(translatedTile.X, translatedTile.Y))
                    count++;
            }

            return count;
        }

        private static bool IsGlowingMushroomBiomeTile(ushort tileType)
        {
           
            return tileType == TileID.MushroomGrass
                || tileType == TileID.MushroomPlants
                || tileType == TileID.MushroomTrees
                || tileType == VanillaMushroomVinesTileId
                || tileType == 626; // Giant Glowing Mushroom
        }

        private static bool IsBeachCandidate(Point16 pylonTopLeft)
        {
            int oceanBandWidth = GetOceanBandWidth();
            if (!(pylonTopLeft.X <= oceanBandWidth
                || pylonTopLeft.X >= Main.maxTilesX - oceanBandWidth))
                return false;

            Rectangle scan = new Rectangle(
                Math.Max(0, pylonTopLeft.X - 80),
                Math.Max(10, (int)(Main.worldSurface * 0.35f) - 20),
                Math.Min(Main.maxTilesX - 1, pylonTopLeft.X + 80) - Math.Max(0, pylonTopLeft.X - 80) + 1,
                Math.Min(Main.maxTilesY - 1, (int)Main.worldSurface + 160) - Math.Max(10, (int)(Main.worldSurface * 0.35f) - 20) + 1);

            int sandCount = 0;
            int waterCount = 0;

            for (int x = scan.Left; x < scan.Right; x++)
            {
                for (int y = scan.Top; y < scan.Bottom; y++)
                {
                    Tile t = Framing.GetTileSafely(x, y);
                    if (t.HasTile && !t.IsActuated)
                    {
                        ushort tt = t.TileType;
                        if (tt == TileID.Sand || tt == TileID.Pearlsand || tt == TileID.Ebonsand || tt == TileID.Crimsand
                            || tt == TileID.HardenedSand || tt == TileID.Sandstone)
                            sandCount++;
                    }

                    if (t.LiquidAmount > 40)
                        waterCount++;
                }
            }

            return sandCount >= 250 && waterCount >= 180;
        }

        private bool MatchesOceanPreference(Point16 pylonTopLeft)
        {
            if (!IsBeachCandidate(pylonTopLeft))
                return false;

            return MatchesSpecificOceanPlacement(pylonTopLeft, OceanPlacement);
        }

        private bool MatchesSpecificOceanPlacement(Point16 pylonTopLeft, BiomeDowserOceanPlacement oceanPlacement)
        {
            return oceanPlacement switch
            {
                BiomeDowserOceanPlacement.DryBeach => IsDryBeachPlacement(pylonTopLeft),
                BiomeDowserOceanPlacement.Boat => IsBoatPlacement(pylonTopLeft),
                BiomeDowserOceanPlacement.Submarine => IsSubmergedPlacement(pylonTopLeft),
                _ => IsOceanFloorPlacement(pylonTopLeft),
            };
        }

        private ZoneTileBounds GetRestoredZoneBounds(Point16 pylonTopLeft)
        {
            Point16 topLeft = new Point16(
                (short)(pylonTopLeft.X - PylonOffset.X),
                (short)(pylonTopLeft.Y - PylonOffset.Y));
            Point16 bottomRight = new Point16(
                (short)(topLeft.X + Zone.Width - 1),
                (short)(topLeft.Y + Zone.Height - 1));
            return new ZoneTileBounds(topLeft, bottomRight);
        }

        private static Rectangle CreateArea(int left, int top, int right, int bottom)
        {
            int clampedLeft = Math.Max(0, left);
            int clampedTop = Math.Max(0, top);
            int clampedRight = Math.Min(Main.maxTilesX - 1, right);
            int clampedBottom = Math.Min(Main.maxTilesY - 1, bottom);
            if (clampedRight < clampedLeft || clampedBottom < clampedTop)
                return Rectangle.Empty;

            return new Rectangle(
                clampedLeft,
                clampedTop,
                clampedRight - clampedLeft + 1,
                clampedBottom - clampedTop + 1);
        }

        private static int CountWaterTiles(Rectangle area, int liquidThreshold = 40)
        {
            if (area == Rectangle.Empty)
                return 0;

            int waterTiles = 0;
            for (int x = area.Left; x < area.Right; x++)
            {
                for (int y = area.Top; y < area.Bottom; y++)
                {
                    Tile t = Framing.GetTileSafely(x, y);
                    if (t.LiquidAmount >= liquidThreshold && t.LiquidType == LiquidID.Water)
                        waterTiles++;
                }
            }

            return waterTiles;
        }

        private static int CountSolidTiles(Rectangle area)
        {
            if (area == Rectangle.Empty)
                return 0;

            int solidTiles = 0;
            for (int x = area.Left; x < area.Right; x++)
            {
                for (int y = area.Top; y < area.Bottom; y++)
                {
                    Tile tile = Framing.GetTileSafely(x, y);
                    if (tile.HasTile && !tile.IsActuated && Main.tileSolid[tile.TileType] && !TileID.Sets.Platforms[tile.TileType])
                        solidTiles++;
                }
            }

            return solidTiles;
        }

        private static int GetTileCount(Rectangle area)
        {
            return area == Rectangle.Empty ? 0 : area.Width * area.Height;
        }

        private static int CountWaterAroundZone(ZoneTileBounds zoneBounds, int horizontalPadding, int topPadding, int bottomPadding, int liquidThreshold = 40)
        {
            Rectangle expandedArea = CreateArea(
                zoneBounds.TopLeft.X - horizontalPadding,
                zoneBounds.TopLeft.Y - topPadding,
                zoneBounds.BottomRight.X + horizontalPadding,
                zoneBounds.BottomRight.Y + bottomPadding);
            Rectangle zoneArea = CreateArea(
                zoneBounds.TopLeft.X,
                zoneBounds.TopLeft.Y,
                zoneBounds.BottomRight.X,
                zoneBounds.BottomRight.Y);

            return Math.Max(0, CountWaterTiles(expandedArea, liquidThreshold) - CountWaterTiles(zoneArea, liquidThreshold));
        }

        private static int GetWaterSampleTileCountAroundZone(ZoneTileBounds zoneBounds, int horizontalPadding, int topPadding, int bottomPadding)
        {
            Rectangle expandedArea = CreateArea(
                zoneBounds.TopLeft.X - horizontalPadding,
                zoneBounds.TopLeft.Y - topPadding,
                zoneBounds.BottomRight.X + horizontalPadding,
                zoneBounds.BottomRight.Y + bottomPadding);
            Rectangle zoneArea = CreateArea(
                zoneBounds.TopLeft.X,
                zoneBounds.TopLeft.Y,
                zoneBounds.BottomRight.X,
                zoneBounds.BottomRight.Y);

            return Math.Max(0, GetTileCount(expandedArea) - GetTileCount(zoneArea));
        }

        private bool IsDryBeachPlacement(Point16 pylonTopLeft)
        {
            ZoneTileBounds zoneBounds = GetRestoredZoneBounds(pylonTopLeft);
            bool leftOcean = pylonTopLeft.X < Main.maxTilesX / 2;
            int shorelineWaterX = FindOceanShorelineWaterColumn(leftOcean);
            int waterAroundZone = CountWaterAroundZone(zoneBounds, 3, 1, 3);
            bool fullyLandward = leftOcean
                ? zoneBounds.TopLeft.X > shorelineWaterX
                : zoneBounds.BottomRight.X < shorelineWaterX;
            return fullyLandward
                && zoneBounds.BottomRight.Y <= Main.worldSurface + 20
                && waterAroundZone <= Math.Max(18, Zone.Width * 2);
        }

        private bool IsBoatPlacement(Point16 pylonTopLeft)
        {
            ZoneTileBounds zoneBounds = GetRestoredZoneBounds(pylonTopLeft);
            bool leftOcean = pylonTopLeft.X < Main.maxTilesX / 2;
            int shorelineWaterX = FindOceanShorelineWaterColumn(leftOcean);
            Rectangle waterlineArea = CreateArea(zoneBounds.TopLeft.X - 1, zoneBounds.BottomRight.Y + 1, zoneBounds.BottomRight.X + 1, zoneBounds.BottomRight.Y + 5);
            Rectangle upperArea = CreateArea(zoneBounds.TopLeft.X - 1, zoneBounds.TopLeft.Y - 2, zoneBounds.BottomRight.X + 1, zoneBounds.TopLeft.Y + Math.Max(2, Zone.Height / 2));
            int waterAtHull = CountWaterTiles(waterlineArea);
            int waterInsideUpper = CountWaterTiles(upperArea, 80);
            int minHullWater = Math.Max(8, waterlineArea.Width * 2);
            int maxUpperWater = Math.Max(8, upperArea.Width);
            bool fullyWaterward = leftOcean
                ? zoneBounds.BottomRight.X < shorelineWaterX
                : zoneBounds.TopLeft.X > shorelineWaterX;
            return fullyWaterward && waterAtHull >= minHullWater && waterInsideUpper <= maxUpperWater;
        }

        private bool IsSubmergedPlacement(Point16 pylonTopLeft)
        {
            ZoneTileBounds zoneBounds = GetRestoredZoneBounds(pylonTopLeft);
            bool leftOcean = pylonTopLeft.X < Main.maxTilesX / 2;
            int shorelineWaterX = FindOceanShorelineWaterColumn(leftOcean);
            int submerged = CountWaterAroundZone(zoneBounds, 2, 2, 2, 140);
            int total = GetWaterSampleTileCountAroundZone(zoneBounds, 2, 2, 2);
            bool fullyWaterward = leftOcean
                ? zoneBounds.BottomRight.X < shorelineWaterX
                : zoneBounds.TopLeft.X > shorelineWaterX;
            return fullyWaterward && total > 0 && submerged >= total * 0.45f;
        }

        private bool IsOceanFloorPlacement(Point16 pylonTopLeft)
        {
            ZoneTileBounds zoneBounds = GetRestoredZoneBounds(pylonTopLeft);
            bool leftOcean = pylonTopLeft.X < Main.maxTilesX / 2;
            int shorelineWaterX = FindOceanShorelineWaterColumn(leftOcean);
            Rectangle belowArea = CreateArea(zoneBounds.TopLeft.X, zoneBounds.BottomRight.Y + 1, zoneBounds.BottomRight.X, zoneBounds.BottomRight.Y + 3);
            int solidBelow = CountSolidTiles(belowArea);
            int waterAround = CountWaterAroundZone(zoneBounds, 2, 2, 2, 80);
            int minSolidBelow = Math.Max(6, belowArea.Width * 2);
            int minWaterAround = Math.Max(12, Zone.Width * Math.Max(2, Zone.Height / 3));
            bool fullyWaterward = leftOcean
                ? zoneBounds.BottomRight.X < shorelineWaterX
                : zoneBounds.TopLeft.X > shorelineWaterX;
            return fullyWaterward && solidBelow >= minSolidBelow && waterAround >= minWaterAround;
        }

        private static bool IsSkyIslandSurface(Point16 pylonTopLeft)
        {
            if (pylonTopLeft.Y > Main.worldSurface - 40)
                return false;

            Rectangle scan = new Rectangle(pylonTopLeft.X - 10, pylonTopLeft.Y - 6, 22, 12);
            int cloudTiles = 0;
            for (int x = scan.Left; x < scan.Right; x++)
            {
                for (int y = scan.Top; y < scan.Bottom; y++)
                {
                    Tile t = Framing.GetTileSafely(x, y);
                    if (t.HasTile && (t.TileType == TileID.Cloud || t.TileType == TileID.RainCloud || t.TileType == TileID.Sunplate))
                        cloudTiles++;
                }
            }

            return cloudTiles >= 10;
        }

        private static Point16? _cachedAetherCenter;
        private static bool _searchedAether;

        private static int GetOceanBandWidth()
        {
            return Math.Max(420, (int)(Main.maxTilesX * 0.065f));
        }

        private int GetZoneLeftSpanWithPadding()
        {
            return Math.Max(4, Zone.CenterX - Zone.TopLeft.X + 2);
        }

        private int GetZoneRightSpanWithPadding()
        {
            return Math.Max(4, Zone.BottomRight.X - Zone.CenterX + 2);
        }

        private void GetOceanHorizontalBounds(bool leftOcean, out int minCenterX, out int maxCenterX)
        {
            int leftSpan = GetZoneLeftSpanWithPadding();
            int rightSpan = GetZoneRightSpanWithPadding();
            int oceanBandWidth = GetOceanBandWidth();
            int worldMin = Math.Max(leftSpan, 20);
            int worldMax = Math.Max(worldMin, Main.maxTilesX - rightSpan - 1);

            if (leftOcean)
            {
                minCenterX = worldMin;
                maxCenterX = Math.Max(minCenterX, Math.Min(oceanBandWidth, worldMax));
            }
            else
            {
                minCenterX = Math.Min(worldMax, Math.Max(Main.maxTilesX - oceanBandWidth, worldMin));
                maxCenterX = worldMax;
            }
        }

        private int ClampToOceanBand(int targetCenterX)
        {
            GetOceanHorizontalBounds(targetCenterX < Main.maxTilesX / 2, out int minCenterX, out int maxCenterX);
            return Math.Clamp(targetCenterX, minCenterX, maxCenterX);
        }

        private int SnapToNearestOceanWaterColumn(int targetCenterX, int minimumWaterDepth = 4)
        {
            GetOceanHorizontalBounds(targetCenterX < Main.maxTilesX / 2, out int minCenterX, out int maxCenterX);
            targetCenterX = Math.Clamp(targetCenterX, minCenterX, maxCenterX);
            return FindNearestWaterColumn(targetCenterX, minCenterX, maxCenterX, minimumWaterDepth);
        }

        private IEnumerable<int> EnumerateOceanCandidateCenterXs(int candidateStep, bool leftOcean)
        {
            GetOceanHorizontalBounds(leftOcean, out int minCenterX, out int maxCenterX);
            if (minCenterX > maxCenterX)
                yield break;

            int preferredCenterX = GetPreferredOceanCenterX(leftOcean, minCenterX, maxCenterX);
            yield return preferredCenterX;

            for (int offset = candidateStep; ; offset += candidateStep)
            {
                bool added = false;
                int left = preferredCenterX - offset;
                int right = preferredCenterX + offset;

                if (left >= minCenterX)
                {
                    yield return left;
                    added = true;
                }

                if (right <= maxCenterX)
                {
                    yield return right;
                    added = true;
                }

                if (!added)
                    yield break;
            }
        }

        private int GetPreferredOceanCenterX(bool leftOcean, int minCenterX, int maxCenterX)
        {
            return OceanPlacement switch
            {
                BiomeDowserOceanPlacement.DryBeach => FindPreferredDryBeachCenterX(leftOcean, minCenterX, maxCenterX),
                BiomeDowserOceanPlacement.Boat => FindPreferredWaterCenterX(leftOcean, minCenterX, maxCenterX, Math.Max(6, Math.Min(18, Zone.Height / 2))),
                BiomeDowserOceanPlacement.Submarine => FindPreferredWaterCenterX(leftOcean, minCenterX, maxCenterX, Math.Max(10, Zone.Height + 4)),
                _ => FindPreferredWaterCenterX(leftOcean, minCenterX, maxCenterX, Math.Max(10, Zone.Height + 4)),
            };
        }

        private int FindPreferredDryBeachCenterX(bool leftOcean, int minCenterX, int maxCenterX)
        {
            int shorelineLandX = FindOceanShorelineLandColumn(leftOcean, minCenterX, maxCenterX);
            int clearance = Math.Max(4, Math.Min(12, Zone.Width / 5));
            int targetCenterX = leftOcean
                ? shorelineLandX + GetZoneLeftSpanWithPadding() + clearance
                : shorelineLandX - GetZoneRightSpanWithPadding() - clearance;
            return Math.Clamp(targetCenterX, minCenterX, maxCenterX);
        }

        private int FindPreferredWaterCenterX(bool leftOcean, int minCenterX, int maxCenterX, int minimumWaterDepth)
        {
            int shorelineWaterX = FindOceanShorelineWaterColumn(leftOcean, minCenterX, maxCenterX);
            int clearance = Math.Max(6, Math.Min(16, Zone.Width / 4));
            int nearShoreCenterX = leftOcean
                ? shorelineWaterX - GetZoneRightSpanWithPadding() - clearance
                : shorelineWaterX + GetZoneLeftSpanWithPadding() + clearance;
            nearShoreCenterX = Math.Clamp(nearShoreCenterX, minCenterX, maxCenterX);

            if (HasWaterColumn(nearShoreCenterX, minimumWaterDepth))
                return nearShoreCenterX;

            int direction = leftOcean ? -1 : 1;
            for (int offset = 4; offset <= maxCenterX - minCenterX; offset += 4)
            {
                int preferred = nearShoreCenterX + direction * offset;
                int alternate = nearShoreCenterX - direction * offset;

                if (preferred >= minCenterX && preferred <= maxCenterX && HasWaterColumn(preferred, minimumWaterDepth))
                    return preferred;
                if (alternate >= minCenterX && alternate <= maxCenterX && HasWaterColumn(alternate, minimumWaterDepth))
                    return alternate;
            }

            return nearShoreCenterX;
        }

        private int FindOceanShorelineWaterColumn(bool leftOcean)
        {
            GetOceanHorizontalBounds(leftOcean, out int minCenterX, out int maxCenterX);
            return FindOceanShorelineWaterColumn(leftOcean, minCenterX, maxCenterX);
        }

        private static int FindOceanShorelineWaterColumn(bool leftOcean, int minCenterX, int maxCenterX)
        {
            int start = leftOcean ? maxCenterX : minCenterX;
            int end = leftOcean ? minCenterX : maxCenterX;
            int step = leftOcean ? -1 : 1;

            for (int x = start; leftOcean ? x >= end : x <= end; x += step)
            {
                if (HasWaterColumn(x, 6))
                    return x;
            }

            return Math.Clamp((minCenterX + maxCenterX) / 2, minCenterX, maxCenterX);
        }

        private static int FindOceanShorelineLandColumn(bool leftOcean, int minCenterX, int maxCenterX)
        {
            int shorelineWaterX = FindOceanShorelineWaterColumn(leftOcean, minCenterX, maxCenterX);
            int inlandDirection = leftOcean ? 1 : -1;

            for (int offset = 1; offset <= 80; offset++)
            {
                int candidateX = shorelineWaterX + inlandDirection * offset;
                if (candidateX < minCenterX || candidateX > maxCenterX)
                    break;

                if (!HasWaterColumn(candidateX, 4))
                    return candidateX;
            }

            int fallback = shorelineWaterX + inlandDirection * 8;
            return Math.Clamp(fallback, minCenterX, maxCenterX);
        }

        private ZoneRestorePlacement BuildPlacementFromBottomY(int targetCenterX, int targetBottomY, bool skipSupportBridging = false)
        {
            int deltaX = targetCenterX - Zone.CenterX;
            int clampedBottomY = Math.Clamp(targetBottomY, Zone.Height + 10, Main.maxTilesY - 80);
            int deltaY = clampedBottomY - Zone.BottomRight.Y;
            var newTopLeft = new Point16((short)(Zone.TopLeft.X + deltaX), (short)(Zone.TopLeft.Y + deltaY));
            var newBottomRight = new Point16((short)(Zone.BottomRight.X + deltaX), (short)clampedBottomY);
            return new ZoneRestorePlacement(newTopLeft, newBottomRight, deltaX, deltaY, clampedBottomY, skipSupportBridging);
        }

        private ZoneRestorePlacement BuildPlacementFromTopY(int targetCenterX, int targetTopY, bool skipSupportBridging = false)
        {
            int targetBottomY = targetTopY + Zone.Height - 1;
            return BuildPlacementFromBottomY(targetCenterX, targetBottomY, skipSupportBridging);
        }

        private int FindBoatFloorRelativeY()
        {
            int bestRelativeY = Math.Clamp(Zone.Height / 2, 0, Math.Max(0, Zone.Height - 1));
            int bestScore = int.MinValue;

            for (int relY = 0; relY < Zone.Height; relY++)
            {
                int solidCount = 0;
                int walkableCount = 0;

                for (int relX = 0; relX < Zone.Width; relX++)
                {
                    if (!TryGetSavedTileAtRelative(relX, relY, out AnchoredTileData tileData) || !IsSavedSolidTile(tileData))
                        continue;

                    solidCount++;
                    if (HasSavedHeadroom(relX, relY))
                        walkableCount++;
                }

                if (solidCount == 0)
                    continue;

                int score = (walkableCount * 24) + (solidCount * 4) + relY;
                if (score > bestScore || (score == bestScore && relY > bestRelativeY))
                {
                    bestScore = score;
                    bestRelativeY = relY;
                }
            }

            return bestRelativeY;
        }

        private bool TryGetSavedTileAtRelative(int relX, int relY, out AnchoredTileData tileData)
        {
            var worldPoint = new Point16(
                (short)(Zone.TopLeft.X + relX),
                (short)(Zone.TopLeft.Y + relY));
            return Zone.Tiles.TryGetValue(worldPoint, out tileData);
        }

        private static bool IsSavedSolidTile(AnchoredTileData tileData)
        {
            return tileData.Active
                && !tileData.IsActuated
                && Main.tileSolid[tileData.TileType]
                && !TileID.Sets.Platforms[tileData.TileType];
        }

        private bool HasSavedHeadroom(int relX, int relY)
        {
            for (int offset = 1; offset <= 2; offset++)
            {
                int aboveY = relY - offset;
                if (aboveY < 0)
                    continue;

                if (TryGetSavedTileAtRelative(relX, aboveY, out AnchoredTileData aboveTile) && IsSavedSolidTile(aboveTile))
                    return false;
            }

            return true;
        }

        public static void ResetAetherCache()
        {
            _cachedAetherCenter = null;
            _searchedAether = false;
        }

        private bool IsNearAether(Point16 pylonTopLeft)
        {
            EnsureAetherSample();
            if (_cachedAetherCenter == null)
                return false;

            ZoneTileBounds zoneBounds = GetRestoredZoneBounds(pylonTopLeft);
            if (ZoneContainsShimmer(zoneBounds))
                return false;

            Point16 shimmerCenter = _cachedAetherCenter.Value;
            int dx = DistanceToRange(shimmerCenter.X, zoneBounds.TopLeft.X, zoneBounds.BottomRight.X);
            int dy = DistanceToRange(shimmerCenter.Y, zoneBounds.TopLeft.Y, zoneBounds.BottomRight.Y);
            return dx <= 96 && dy <= 140;
        }

        private static int DistanceToRange(int value, int min, int max)
        {
            if (value < min)
                return min - value;
            if (value > max)
                return value - max;
            return 0;
        }

        private static bool ZoneContainsShimmer(ZoneTileBounds bounds)
        {
            for (int x = bounds.TopLeft.X; x <= bounds.BottomRight.X; x++)
            {
                for (int y = bounds.TopLeft.Y; y <= bounds.BottomRight.Y; y++)
                {
                    Tile t = Framing.GetTileSafely(x, y);
                    if (t.LiquidAmount > 40 && t.LiquidType == LiquidID.Shimmer)
                        return true;
                }
            }

            return false;
        }

        private int FindPreferredAetherPlacementCenterX(Point16 shimmerCenter)
        {
            bool preferLeftSide = Zone.CenterX <= shimmerCenter.X;
            int shellColumn = FindAetherShellColumn(shimmerCenter, preferLeftSide);
            int clearance = Math.Max(3, Math.Min(8, Zone.Width / 5));
            int targetCenterX = preferLeftSide
                ? shellColumn - GetZoneRightSpanWithPadding() - clearance
                : shellColumn + GetZoneLeftSpanWithPadding() + clearance;

            int minCenterX = GetZoneLeftSpanWithPadding();
            int maxCenterX = Main.maxTilesX - GetZoneRightSpanWithPadding() - 1;
            return Math.Clamp(targetCenterX, minCenterX, maxCenterX);
        }

        private static int FindAetherShellColumn(Point16 shimmerCenter, bool preferLeftSide)
        {
            const int searchRadius = 180;
            const int step = 2;

            for (int dist = 0; dist <= searchRadius; dist += step)
            {
                int preferred = preferLeftSide ? shimmerCenter.X - dist : shimmerCenter.X + dist;
                int alternate = preferLeftSide ? shimmerCenter.X + dist : shimmerCenter.X - dist;

                if (preferred >= 10 && preferred < Main.maxTilesX - 10 && !ColumnHasShimmer(preferred, shimmerCenter.Y))
                    return preferred;
                if (dist > 0 && alternate >= 10 && alternate < Main.maxTilesX - 10 && !ColumnHasShimmer(alternate, shimmerCenter.Y))
                    return alternate;
            }

            return shimmerCenter.X;
        }

        private static bool ColumnHasShimmer(int x, int shimmerCenterY)
        {
            int top = Math.Max(10, shimmerCenterY - 80);
            int bottom = Math.Min(Main.maxTilesY - 120, shimmerCenterY + 120);

            for (int y = top; y <= bottom; y++)
            {
                Tile t = Framing.GetTileSafely(x, y);
                if (t.LiquidAmount > 40 && t.LiquidType == LiquidID.Shimmer)
                    return true;
            }

            return false;
        }

        private static void EnsureAetherSample()
        {
            if (_searchedAether)
                return;

            _searchedAether = true;
            for (int x = 40; x < Main.maxTilesX - 40; x += 12)
            {
                for (int y = (int)Main.rockLayer; y < Main.maxTilesY - 200; y += 8)
                {
                    Tile t = Framing.GetTileSafely(x, y);
                    if (t.LiquidAmount > 180 && t.LiquidType == LiquidID.Shimmer)
                    {
                        _cachedAetherCenter = new Point16((short)x, (short)y);
                        return;
                    }
                }
            }
        }
    }

    public class BiomeDowserSystem : ModSystem
    {
        public static readonly Dictionary<int, BiomeDowserZone> Zones = new();
        private static readonly List<RestoredZoneTransform> LastRestoreTransforms = new();
        private static readonly List<(string Text, Color Color)> PendingChatLines = new();
        private static int _nextId = 1;

        public static int NextId() => _nextId++;
        public static void RecalculateNextId() => _nextId = Zones.Count == 0 ? 1 : Zones.Keys.Max() + 1;

        public override void OnWorldLoad()
        {
            Zones.Clear();
            LastRestoreTransforms.Clear();
            if (DynamicWorldRegenSystem.CurrentContext == null)
                PendingChatLines.Clear();
            _nextId = 1;
            BiomeDowserZone.ResetAetherCache();
        }

        public override void OnWorldUnload()
        {
            Zones.Clear();
            LastRestoreTransforms.Clear();
            if (DynamicWorldRegenSystem.CurrentContext == null)
                PendingChatLines.Clear();
            BiomeDowserZone.ResetAetherCache();
        }

        public override void PostUpdatePlayers()
        {
            if (PendingChatLines.Count == 0)
                return;

            if (Main.netMode != NetmodeID.SinglePlayer || Main.gameMenu || Main.LocalPlayer == null || !Main.LocalPlayer.active)
                return;

            foreach ((string text, Color color) in PendingChatLines)
                Main.NewText(text, color);

            PendingChatLines.Clear();
        }

        public static void RefreshAllChestSnapshots()
        {
            foreach (BiomeDowserZone zone in Zones.Values)
                zone.RefreshChestSnapshots();
        }

        public static void RestoreAllZones(bool announce = true)
        {
            LastRestoreTransforms.Clear();
            if (Zones.Count == 0)
                return;

            bool logChat = ModContent.GetInstance<DynamicWorldsConfig>().BiomeDowserRegenChatLog;
            var chatSummaries = logChat && Main.netMode == NetmodeID.SinglePlayer
                ? new List<string>()
                : null;

            var reservedBounds = new List<ZoneTileBounds>();
            foreach (BuildingZone structureZone in StructureAnchorSystem.Zones.Values)
                reservedBounds.Add(new ZoneTileBounds(structureZone.TopLeft, structureZone.BottomRight));

            int placedCount = 0;
            foreach (var kv in Zones.OrderBy(kv => kv.Key))
            {
                BiomeDowserZone zone = kv.Value;
                Point16 oldTopLeft = zone.Zone.TopLeft;
                Point16 oldBottomRight = zone.Zone.BottomRight;

                if (!zone.TryRestoreToMatchingBiome(reservedBounds, out string failureReason))
                {
                    ModContent.GetInstance<DynamicWorlds>().Logger.Warn($"[BiomeDowser] {failureReason} Falling back to the original X position.");
                    zone.Zone.RestoreToWorld();
                    zone.LastRestoreSummary = failureReason + " (fell back to original position)";
                }

                if (chatSummaries != null)
                    chatSummaries.Add(zone.LastRestoreSummary ?? $"Zone #{zone.Id} {BiomeDowserZone.GetPylonTypeName(zone.PylonType)} restored.");

                reservedBounds.Add(new ZoneTileBounds(zone.Zone.TopLeft, zone.Zone.BottomRight));
                LastRestoreTransforms.Add(new RestoredZoneTransform(
                    oldTopLeft,
                    oldBottomRight,
                    zone.Zone.TopLeft.X - oldTopLeft.X,
                    zone.Zone.TopLeft.Y - oldTopLeft.Y));
                placedCount++;
            }

            int restoredPylons = PylonRestoreHelper.RestoreVanillaPylons(
                Zones.Values.SelectMany(zone => zone.Zone.Tiles.Keys));
            if (restoredPylons > 0)
            {
                ModContent.GetInstance<DynamicWorlds>().Logger.Info(
                    $"[BiomeDowser] Re-registered {restoredPylons} restored vanilla pylon(s).");
            }

            if (announce && Main.netMode == NetmodeID.SinglePlayer)
            {
                ShowOrQueueChatLine(
                    $"Restored {placedCount} Biome Dowser zone{(placedCount == 1 ? "" : "s")}.",
                    new Color(255, 215, 120));
            }

            if (chatSummaries != null && chatSummaries.Count > 0)
            {
                int maxLines = 10;
                foreach (string line in chatSummaries.Take(maxLines))
                    ShowOrQueueChatLine(line, new Color(180, 230, 255));

                if (chatSummaries.Count > maxLines)
                {
                    ShowOrQueueChatLine(
                        $"...and {chatSummaries.Count - maxLines} more Biome Dowser entries.",
                        new Color(180, 230, 255));
                }
            }
        }

        private static void ShowOrQueueChatLine(string text, Color color)
        {
            if (Main.netMode != NetmodeID.SinglePlayer)
                return;

            if (!Main.gameMenu && Main.LocalPlayer != null && Main.LocalPlayer.active)
            {
                Main.NewText(text, color);
                return;
            }

            PendingChatLines.Add((text, color));
        }

        public static bool TryTranslateSavedPoint(Point16 savedPoint, out Point16 translatedPoint)
        {
            foreach (RestoredZoneTransform transform in LastRestoreTransforms)
            {
                if (transform.Contains(savedPoint))
                {
                    translatedPoint = transform.Translate(savedPoint);
                    return true;
                }
            }

            translatedPoint = default;
            return false;
        }

        public static bool TryGetZoneAt(Point16 tilePosition, out int zoneId)
        {
            foreach (var kv in Zones)
            {
                if (tilePosition.X >= kv.Value.TopLeft.X && tilePosition.X <= kv.Value.BottomRight.X &&
                    tilePosition.Y >= kv.Value.TopLeft.Y && tilePosition.Y <= kv.Value.BottomRight.Y)
                {
                    zoneId = kv.Key;
                    return true;
                }
            }

            zoneId = -1;
            return false;
        }

        internal static bool TryCreateZone(
            Point16 topLeft,
            Point16 bottomRight,
            Func<TeleportPylonType, BiomeDowserPylonPreferences> preferPreferences,
            out BiomeDowserZone zone,
            out string message)
        {
            zone = null;

            if (bottomRight.X - topLeft.X < 2 || bottomRight.Y - topLeft.Y < 3)
            {
                message = "Drag a larger area so the full pylon structure is inside the Biome Dowser zone.";
                return false;
            }

            ZoneTileBounds candidateBounds = new ZoneTileBounds(topLeft, bottomRight);
            foreach (var kv in StructureAnchorSystem.Zones)
            {
                ZoneTileBounds other = new ZoneTileBounds(kv.Value.TopLeft, kv.Value.BottomRight);
                if (candidateBounds.Overlaps(other))
                {
                    message = $"Biome Dowser zone overlaps with structure zone #{kv.Key}. Zones cannot share tiles.";
                    return false;
                }
            }

            foreach (var kv in Zones)
            {
                ZoneTileBounds other = new ZoneTileBounds(kv.Value.TopLeft, kv.Value.BottomRight);
                if (candidateBounds.Overlaps(other))
                {
                    message = $"Biome Dowser zone overlaps with Biome Dowser zone #{kv.Key}. Zones cannot share tiles.";
                    return false;
                }
            }

            if (StructureAnchorSystem.TryFindOverlappingAnchoredTile(topLeft, bottomRight, out Point16 anchoredOverlap))
            {
                message =
                    $"Biome Dowser zones cannot overlap individually anchored tiles. Remove the anchor at ({anchoredOverlap.X}, {anchoredOverlap.Y}) first.";
                return false;
            }

            int zoneId = NextId();
            if (!BiomeDowserZone.TryCapture(topLeft, bottomRight, zoneId, preferPreferences, out zone, out string errorMessage))
            {
                message = errorMessage;
                return false;
            }

            Zones[zoneId] = zone;
            message =
                $"Biome Dowser zone #{zoneId} created for a {zone.PylonType} pylon ({BiomeDowserPlacementHelper.GetLabel(zone.PlacementMode)} preferred): {zone.Width}x{zone.Height}.";
            return true;
        }

        internal static bool RemoveZoneAt(Point16 clickPos, out int removedZoneId, out string message)
        {
            foreach (var kv in Zones)
            {
                if (clickPos.X < kv.Value.TopLeft.X || clickPos.X > kv.Value.BottomRight.X ||
                    clickPos.Y < kv.Value.TopLeft.Y || clickPos.Y > kv.Value.BottomRight.Y)
                {
                    continue;
                }

                removedZoneId = kv.Key;
                Zones.Remove(removedZoneId);
                message = $"Biome Dowser zone #{removedZoneId} removed. ({Zones.Count} zones remain)";
                return true;
            }

            removedZoneId = -1;
            message = "Shift+Click on a Biome Dowser zone to remove it.";
            return false;
        }

        internal static void UpsertSyncedZone(BiomeDowserZone syncedZone)
        {
            if (syncedZone == null)
                return;

            if (Zones.TryGetValue(syncedZone.Id, out BiomeDowserZone existingZone) && existingZone.Zone.Tiles.Count > 0)
                return;

            Zones[syncedZone.Id] = syncedZone;
        }

        internal static void RemoveSyncedZone(int zoneId)
        {
            Zones.Remove(zoneId);
        }

        public override void SaveWorldData(TagCompound tag)
        {
            var list = new List<TagCompound>();
            foreach (BiomeDowserZone zone in Zones.Values)
                list.Add(zone.ToTag());

            tag["BiomeDowserZones"] = list;
            tag["BiomeDowserNextId"] = _nextId;
        }

        public override void LoadWorldData(TagCompound tag)
        {
            Zones.Clear();
            _nextId = 1;

            if (tag.ContainsKey("BiomeDowserZones"))
            {
                foreach (TagCompound zoneTag in tag.GetList<TagCompound>("BiomeDowserZones"))
                {
                    BiomeDowserZone zone = BiomeDowserZone.FromTag(zoneTag);
                    Zones[zone.Id] = zone;
                }
            }

            if (tag.ContainsKey("BiomeDowserNextId"))
                _nextId = tag.GetInt("BiomeDowserNextId");

            if (_nextId <= 0 || _nextId <= Zones.Keys.DefaultIfEmpty(0).Max())
                RecalculateNextId();
        }

        public override void PostDrawTiles()
        {
            Player player = Main.LocalPlayer;
            if (player?.HeldItem == null)
                return;

            if (!WorldToolOverlayHelper.IsHoldingWorldTool(player))
                return;

            SpriteBatch spriteBatch = Main.spriteBatch;
            Vector2 screenPos = Main.screenPosition;
            WorldToolOverlayHelper.BeginOverlay(spriteBatch);

            foreach (BiomeDowserZone zone in Zones.Values)
            {
                WorldToolOverlayHelper.DrawAreaOverlay(
                    spriteBatch,
                    zone.TopLeft,
                    zone.BottomRight,
                    screenPos,
                    new Color(255, 210, 90) * 0.32f,
                    new Color(255, 180, 70));
            }

            BiomeDowserPlayer modPlayer = player.GetModPlayer<BiomeDowserPlayer>();
            if (modPlayer.IsDragging)
            {
                int x0 = Math.Min(modPlayer.DragStart.X, modPlayer.DragEnd.X);
                int x1 = Math.Max(modPlayer.DragStart.X, modPlayer.DragEnd.X);
                int y0 = Math.Min(modPlayer.DragStart.Y, modPlayer.DragEnd.Y);
                int y1 = Math.Max(modPlayer.DragStart.Y, modPlayer.DragEnd.Y);

                WorldToolOverlayHelper.DrawAreaOverlay(
                    spriteBatch,
                    new Point16((short)x0, (short)y0),
                    new Point16((short)x1, (short)y1),
                    screenPos,
                    new Color(255, 225, 140) * 0.36f,
                    Color.Gold);
            }

            spriteBatch.End();
        }
    }

    public class BiomeDowserPlayer : ModPlayer
    {
        public bool IsDragging;
        public Point16 DragStart;
        public Point16 DragEnd;
    public BiomeDowserPlacementMode PlacementMode = BiomeDowserPlacementMode.Surface;
    private readonly Dictionary<TeleportPylonType, BiomeDowserPylonPreferences> _preferences = new();

        private bool _wasLeftMouseHeldLastFrame;
        private bool _wasRightMouseHeldLastFrame;

        public override void PostUpdate()
        {
            if (Main.netMode == NetmodeID.Server || Main.mapFullscreen || Player.whoAmI != Main.myPlayer)
            {
                if (IsDragging)
                    CancelDrag();

                _wasLeftMouseHeldLastFrame = false;
                _wasRightMouseHeldLastFrame = false;
                return;
            }

            bool holding = Player.HeldItem?.type == ModContent.ItemType<BiomeDowser>();
            if (!holding)
            {
                if (IsDragging)
                    CancelDrag();

                _wasLeftMouseHeldLastFrame = false;
                _wasRightMouseHeldLastFrame = false;
                return;
            }

            if (BiomeDowserSettingsSystem.IsVisible)
            {
                if (IsDragging)
                    CancelDrag();

                _wasLeftMouseHeldLastFrame = Main.mouseLeft;
                _wasRightMouseHeldLastFrame = Main.mouseRight;
                return;
            }

            bool leftMouseHeld = Main.mouseLeft && !Main.LocalPlayer.mouseInterface;
            bool rightMouseHeld = Main.mouseRight && !Main.LocalPlayer.mouseInterface;
            bool shiftHeld = Main.keyState.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.LeftShift)
                || Main.keyState.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.RightShift);
            int tileX = (int)(Main.MouseWorld.X / 16f);
            int tileY = (int)(Main.MouseWorld.Y / 16f);
            Point16 mouseTile = new Point16(tileX, tileY);

            if (rightMouseHeld && !_wasRightMouseHeldLastFrame && !IsDragging)
            {
                BiomeDowserSettingsSystem.ToggleUI();
                Main.mouseRightRelease = false;
                _wasRightMouseHeldLastFrame = true;
                return;
            }

            if (shiftHeld && leftMouseHeld && !_wasLeftMouseHeldLastFrame && !Main.LocalPlayer.mouseInterface)
            {
                RemoveZoneAtPosition(mouseTile);
                _wasLeftMouseHeldLastFrame = true;
                _wasRightMouseHeldLastFrame = rightMouseHeld;
                return;
            }

            if (leftMouseHeld)
            {
                if (!_wasLeftMouseHeldLastFrame)
                {
                    IsDragging = true;
                    DragStart = mouseTile;
                    DragEnd = mouseTile;
                }
                else if (IsDragging)
                {
                    DragEnd = mouseTile;
                }
            }
            else if (_wasLeftMouseHeldLastFrame && IsDragging)
            {
                SoundEngine.PlaySound(SoundID.Item4, Player.position);
                IsDragging = false;
                CommitZone();
            }

            _wasLeftMouseHeldLastFrame = leftMouseHeld;
            _wasRightMouseHeldLastFrame = rightMouseHeld;
        }

        private void CommitZone()
        {
            int x0 = Math.Min(DragStart.X, DragEnd.X);
            int x1 = Math.Max(DragStart.X, DragEnd.X);
            int y0 = Math.Min(DragStart.Y, DragEnd.Y);
            int y1 = Math.Max(DragStart.Y, DragEnd.Y);

            var topLeft = new Point16((short)x0, (short)y0);
            var bottomRight = new Point16((short)x1, (short)y1);
            if (Main.netMode == NetmodeID.SinglePlayer)
            {
                BiomeDowserPlacementMode requestedMode = PlacementMode;
                if (BiomeDowserSystem.TryCreateZone(topLeft, bottomRight, GetPreferredPreferences, out BiomeDowserZone zone, out string message))
                {
                    if (zone.PlacementMode != requestedMode)
                    {
                        Main.NewText(
                            $"{zone.PylonType} pylons don't support {BiomeDowserPlacementHelper.GetLabel(requestedMode)} placement. Using {BiomeDowserPlacementHelper.GetLabel(zone.PlacementMode)} instead.",
                            255,
                            215,
                            120);
                    }

                    Main.NewText(message, 255, 215, 120);
                }
                else
                {
                    Main.NewText(message, 255, 200, 80);
                }

                return;
            }

            DynamicWorldsNet.RequestBiomeZoneCreate(topLeft, bottomRight, GetNetworkPreferencesSnapshot());
        }

        private void RemoveZoneAtPosition(Point16 clickPos)
        {
            if (Main.netMode == NetmodeID.SinglePlayer)
            {
                if (BiomeDowserSystem.RemoveZoneAt(clickPos, out _, out string message))
                {
                    SoundEngine.PlaySound(SoundID.Item14, Player.position);
                    Main.NewText(message, 255, 180, 120);
                }
                else
                {
                    Main.NewText(message, 255, 220, 120);
                }

                return;
            }

            DynamicWorldsNet.RequestBiomeZoneRemoveAt(clickPos);
        }

        public void CancelDrag()
        {
            IsDragging = false;
            _wasLeftMouseHeldLastFrame = false;
            _wasRightMouseHeldLastFrame = false;
        }

        private void TogglePlacementMode()
        {
            PlacementMode = PlacementMode switch
            {
                BiomeDowserPlacementMode.Surface => BiomeDowserPlacementMode.Underground,
                BiomeDowserPlacementMode.Underground => BiomeDowserPlacementMode.Floating,
                _ => BiomeDowserPlacementMode.Surface,
            };

            Main.NewText(
                $"Biome Dowser now prefers {BiomeDowserPlacementHelper.GetLabel(PlacementMode)} placement when the selected pylon supports it.",
                255,
                220,
                120);
        }

        public BiomeDowserPylonPreferences GetPreferredPreferences(TeleportPylonType pylonType)
        {
            if (_preferences.TryGetValue(pylonType, out var prefs))
            {
                prefs.PlacementMode = BiomeDowserPlacementHelper.SanitizeForPylon(pylonType, prefs.PlacementMode);
                return prefs;
            }

            var fallback = BiomeDowserPylonPreferences.DefaultFor(pylonType);
            fallback.PlacementMode = BiomeDowserPlacementHelper.SanitizeForPylon(pylonType, PlacementMode);
            return fallback;
        }

        public BiomeDowserPlacementMode GetPreferredPlacementMode(TeleportPylonType pylonType)
        {
            return GetPreferredPreferences(pylonType).PlacementMode;
        }

        public void SetPreferredPlacementMode(TeleportPylonType pylonType, BiomeDowserPlacementMode mode)
        {
            var prefs = GetPreferredPreferences(pylonType);
            prefs.PlacementMode = mode;
            SetPreferredPreferences(pylonType, prefs);
        }

        public void SetPreferredPreferences(TeleportPylonType pylonType, BiomeDowserPylonPreferences prefs)
        {
            prefs.PlacementMode = BiomeDowserPlacementHelper.SanitizeForPylon(pylonType, prefs.PlacementMode);
            _preferences[pylonType] = prefs;
        }

        public Dictionary<TeleportPylonType, BiomeDowserPylonPreferences> GetNetworkPreferencesSnapshot()
        {
            var snapshot = new Dictionary<TeleportPylonType, BiomeDowserPylonPreferences>();
            TeleportPylonType[] pylonTypes =
            {
                TeleportPylonType.SurfacePurity,
                TeleportPylonType.Jungle,
                TeleportPylonType.Hallow,
                TeleportPylonType.Underground,
                TeleportPylonType.Desert,
                TeleportPylonType.Snow,
                TeleportPylonType.Beach,
                TeleportPylonType.GlowingMushroom,
                TeleportPylonType.Victory,
            };

            foreach (TeleportPylonType pylonType in pylonTypes)
                snapshot[pylonType] = GetPreferredPreferences(pylonType);

            return snapshot;
        }

        public override void SaveData(TagCompound tag)
        {
            if (_preferences.Count > 0)
            {
                var entries = new List<TagCompound>();
                foreach (var kv in _preferences)
                {
                    BiomeDowserPylonPreferences prefs = kv.Value;
                    entries.Add(new TagCompound
                    {
                        ["pylonType"] = (int)kv.Key,
                        ["mode"] = (int)prefs.PlacementMode,
                        ["floatOffset"] = prefs.FloatingYOffsetFromSurface,
                        ["undergroundOffset"] = prefs.UndergroundYOffsetFromSurface,
                        ["ocean"] = (int)prefs.OceanPlacement,
                        ["skyIsland"] = prefs.PreferSkyIslandSurface,
                        ["aether"] = prefs.PreferAetherCavern,
                    });
                }
                tag["dowserPlacementPrefs"] = entries;
            }
        }

        public override void LoadData(TagCompound tag)
        {
            _preferences.Clear();
            if (tag.TryGet("dowserPlacementPrefs", out List<TagCompound> list))
            {
                foreach (var entry in list)
                {
                    TeleportPylonType type = (TeleportPylonType)entry.GetInt("pylonType");
                    var prefs = BiomeDowserPylonPreferences.DefaultFor(type);
                    if (entry.ContainsKey("mode"))
                        prefs.PlacementMode = (BiomeDowserPlacementMode)entry.GetInt("mode");
                    if (entry.ContainsKey("floatOffset"))
                        prefs.FloatingYOffsetFromSurface = entry.GetInt("floatOffset");
                    if (entry.ContainsKey("undergroundOffset"))
                        prefs.UndergroundYOffsetFromSurface = entry.GetInt("undergroundOffset");
                    if (entry.ContainsKey("ocean"))
                        prefs.OceanPlacement = (BiomeDowserOceanPlacement)entry.GetInt("ocean");
                    if (entry.ContainsKey("skyIsland"))
                        prefs.PreferSkyIslandSurface = entry.GetBool("skyIsland");
                    if (entry.ContainsKey("aether"))
                        prefs.PreferAetherCavern = entry.GetBool("aether");

                    prefs.PlacementMode = BiomeDowserPlacementHelper.SanitizeForPylon(type, prefs.PlacementMode);
                    _preferences[type] = prefs;
                }
            }
        }
    }

    public class BiomeDowser : ModItem
    {
        public override string Texture => "DynamicWorlds/Preservation/BiomeDowser";

        public override void SetDefaults()
        {
            Item.width = 32;
            Item.height = 32;
            Item.useStyle = ItemUseStyleID.Shoot;
            Item.useTime = 12;
            Item.useAnimation = 18;
            Item.rare = ItemRarityID.LightRed;
            Item.value = Item.buyPrice(gold: 1);
            Item.maxStack = 1;
            Item.consumable = false;
            Item.noMelee = true;
            Item.noUseGraphic = false;
            Item.UseSound = SoundID.Item8;
        }

        public override bool CanUseItem(Player player) => true;
        public override bool ConsumeItem(Player player) => false;

        public override void ModifyTooltips(List<TooltipLine> tooltips)
        {
            tooltips.Add(new TooltipLine(Mod, "BDInfo1",
                "Left-click and drag to project a biome-aware pylon structure zone.")
                { OverrideColor = Color.Gold });
            tooltips.Add(new TooltipLine(Mod, "BDInfo2",
                "Each zone must fully contain exactly one vanilla pylon.")
                { OverrideColor = Color.LightGoldenrodYellow });
            tooltips.Add(new TooltipLine(Mod, "BDInfo3",
                "Only tiles connected to that pylon inside the selection are saved and moved.")
                { OverrideColor = Color.LightSkyBlue });
            tooltips.Add(new TooltipLine(Mod, "BDInfo4",
                "Open Biome Dowser settings to choose surface, underground, floating, sky, and ocean preferences per pylon.")
                { OverrideColor = Color.LightGoldenrodYellow });
            tooltips.Add(new TooltipLine(Mod, "BDInfo4b",
                "Right-click while holding to open the Biome Dowser settings panel.")
                { OverrideColor = Color.LightGoldenrodYellow });
            tooltips.Add(new TooltipLine(Mod, "BDInfo5",
                "On regen, the saved structure is rebuilt in a biome where that pylon can function.")
                { OverrideColor = Color.LightSkyBlue });
            tooltips.Add(new TooltipLine(Mod, "BDInfo6",
                "Biome Dowser zones cannot overlap structure zones or individually anchored tiles.")
                { OverrideColor = Color.Orange });
            tooltips.Add(new TooltipLine(Mod, "BDInfo7",
                "Shift+Click inside a Biome Dowser zone to remove it.")
                { OverrideColor = Color.LightBlue });
            tooltips.Add(new TooltipLine(Mod, "BDInfo8",
                "Hold any world tool to see anchors, erasures, structure zones, and Biome Dowser zones.")
                { OverrideColor = Color.LightSkyBlue });

            BiomeDowserPlayer modPlayer = Main.LocalPlayer?.GetModPlayer<BiomeDowserPlayer>();
            string modeLabel = BiomeDowserPlacementHelper.GetLabel(
                modPlayer?.PlacementMode ?? BiomeDowserPlacementMode.Surface);
            tooltips.Add(new TooltipLine(Mod, "BDInfoPreference",
                $"Current placement preference: {modeLabel}")
                { OverrideColor = Color.Gold });

            int zoneCount = BiomeDowserSystem.Zones.Count;
            if (zoneCount > 0)
            {
                tooltips.Add(new TooltipLine(Mod, "BDZoneCount",
                    $"World has {zoneCount} Biome Dowser zone{(zoneCount == 1 ? "" : "s")}")
                    { OverrideColor = Color.Gold });
            }
        }
    }
}
