using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DynamicWorlds;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.IO;
using Terraria.ModLoader;

namespace DynamicWorldsValidation;

public sealed class DynamicWorldsValidationMod : Mod { }
internal sealed record ValidationCheck(string name, bool passed, string detail);

public sealed class ValidationCommand : ModCommand
{
    public override CommandType Type => CommandType.Console;
    public override string Command => "dwvalidate";
    public override string Usage => "dwvalidate begin|verify|status";
    public override string Description => "Runs isolated Dynamic Worlds validation fixtures (test mod only).";
    public override void Action(CommandCaller caller, string input, string[] args)
    {
        if (!ValidationGuard.TryGet(out _, out string reason, requireWorld: false)) { caller.Reply(reason); return; }
        if (args.Length != 1 || (args[0] != "begin" && args[0] != "verify" && args[0] != "status"))
        { caller.Reply(Usage); return; }
        ServerValidationSystem.Requests.Enqueue(args[0]);
        caller.Reply("Validation request queued on the world update thread: " + args[0]);
    }
}

public sealed class ServerValidationSystem : ModSystem
{
    internal static readonly ConcurrentQueue<string> Requests = new();
    private readonly List<ValidationCheck> _checks = new();
    private string _phase = "world_ready";
    private string _error;
    private bool _announced;
    private bool _sawBusy;
    private bool _verifyRequested;
    private int _threadId;
    private string _beforeSeed;
    private WorldProgressSnapshot _progress;
    private AnchoredTileData _anchor;
    private Point16 _anchorPos, _chestPos, _sentinelPos;
    private int _zoneId, _itemPrefix;
    private Point16 _zoneStart;
    private Dictionary<Point16, AnchoredTileData> _zonePattern;

    public override void Load()
    {
        Main.OnTickForThirdPartySoftwareOnly += OnIdleServerTick;
    }

    public override void Unload()
    {
        Main.OnTickForThirdPartySoftwareOnly -= OnIdleServerTick;
        while (Requests.TryDequeue(out _)) { }
    }

    private void OnIdleServerTick()
    {
        // Terraria skips world Update when the dedicated server has no clients. This event
        // still runs on its main loop. Advance only the harness: never fake production ticks.
        if (Main.dedServ && !Netplay.HasClients)
            PostUpdateEverything();
    }

    public override void PostUpdateEverything()
    {
        if (!ValidationGuard.TryGet(out var context, out _)) return;
        _threadId = Environment.CurrentManagedThreadId;
        if (!_announced) { _announced = true; Report(context); }
        if (_phase == "fixtures_ready" && MultiplayerRegenSystem.IsBusy) _sawBusy = true;
        while (Requests.TryDequeue(out string request))
        {
            try
            {
                // Recheck immediately before any action, including after a world transition.
                if (!ValidationGuard.TryGet(out context, out string reason)) throw new InvalidOperationException(reason);
                if (request == "begin") Begin(context);
                else if (request == "verify")
                {
                    if (_phase != "fixtures_ready") throw new InvalidOperationException("verify requires completed fixture setup.");
                    _verifyRequested = true;
                }
                Report(context);
            }
            catch (Exception ex)
            {
                _phase = "failed";
                _error = ex.ToString();
                Mod.Logger.Error("[DWValidation] Failed", ex);
                if (context != null) Report(context);
            }
        }
        if (_verifyRequested && _phase == "fixtures_ready" && !MultiplayerRegenSystem.IsBusy
            && Main.ActiveWorldFileData?.SeedText == context.ExpectedSeed.ToString())
        {
            try
            {
                _verifyRequested = false;
                Verify(context);
                Report(context);
            }
            catch (Exception ex)
            {
                _phase = "failed";
                _error = ex.ToString();
                Mod.Logger.Error("[DWValidation] Failed", ex);
                Report(context);
            }
        }
    }

    private void Check(string name, bool passed, string detail)
    {
        _checks.Add(new ValidationCheck(name, passed, detail));
        Mod.Logger.Info($"[DWValidation] {name}: {(passed ? "PASS" : "FAIL")} {detail}");
        if (!passed) throw new InvalidOperationException(name + ": " + detail);
    }

    private void Begin(ValidationContext context)
    {
        if (_phase != "world_ready") throw new InvalidOperationException("begin is accepted exactly once in a fresh validation server.");
        _phase = "running_checks";
        Report(context);
        Check("guard.isolated_world", true, Main.ActiveWorldFileData.Path);
        if (Main.player.Any(player => player != null && player.active)) throw new InvalidOperationException("Fixture creation requires an empty dedicated server.");
        _beforeSeed = Main.ActiveWorldFileData.SeedText;
        if (_beforeSeed == context.ExpectedSeed.ToString()) throw new InvalidOperationException("Regeneration seed must differ from initial seed.");

        foreach (var check in PacketDirectionChecks.ServerChecks())
            Check(check.Key, check.Value, "Production packet receive-direction regression.");
        foreach (var check in AnchorCapChecks.ServerChecks())
            Check(check.Key, check.Value, "Anchor cap configuration and admission regression.");
        foreach (var check in SchedulerChecks.ServerChecks())
            Check(check.Key, check.Value, "Multiplayer scheduler enablement regression.");

        // Use actual Terraria globals and the production capture/apply implementation.
        Main.hardMode = false;
        Main.GameMode = 1;
        NPC.downedBoss1 = true; NPC.downedBoss2 = false; NPC.downedBoss3 = true;
        NPC.downedQueenBee = true; NPC.downedGoblins = true; NPC.downedSlimeKing = false;
        NPC.combatBookWasUsed = true;
        WorldGen.SavedOreTiers.Copper = TileID.Tin;
        WorldGen.SavedOreTiers.Iron = TileID.Lead;
        _progress = WorldProgressUtil.Capture();
        NPC.downedBoss1 = false; NPC.downedBoss2 = true; NPC.downedBoss3 = false;
        NPC.downedQueenBee = false; NPC.downedGoblins = false; NPC.combatBookWasUsed = false;
        Main.GameMode = 0;
        WorldGen.SavedOreTiers.Copper = TileID.Copper;
        WorldGen.SavedOreTiers.Iron = TileID.Iron;
        WorldProgressUtil.Apply(_progress, preserveEvilType: true);
        Check("progress.capture_restore", ProgressMatches(), "Boss flags (true and false), event flag, consumable, difficulty, evil type and ore tiers round-trip.");

        _anchorPos = new Point16(240, 110);
        Tile tile = PutBlock(_anchorPos.X, _anchorPos.Y, TileID.GoldBrick);
        tile.WallType = WallID.Stone;
        tile.RedWire = true; tile.BlueWire = true; tile.GreenWire = true; tile.YellowWire = true;
        tile.HasActuator = true; tile.IsActuated = false; tile.IsHalfBlock = true;
        // Liquid is checked immediately; the long-lived regeneration fixture is dry to avoid simulation drift.
        tile.LiquidAmount = 73; tile.LiquidType = LiquidID.Honey;
        var data = AnchoredTileData.FromTag(AnchoredTileData.CaptureFromWorld(_anchorPos.X, _anchorPos.Y).ToTag());
        tile.ClearEverything(); data.RestoreToWorld();
        var roundTrip = AnchoredTileData.CaptureFromWorld(_anchorPos.X, _anchorPos.Y);
        Check("tile.snapshot_restore", TileMatches(data, roundTrip) && roundTrip.Liquid == 73 && roundTrip.LiquidType == LiquidID.Honey,
            "Tile, wall, half-block, wires, actuator and liquid survive capture/tag/restore.");
        tile.LiquidAmount = 0;
        AnchoredTileSystem.AnchorTile(_anchorPos.X, _anchorPos.Y);
        _anchor = AnchoredTileData.CaptureFromWorld(_anchorPos.X, _anchorPos.Y);

        _chestPos = new Point16(250, 110);
        int chest = CreateChest(_chestPos);
        SetChestItems(chest, 7);
        var saved = SavedChestContents.FromTag(SavedChestContents.CaptureFromWorld(_chestPos).ToTag());
        Main.chest[chest].item[0].TurnToAir(); Main.chest[chest].item[1].TurnToAir();
        saved.RestoreToWorld();
        Check("chest.snapshot_restore", ChestMatches(_chestPos, 7), "Container item type, stack and prefix survive capture/tag/restore.");
        for (int x = _chestPos.X; x <= _chestPos.X + 1; x++)
            for (int y = _chestPos.Y; y <= _chestPos.Y + 2; y++) AnchoredTileSystem.AnchorTile(x, y);
        // Changing items after anchoring exercises production pre-regen chest refresh.
        SetChestItems(chest, 23);

        _zoneStart = new Point16(Main.maxTilesX / 2 + 150, 100);
        for (int x = 0; x < 7; x++) for (int y = 0; y < 6; y++)
            Framing.GetTileSafely(_zoneStart.X + x, _zoneStart.Y + y).ClearEverything();
        for (int x = 0; x < 7; x++) PutBlock(_zoneStart.X + x, _zoneStart.Y + 4, TileID.BlueDungeonBrick);
        PutBlock(_zoneStart.X, _zoneStart.Y + 3, TileID.GoldBrick);
        PutBlock(_zoneStart.X + 6, _zoneStart.Y + 3, TileID.SilverBrick);
        PutBlock(_zoneStart.X + 3, _zoneStart.Y + 5, TileID.Stone);
        Point16 zoneChestPos = new(_zoneStart.X + 2, _zoneStart.Y + 2);
        int zoneChest = CreateChest(zoneChestPos);
        SetChestItems(zoneChest, 11);
        _zoneId = StructureAnchorSystem.NextId();
        var zone = BuildingZone.FromTag(BuildingZone.Capture(_zoneStart, new Point16(_zoneStart.X + 6, _zoneStart.Y + 4), _zoneId).ToTag());
        _zonePattern = new Dictionary<Point16, AnchoredTileData>(zone.Tiles);
        Framing.GetTileSafely(_zoneStart.X, _zoneStart.Y + 3).ClearEverything();
        Main.chest[zoneChest].item[0].TurnToAir();
        zone.RestoreToPlacement(new ZoneRestorePlacement(zone.TopLeft, zone.BottomRight, 0, 0, zone.SavedGroundY, true), "[DWValidation]");
        Check("structure.capture_restore", ZoneMatches(zone) && ChestMatches(zoneChestPos, 11), "Structure geometry and container survive capture/tag/restore.");
        StructureAnchorSystem.Zones[_zoneId] = zone;
        SetChestItems(Chest.FindChest(zoneChestPos.X, zoneChestPos.Y), 37);

        _sentinelPos = new Point16(270, 110);
        PutBlock(_sentinelPos.X, _sentinelPos.Y, TileID.RainbowBrick);
        var config = ModContent.GetInstance<DynamicWorldsConfig>();
        config.EnableMultiplayerRegen = true;
        config.MultiplayerRegenCountdownSeconds = 1;
        config.EnableScheduledMultiplayerRegen = false;
        config.EnableRegenCounter = false;
        config.PreserveEvilType = true;
        ClientFixture.CreateDisposablePlayer(context);
        Check("fixture.ready", AnchoredTileSystem.AnchoredTiles.Count >= 7 && StructureAnchorSystem.Zones.ContainsKey(_zoneId),
            "Protected tile, refreshed chest, movable structure and unprotected control are ready.");
        // Persist the fixture for the separate graphical process. This exercises real
        // .wld/.twld serialization, without advancing or faking the production server queue.
        WorldFile.SaveWorld();
        _phase = "fixtures_ready";
    }

    private void Verify(ValidationContext context)
    {
        if (_phase != "fixtures_ready") throw new InvalidOperationException("verify requires completed fixture setup and a real regenworld command.");
        _phase = "verifying_regen";
        Check("regen.command_completed", !MultiplayerRegenSystem.IsBusy && Main.ActiveWorldFileData.SeedText == context.ExpectedSeed.ToString()
            && Main.ActiveWorldFileData.SeedText != _beforeSeed, "Actual world seed changed to requested seed; regeneration queue is idle.");
        Check("regen.progress_preserved", ProgressMatches(), "Configured progression persisted through real world generation.");
        Check("regen.anchor_preserved", AnchoredTileSystem.AnchoredTiles.ContainsKey(_anchorPos)
            && TileMatches(_anchor, AnchoredTileData.CaptureFromWorld(_anchorPos.X, _anchorPos.Y)), "Exact anchored tile retained its location and state.");
        Check("regen.anchored_chest_preserved", ChestMatches(_chestPos, 23), "Anchored chest retained the latest item stack and prefix.");
        bool hasZone = StructureAnchorSystem.Zones.TryGetValue(_zoneId, out var zone);
        Check("regen.structure_preserved", hasZone && ZoneMatches(zone), "All structure cells match at their translated coordinates.");
        Check("regen.structure_chest_preserved", hasZone && ChestMatches(new Point16(zone.TopLeft.X + 2, zone.TopLeft.Y + 2), 37),
            "Moved structure chest retained latest contents.");
        Tile sentinel = Framing.GetTileSafely(_sentinelPos.X, _sentinelPos.Y);
        Check("regen.unprotected_tile_replaced", !sentinel.HasTile || sentinel.TileType != TileID.RainbowBrick,
            "Unprotected control tile was replaced, proving terrain generation occurred.");
        _phase = "passed";
    }

    private bool ProgressMatches()
    {
        var actual = WorldProgressUtil.Capture();
        return actual.downedBoss1 == _progress.downedBoss1 && actual.downedBoss2 == _progress.downedBoss2
            && actual.downedBoss3 == _progress.downedBoss3 && actual.downedQueenBee == _progress.downedQueenBee
            && actual.downedSlimeKing == _progress.downedSlimeKing && actual.downedGoblins == _progress.downedGoblins
            && actual.combatBookWasUsed == _progress.combatBookWasUsed && actual.hardMode == _progress.hardMode
            && actual.gameMode == _progress.gameMode && actual.crimson == _progress.crimson
            && actual.copperTier == _progress.copperTier && actual.ironTier == _progress.ironTier;
    }

    private static Tile PutBlock(int x, int y, ushort type)
    {
        Tile tile = Framing.GetTileSafely(x, y);
        tile.ClearEverything(); tile.HasTile = true; tile.TileType = type;
        return tile;
    }

    private int CreateChest(Point16 pos)
    {
        for (int dx = 0; dx < 2; dx++)
        {
            for (int dy = 0; dy < 2; dy++)
            {
                Tile tile = PutBlock(pos.X + dx, pos.Y + dy, TileID.Containers);
                tile.TileFrameX = (short)(dx * 18); tile.TileFrameY = (short)(dy * 18);
            }
            PutBlock(pos.X + dx, pos.Y + 2, TileID.BlueDungeonBrick);
        }
        int index = Chest.FindChest(pos.X, pos.Y);
        if (index < 0) index = Chest.CreateChest(pos.X, pos.Y);
        if (index < 0) throw new InvalidOperationException("Could not create fixture chest.");
        return index;
    }

    private void SetChestItems(int index, int count)
    {
        if (index < 0) throw new InvalidOperationException("Fixture chest disappeared.");
        Main.chest[index].item[0].SetDefaults(ItemID.Torch);
        Main.chest[index].item[0].stack = count;
        Main.chest[index].item[1].SetDefaults(ItemID.IronPickaxe);
        Main.chest[index].item[1].Prefix(PrefixID.Light);
        _itemPrefix = Main.chest[index].item[1].prefix;
    }

    private bool ChestMatches(Point16 pos, int count)
    {
        int index = Chest.FindChest(pos.X, pos.Y);
        return index >= 0 && Main.chest[index]?.item[0]?.type == ItemID.Torch
            && Main.chest[index].item[0].stack == count && Main.chest[index].item[1]?.type == ItemID.IronPickaxe
            && Main.chest[index].item[1].stack == 1 && Main.chest[index].item[1].prefix == _itemPrefix;
    }

    private bool ZoneMatches(BuildingZone zone)
    {
        if (zone.Width != 7 || zone.Height != 5 || zone.Tiles.Count != _zonePattern.Count) return false;
        foreach (var pair in _zonePattern)
        {
            int x = zone.TopLeft.X + pair.Key.X - _zoneStart.X;
            int y = zone.TopLeft.Y + pair.Key.Y - _zoneStart.Y;
            if (!TileMatches(pair.Value, AnchoredTileData.CaptureFromWorld(x, y))) return false;
        }
        return true;
    }

    private static bool TileMatches(AnchoredTileData a, AnchoredTileData b) =>
        a.Active == b.Active && (!a.Active || a.TileType == b.TileType) && a.WallType == b.WallType
        && a.HalfBlock == b.HalfBlock && a.Slope == b.Slope && a.WireRed == b.WireRed && a.WireBlue == b.WireBlue
        && a.WireGreen == b.WireGreen && a.WireYellow == b.WireYellow && a.HasActuator == b.HasActuator
        && a.IsActuated == b.IsActuated && a.Liquid == b.Liquid && (a.Liquid == 0 || a.LiquidType == b.LiquidType);

    private void Report(ValidationContext context) => ValidationGuard.WriteResult(context, new
    {
        schema_version = 1, run_id = context.RunId, phase = _phase, success = _phase == "passed" || _phase == "fixtures_ready",
        updated_at_utc = DateTimeOffset.UtcNow.ToString("O"), process_id = Environment.ProcessId,
        update_thread_id = _threadId, world_path = Main.ActiveWorldFileData?.Path,
        world_seed = Main.ActiveWorldFileData?.SeedText, initial_seed = _beforeSeed,
        expected_seed = context.ExpectedSeed, saw_regen_busy = _sawBusy,
        checks = _checks, error = _error
    });
}
