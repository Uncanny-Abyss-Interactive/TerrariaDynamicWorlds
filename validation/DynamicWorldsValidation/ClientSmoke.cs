using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DynamicWorlds;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.IO;
using Terraria.ModLoader;

namespace DynamicWorldsValidation;

internal static class ClientFixture
{
    internal const string PlayerName = "DWValidation";
    internal const string WorldName = "DWValidation";

    // Called by the dedicated-server fixture only after its disposable-world guard passes.
    internal static void CreateDisposablePlayer(ValidationContext context)
    {
        if (!ValidationGuard.TryGet(out ValidationContext verified, out string reason) ||
            verified.RunId != context.RunId || verified.Root != context.Root)
            throw new InvalidOperationException("Player fixture guard failed: " + reason);

        string directory = Path.Combine(context.Root, "Players");
        string path = Path.Combine(directory, PlayerName + ".plr");
        if (Path.GetFullPath(Main.PlayerPath) != Path.GetFullPath(directory) ||
            new DirectoryInfo(directory).LinkTarget != null || new FileInfo(path).LinkTarget != null)
            throw new InvalidOperationException("The fixture player directory is not the disposable local save directory.");
        Directory.CreateDirectory(directory);
        if (File.Exists(path))
            throw new InvalidOperationException("The disposable player fixture already exists.");

        var player = new Player
        {
            name = PlayerName,
            difficulty = 0,
            statLife = 100,
            statLifeMax = 100,
            statMana = 20,
            statManaMax = 20,
            savedPerPlayerFieldsThatArentInThePlayerClass = new Player.SavedPlayerDataWithAnnoyingRules()
        };
        player.inventory[0].SetDefaults(ItemID.CopperShortsword);
        player.inventory[1].SetDefaults(ItemID.CopperPickaxe);
        player.inventory[2].SetDefaults(ItemID.CopperAxe);

        var data = new PlayerFileData(path, cloudSave: false)
        {
            Player = player,
            Metadata = FileMetadata.FromCurrentSettings(FileType.Player)
        };
        // This public serializer creates a normal encrypted .plr. SavePlayer additionally
        // touches client achievement/map state, which is unavailable on a dedicated server.
        // A fresh vanilla fixture has no .tplr data; tML initializes its ModPlayers on load.
        File.WriteAllBytes(path, Player.SavePlayerFile_Vanilla(data));
        if (new FileInfo(path).Length < 100)
            throw new InvalidOperationException("Disposable player serialization produced an empty file.");
    }
}

public sealed class ClientSmokeSystem : ModSystem
{
    private const string InitialSeed = "24681357";
    private static readonly string[] ToolNames =
    {
        "RealityAnchor", "RealityEraser", "StructureAnchorItem", "BiomeDowser"
    };

    private ValidationContext _context;
    private bool _guardChecked;
    private bool _complete;
    private bool _exitRequested;
    private bool _selectedTool;
    private bool _regenRequested;
    private bool _sawRegenBusy;
    private bool _regenReloaded;
    private bool _initialFixtureVerified;
    private string _initialSeed;
    private int _initialUpdates;
    private int _worldUnloadsAfterRequest;
    private int _worldLoadsAfterRequest;
    private int _updates;
    private int _renderedFrames;
    private Dictionary<string, bool> _toolGiftChecks = new();

    private static bool IsClientRun => !Main.dedServ &&
        Environment.GetEnvironmentVariable("DW_VALIDATION_MODE") == "client";

    private bool EnsureContext()
    {
        if (!IsClientRun)
            return false;
        if (!_guardChecked)
        {
            _guardChecked = true;
            if (!ValidationGuard.TryGet(out _context, out string reason,
                    requireWorld: false, requireServer: false))
            {
                _context = null;
                Mod.Logger.Warn("Client smoke test refused to run: " + reason);
            }
            else if (Path.GetFullPath(Main.SavePath) != _context.Root ||
                Path.GetFullPath(Main.PlayerPath) != Path.Combine(_context.Root, "Players") ||
                _context.ResultPath != Path.Combine(_context.Root, "client-result.json"))
            {
                _context = null;
                Mod.Logger.Warn("Client smoke test refused to run outside its disposable save and result paths.");
            }
        }
        return _context != null;
    }

    public override void PostUpdateEverything()
    {
        if (!EnsureContext())
            return;
        if (_exitRequested)
        {
            _exitRequested = false;
            Main.instance.Exit();
        }
    }

    // The same ModSystem survives SaveAndQuit, world generation and playWorld.
    // Keep the run state across these hooks so a second world entry cannot pass
    // without the production regeneration lifecycle having actually completed.
    public override void OnWorldUnload()
    {
        if (IsClientRun && _regenRequested)
            _worldUnloadsAfterRequest++;
    }

    public override void OnWorldLoad()
    {
        if (IsClientRun && _regenRequested)
            _worldLoadsAfterRequest++;
    }

    public override void PostUpdateWorld()
    {
        if (!EnsureContext() || _complete || Main.gameMenu)
            return;
        if (_regenRequested)
        {
            _sawRegenBusy |= DynamicWorldRegenSystem.IsBusy;
            if (DynamicWorldRegenSystem.IsBusy || WorldGen.gen)
                return;
        }
        if (!ValidateLoadedFixture(out string reason))
        {
            Complete(false, new Dictionary<string, bool> { ["disposable_fixture_loaded"] = false }, reason);
            return;
        }

        if (!_regenRequested)
        {
            _initialUpdates++;
            if (_initialUpdates >= 60)
                RequestRegeneration();
            return;
        }
        if (!_regenReloaded)
        {
            if (!_sawRegenBusy || _worldUnloadsAfterRequest == 0 || _worldLoadsAfterRequest == 0)
                return;
            if (Main.ActiveWorldFileData.SeedText != _context.ExpectedSeed.ToString())
            {
                Complete(false, new Dictionary<string, bool> { ["saved_regeneration_seed_reloaded"] = false },
                    "Production regeneration became idle after re-entry without the requested seed.");
                return;
            }
            _regenReloaded = true;
            Mod.Logger.Info("[DynamicWorldsValidation] Regenerated world re-entered; checking 120 gameplay updates and rendered frames.");
        }
        _updates++;
        if (!_selectedTool && ModLoader.TryGetMod("DynamicWorlds", out Mod target) &&
            target.TryFind("RealityAnchor", out ModItem anchor))
        {
            int slot = Array.FindIndex(Main.LocalPlayer.inventory,
                item => item != null && !item.IsAir && item.type == anchor.Type);
            if (slot >= 0 && slot < 10)
            {
                Main.LocalPlayer.selectedItem = slot;
                _selectedTool = true;
            }
        }
    }

    private void RequestRegeneration()
    {
        _toolGiftChecks = ToolGiftChecks.BeforeRegeneration();
        _initialSeed = Main.ActiveWorldFileData.SeedText;
        var checks = new Dictionary<string, bool>
        {
            ["initial_seed_matches"] = _initialSeed == InitialSeed && _initialSeed != _context.ExpectedSeed.ToString(),
            ["initial_structure_fixture_reloaded"] = FixtureAssertions.VerifySavedStructure(out string structureDetail),
            ["initial_unprotected_sentinel_present"] = !FixtureAssertions.VerifyUnprotectedSentinelRemoved(),
            ["singleplayer"] = Main.netMode == NetmodeID.SinglePlayer
        };
        foreach (var check in _toolGiftChecks)
            checks[check.Key] = check.Value;
        if (!checks.Values.All(value => value))
        {
            Complete(false, checks, "Initial saved fixtures are invalid: " + structureDetail);
            return;
        }
        _initialFixtureVerified = true;
        var config = ModContent.GetInstance<DynamicWorldsConfig>();
        config.PreserveEvilType = true;
        config.EnableRegenCounter = false;
        config.EnableScheduledMultiplayerRegen = false;
        _regenRequested = true;
        try
        {
            // This is the public entry point used by the real single-player command.
            // It performs SaveAndQuit, generation, restoration and world re-entry.
            SingleplayerRegenHelper.RegenerateWorldWithProgress(_context.ExpectedSeed.ToString());
            _sawRegenBusy |= DynamicWorldRegenSystem.IsBusy;
            if (!_sawRegenBusy)
            {
                checks["production_regeneration_started"] = false;
                Complete(false, checks, "The production regeneration entry point did not start its lifecycle.");
                return;
            }
            Mod.Logger.Info($"[DynamicWorldsValidation] Requested actual single-player regeneration from seed {_initialSeed} to {_context.ExpectedSeed}.");
        }
        catch (Exception ex)
        {
            checks["production_regeneration_started"] = false;
            Complete(false, checks, ex.ToString());
        }
    }

    public override void PostDrawInterface(SpriteBatch spriteBatch)
    {
        if (!EnsureContext() || _complete || Main.gameMenu || !_regenReloaded ||
            DynamicWorldRegenSystem.IsBusy || WorldGen.gen || _updates == 0)
            return;
        if (!ValidateLoadedFixture(out string reason))
        {
            Complete(false, new Dictionary<string, bool> { ["disposable_fixture_loaded"] = false }, reason);
            return;
        }

        _renderedFrames++;
        if (_renderedFrames < 120 || _updates < 120)
            return;

        var checks = new Dictionary<string, bool>
        {
            ["disposable_fixture_loaded"] = true,
            ["initial_seed_matches"] = _initialSeed == InitialSeed,
            ["initial_saved_fixtures_verified"] = _initialFixtureVerified,
            ["production_regeneration_requested"] = _regenRequested,
            ["production_regeneration_busy_observed"] = _sawRegenBusy,
            ["production_regeneration_idle"] = !DynamicWorldRegenSystem.IsBusy,
            ["world_unloaded_and_reloaded"] = _worldUnloadsAfterRequest > 0 && _worldLoadsAfterRequest > 0 && _regenReloaded,
            ["regeneration_seed_changed"] = Main.ActiveWorldFileData.SeedText != _initialSeed,
            ["gameplay_updates_120"] = _updates >= 120,
            ["rendered_gameplay_frames_120"] = _renderedFrames >= 120,
            ["singleplayer"] = Main.netMode == NetmodeID.SinglePlayer,
            ["player_alive"] = Main.LocalPlayer.active && !Main.LocalPlayer.dead,
            ["saved_regeneration_seed_reloaded"] = Main.ActiveWorldFileData.SeedText == _context.ExpectedSeed.ToString(),
            ["saved_boss_progression_reloaded"] = NPC.downedBoss1 && !NPC.downedBoss2 &&
                NPC.downedBoss3 && NPC.downedQueenBee && NPC.downedGoblins && !NPC.downedSlimeKing,
            ["saved_combat_book_reloaded"] = NPC.combatBookWasUsed,
            ["saved_difficulty_reloaded"] = !Main.hardMode && Main.GameMode == 1,
            ["saved_anchor_tile_reloaded"] = VerifyAnchoredTile(),
            ["graphics_device_ready"] = Main.instance.GraphicsDevice != null &&
                Main.instance.GraphicsDevice.PresentationParameters.BackBufferWidth > 0 &&
                Main.instance.GraphicsDevice.PresentationParameters.BackBufferHeight > 0
        };

        try
        {
            foreach (var check in MicrofixChecks.ClientChecks())
                checks[check.Key] = check.Value;
            foreach (var check in LoadingLayoutChecks.ClientChecks())
                checks[check.Key] = check.Value;
            foreach (var check in _toolGiftChecks)
                checks[check.Key] = check.Value;
            checks["microfix.tools_disabled_setting_survived_regeneration"] =
                !ModContent.GetInstance<DynamicWorldsConfig>().AutoGiveTools;
            int chestIndex = Chest.FindChest(250, 110);
            checks["saved_anchor_chest_reloaded"] = chestIndex >= 0 &&
                Main.chest[chestIndex]?.item[0]?.type == ItemID.Torch &&
                Main.chest[chestIndex].item[0].stack == 23 &&
                Main.chest[chestIndex].item[1].type == ItemID.IronPickaxe &&
                Main.chest[chestIndex].item[1].stack == 1 &&
                Main.chest[chestIndex].item[1].prefix == PrefixID.Light;
            checks["saved_structure_and_chest_reloaded"] = FixtureAssertions.VerifySavedStructure(out string structureDetail);
            checks["unprotected_sentinel_replaced"] = FixtureAssertions.VerifyUnprotectedSentinelRemoved();
            checks["dynamic_worlds_loaded"] = ModLoader.TryGetMod("DynamicWorlds", out Mod target);
            var toolDetails = new List<object>();
            foreach (string name in ToolNames)
            {
                bool registered = target != null && target.TryFind(name, out ModItem _);
                checks[name + "_registered"] = registered;
                if (!registered)
                    continue;
                ModItem definition = target.Find<ModItem>(name);
                bool inventoryPresent = Main.LocalPlayer.inventory.Any(item =>
                    item != null && !item.IsAir && item.type == definition.Type && item.stack > 0);
                checks[name + "_in_inventory"] = inventoryPresent;
                Main.instance.LoadItem(definition.Type);
                Texture2D texture = ModContent.Request<Texture2D>(definition.Texture,
                    AssetRequestMode.ImmediateLoad).Value;
                checks[name + "_texture_loaded"] = TextureAssets.Item[definition.Type].IsLoaded &&
                    texture != null && !texture.IsDisposed && texture.Width > 0 && texture.Height > 0;
                toolDetails.Add(new
                {
                    name,
                    type = definition.Type,
                    inventoryPresent,
                    texture = definition.Texture,
                    width = texture.Width,
                    height = texture.Height
                });
            }

            object screenshot = CaptureBackBuffer();
            Complete(checks.Values.All(value => value), checks,
                checks["saved_structure_and_chest_reloaded"] ? null : structureDetail, toolDetails, screenshot);
        }
        catch (Exception ex)
        {
            checks["client_asset_validation_completed"] = false;
            Complete(false, checks, ex.ToString());
        }
    }

    private static bool VerifyAnchoredTile()
    {
        Tile tile = Framing.GetTileSafely(240, 110);
        return tile.HasTile && tile.TileType == TileID.GoldBrick && tile.WallType == WallID.Stone &&
            tile.IsHalfBlock && tile.RedWire && tile.BlueWire && tile.GreenWire && tile.YellowWire &&
            tile.HasActuator && !tile.IsActuated && tile.LiquidAmount == 0;
    }

    private bool ValidateLoadedFixture(out string reason)
    {
        if (!ValidationGuard.TryGet(out ValidationContext verified, out reason,
                requireWorld: true, requireServer: false) || verified.RunId != _context.RunId)
            return false;
        if (Main.worldName != ClientFixture.WorldName || Main.ActiveWorldFileData.IsCloudSave)
        {
            reason = "The active world is not the local disposable fixture.";
            return false;
        }
        string expectedPlayer = Path.GetFullPath(Path.Combine(_context.Root, "Players", ClientFixture.PlayerName + ".plr"));
        PlayerFileData active = Main.ActivePlayerFileData;
        if (active == null || active.IsCloudSave ||
            Path.GetFullPath(active.Path) != expectedPlayer || new FileInfo(active.Path).LinkTarget != null ||
            new DirectoryInfo(Path.GetDirectoryName(active.Path)).LinkTarget != null ||
            Main.LocalPlayer.name != ClientFixture.PlayerName)
        {
            reason = "The active character is not the local disposable fixture.";
            return false;
        }
        return true;
    }

    private object CaptureBackBuffer()
    {
        string path = Path.Combine(_context.Root, "client.png");
        try
        {
            GraphicsDevice device = Main.instance.GraphicsDevice;
            int width = device.PresentationParameters.BackBufferWidth;
            int height = device.PresentationParameters.BackBufferHeight;
            var pixels = new Color[checked(width * height)];
            device.GetBackBufferData(pixels);
            using var texture = new Texture2D(device, width, height, false, SurfaceFormat.Color);
            texture.SetData(pixels);
            using (FileStream stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
                texture.SaveAsPng(stream, width, height);
            return new { captured = true, path, width, height };
        }
        catch (Exception ex)
        {
            // GPU readback availability is separate from whether gameplay rendered.
            // The caller may obtain a native-app screenshot when this backend cannot read back.
            return new { captured = false, path, error = ex.Message };
        }
    }

    private void Complete(bool passed, Dictionary<string, bool> checks, string error,
        object tools = null, object screenshot = null)
    {
        if (_complete)
            return;
        _complete = true;
        ValidationGuard.WriteResult(_context, new
        {
            schema_version = 1,
            phase = passed ? "passed" : "failed",
            success = passed,
            run_id = _context.RunId,
            mode = "client",
            checks,
            gameplayUpdates = _updates,
            renderedGameplayFrames = _renderedFrames,
            initialGameplayUpdates = _initialUpdates,
            initial_world_seed = _initialSeed,
            worldUnloadsDuringRegeneration = _worldUnloadsAfterRequest,
            worldLoadsDuringRegeneration = _worldLoadsAfterRequest,
            world_seed = Main.ActiveWorldFileData?.SeedText,
            expected_seed = _context.ExpectedSeed,
            player = Main.LocalPlayer.name,
            world = Main.worldName,
            tools,
            screenshot,
            error
        });
        Mod.Logger.Info($"[DynamicWorldsValidation] Client smoke {(passed ? "passed" : "failed")}; frames={_renderedFrames}, updates={_updates}");
        _exitRequested = true;
    }
}
