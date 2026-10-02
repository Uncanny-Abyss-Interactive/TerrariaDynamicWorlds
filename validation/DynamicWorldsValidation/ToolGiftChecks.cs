using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using DynamicWorlds;
using Microsoft.Xna.Framework;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.Config;

namespace DynamicWorldsValidation;

// Call exactly once at the start of ClientSmokeSystem.RequestRegeneration, before
// checking its initial fixture conditions. Retain/merge this returned dictionary into
// both that precondition report and the final client report. On success the real
// disposable config is saved with AutoGiveTools=false, so the following production
// save/quit/regenerate/reload lifecycle exercises the disabled branch too.
internal static class ToolGiftChecks
{
    internal static Dictionary<string, bool> BeforeRegeneration()
    {
        if (!ValidationGuard.TryGet(out var context, out string reason, requireServer: false)
            || Main.dedServ || Main.netMode != NetmodeID.SinglePlayer
            || Environment.GetEnvironmentVariable("DW_VALIDATION_MODE") != "client"
            || DynamicWorldRegenSystem.IsBusy || MultiplayerRegenSystem.IsBusy)
            throw new InvalidOperationException("Tool-gift checks require the idle disposable single-player fixture: " + reason);

        Player player = Main.LocalPlayer;
        if (player == null || !player.active)
            throw new InvalidOperationException("No active fixture player for tool-gift checks.");
        var modPlayer = player.GetModPlayer<DynamicWorldsPlayer>();
        var config = ModContent.GetInstance<DynamicWorldsConfig>();
        var checks = new Dictionary<string, bool>();
        int[] automaticTypes =
        {
            ModContent.ItemType<RealityAnchor>(), ModContent.ItemType<RealityEraser>(),
            ModContent.ItemType<StructureAnchorItem>(), ModContent.ItemType<BiomeDowser>()
        };
        int prefabType = ModContent.ItemType<PrefabToolItem>();
        int[] manualTypes = automaticTypes.Append(prefabType).ToArray();
        Item[] originalInventory = player.inventory;
        Item[] originalWorldItems = Main.item;
        bool originalAutoGive = config.AutoGiveTools;
        Vector2 originalPosition = player.position;
        Vector2 originalVelocity = player.velocity;
        int originalFallStart = player.fallStart;
        int originalSpawnX = player.SpawnX, originalSpawnY = player.SpawnY;
        FieldInfo savedX = typeof(DynamicWorldsPlayer).GetField("_savedTileX", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(nameof(DynamicWorldsPlayer), "_savedTileX");
        FieldInfo savedY = typeof(DynamicWorldsPlayer).GetField("_savedTileY", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(nameof(DynamicWorldsPlayer), "_savedTileY");
        object originalSavedX = savedX.GetValue(modPlayer), originalSavedY = savedY.GetValue(modPlayer);

        string configDirectory = Path.GetFullPath(ConfigManager.ModConfigPath);
        string expectedDirectory = Path.Combine(context.Root, "ModConfigs");
        if (configDirectory != expectedDirectory || new DirectoryInfo(configDirectory).LinkTarget != null)
            throw new InvalidOperationException("Actual config manager path is not the disposable ModConfigs directory.");
        string configPath = Path.Combine(configDirectory, config.Mod.Name + "_" + config.Name + ".json");
        if (new FileInfo(configPath).LinkTarget != null)
            throw new InvalidOperationException("Disposable config file may not be a symbolic link.");
        bool configFileExisted = File.Exists(configPath);
        byte[] originalConfigBytes = configFileExisted ? File.ReadAllBytes(configPath) : null;
        bool attemptedConfigSave = false;
        bool leaveDisabledForRegeneration = false;
        try
        {
            checks["microfix.tools_fresh_config_enabled"] = originalAutoGive;
            var legacy = (DynamicWorldsConfig)config.Clone();
            legacy.AutoGiveTools = false;
            JsonConvert.PopulateObject("{\"AllowCheats\":true}", legacy, ConfigManager.serializerSettings);
            checks["microfix.tools_legacy_config_defaults_enabled"] = legacy.AutoGiveTools;

            ResetScratchItems();
            modPlayer.ClearSavedPosition(); // Repeated entry must not consume/teleport to the real fixture's saved position.
            config.AutoGiveTools = true;
            modPlayer.OnEnterWorld();
            checks["microfix.tools_missing_auto_gifts_all_four"] = automaticTypes.All(type => Total(type) == 1)
                && Total(prefabType) == 0;

            // QuickSpawnItem creates world drops. Measure the actual gifts above, then
            // move those exact emitted instances into inventory as setup for re-entry.
            // This is not a replacement implementation of gifting or a pickup test.
            CollectEmittedGifts();
            bool inventoryHasFour = automaticTypes.All(type => InventoryCount(type) == 1);
            modPlayer.OnEnterWorld();
            checks["microfix.tools_repeat_entry_does_not_duplicate"] = inventoryHasFour
                && automaticTypes.All(type => InventoryCount(type) == 1 && Total(type) == 1)
                && Total(prefabType) == 0;

            config.AutoGiveTools = false;
            Item[] retainedItems = player.inventory.ToArray();
            modPlayer.OnEnterWorld();
            checks["microfix.tools_disabled_keeps_existing_tools"] = player.inventory
                .Select((item, index) => ReferenceEquals(item, retainedItems[index])).All(value => value)
                && automaticTypes.All(type => InventoryCount(type) == 1 && Total(type) == 1);

            ResetScratchItems();
            modPlayer.OnEnterWorld();
            checks["microfix.tools_disabled_creates_no_inventory_gifts"] = manualTypes.All(type => InventoryCount(type) == 0);
            checks["microfix.tools_disabled_creates_no_dropped_gifts"] = !Main.item.Any(item => item != null && item.active && !item.IsAir);

            var caller = new FixtureCaller(player);
            new GiveToolsCommand().Action(caller, "/dwtools", Array.Empty<string>());
            checks["microfix.tools_disabled_manual_command_gives_five"] = !config.AutoGiveTools
                && manualTypes.All(type => Total(type) == 1)
                && caller.Messages.Contains("Given all Dynamic Worlds tools.");

            // Use production file Save/Load, not a hand-rolled config encoding. This also
            // leaves an explicit false on disk for the actual world re-entry that follows.
            MethodInfo save = ConfigMethod("Save");
            MethodInfo load = ConfigMethod("Load");
            attemptedConfigSave = true;
            save.Invoke(null, new object[] { config });
            JObject savedJson = JObject.Parse(File.ReadAllText(configPath));
            var reloaded = (DynamicWorldsConfig)config.Clone();
            reloaded.AutoGiveTools = true;
            load.Invoke(null, new object[] { reloaded });
            checks["microfix.tools_explicit_false_survives_config_save_reload"] =
                savedJson.TryGetValue(nameof(DynamicWorldsConfig.AutoGiveTools), out JToken stored)
                && stored.Type == JTokenType.Boolean && !stored.Value<bool>() && !reloaded.AutoGiveTools;
            checks["microfix.tools_checks_completed"] = true;
            leaveDisabledForRegeneration = checks.Values.All(value => value);
        }
        catch (Exception ex)
        {
            checks["microfix.tools_checks_completed"] = false;
            ModContent.GetInstance<global::DynamicWorlds.DynamicWorlds>().Logger.Error("Tool-gift regression checks failed.", ex);
        }
        finally
        {
            // The original arrays/items were never mutated: all gifts and removals happened
            // in scratch arrays, so the graphical fixture keeps its original usable tools.
            player.inventory = originalInventory;
            Main.item = originalWorldItems;
            player.position = originalPosition;
            player.velocity = originalVelocity;
            player.fallStart = originalFallStart;
            player.SpawnX = originalSpawnX;
            player.SpawnY = originalSpawnY;
            savedX.SetValue(modPlayer, originalSavedX);
            savedY.SetValue(modPlayer, originalSavedY);
            config.AutoGiveTools = leaveDisabledForRegeneration ? false : originalAutoGive;
            if (!leaveDisabledForRegeneration && attemptedConfigSave)
            {
                if (configFileExisted) File.WriteAllBytes(configPath, originalConfigBytes);
                else if (File.Exists(configPath)) File.Delete(configPath);
            }
        }
        checks["microfix.tools_fixture_inventory_restored"] = ReferenceEquals(player.inventory, originalInventory)
            && ReferenceEquals(Main.item, originalWorldItems)
            && player.position == originalPosition && player.velocity == originalVelocity
            && player.fallStart == originalFallStart && player.SpawnX == originalSpawnX && player.SpawnY == originalSpawnY
            && Equals(savedX.GetValue(modPlayer), originalSavedX) && Equals(savedY.GetValue(modPlayer), originalSavedY);
        checks["microfix.tools_disabled_for_real_regeneration"] = leaveDisabledForRegeneration && !config.AutoGiveTools;
        return checks;

        void ResetScratchItems()
        {
            player.inventory = Enumerable.Range(0, originalInventory.Length).Select(_ => new Item()).ToArray();
            Main.item = Enumerable.Range(0, originalWorldItems.Length).Select(index => new Item { whoAmI = index }).ToArray();
        }

        int InventoryCount(int type) => player.inventory.Where(item => item != null && !item.IsAir && item.type == type)
            .Sum(item => item.stack);
        int Total(int type) => InventoryCount(type) + Main.item.Where(item => item != null && item.active && !item.IsAir && item.type == type)
            .Sum(item => item.stack);

        void CollectEmittedGifts()
        {
            for (int index = 0; index < Main.item.Length; index++)
            {
                Item item = Main.item[index];
                if (item == null || !item.active || item.IsAir || !automaticTypes.Contains(item.type)) continue;
                int slot = Array.FindIndex(player.inventory, 0, Math.Min(50, player.inventory.Length), candidate => candidate == null || candidate.IsAir);
                if (slot < 0) throw new InvalidOperationException("Scratch inventory cannot hold emitted gifts.");
                player.inventory[slot] = item.Clone();
                Main.item[index] = new Item { whoAmI = index };
            }
        }
    }

    private static MethodInfo ConfigMethod(string name) => typeof(ConfigManager).GetMethod(name,
        BindingFlags.Static | BindingFlags.NonPublic, binder: null, types: new[] { typeof(ModConfig) }, modifiers: null)
        ?? throw new MissingMethodException(typeof(ConfigManager).FullName, name);

    private sealed class FixtureCaller : CommandCaller
    {
        internal FixtureCaller(Player player) => Player = player;
        public Player Player { get; }
        public CommandType CommandType => Terraria.ModLoader.CommandType.World;
        internal readonly List<string> Messages = new();
        public void Reply(string text, Color color = default) => Messages.Add(text);
    }
}
