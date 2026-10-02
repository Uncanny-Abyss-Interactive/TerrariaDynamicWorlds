using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;

namespace DynamicWorlds
{
    internal static class DynamicWorldsToolCommands
    {
        private readonly record struct ToolGrant(int ItemType, string DisplayName);

        private static readonly Dictionary<string, ToolGrant> ToolAliases = new(StringComparer.OrdinalIgnoreCase)
        {
            ["anchor"] = new(ModContent.ItemType<RealityAnchor>(), "Reality Anchor"),
            ["realityanchor"] = new(ModContent.ItemType<RealityAnchor>(), "Reality Anchor"),
            ["eraser"] = new(ModContent.ItemType<RealityEraser>(), "Reality Eraser"),
            ["realityeraser"] = new(ModContent.ItemType<RealityEraser>(), "Reality Eraser"),
            ["structure"] = new(ModContent.ItemType<StructureAnchorItem>(), "Structure Anchor"),
            ["structureanchor"] = new(ModContent.ItemType<StructureAnchorItem>(), "Structure Anchor"),
            ["builder"] = new(ModContent.ItemType<StructureAnchorItem>(), "Structure Anchor"),
            ["dowser"] = new(ModContent.ItemType<BiomeDowser>(), "Biome Dowser"),
            ["biome"] = new(ModContent.ItemType<BiomeDowser>(), "Biome Dowser"),
            ["biomedowser"] = new(ModContent.ItemType<BiomeDowser>(), "Biome Dowser"),
            ["prefab"] = new(ModContent.ItemType<PrefabToolItem>(), "Prefab Tool"),
            ["prefabtool"] = new(ModContent.ItemType<PrefabToolItem>(), "Prefab Tool"),
        };

        internal static bool TryResolveTargetPlayer(CommandCaller caller, string requestedName, out Player target, out string error)
        {
            target = null;

            if (!string.IsNullOrWhiteSpace(requestedName))
            {
                string trimmed = requestedName.Trim();
                List<Player> matches = Main.player
                    .Where(player => player != null && player.active && player.name != null)
                    .Where(player =>
                        string.Equals(player.name, trimmed, StringComparison.OrdinalIgnoreCase) ||
                        player.name.StartsWith(trimmed, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (matches.Count == 1)
                {
                    target = matches[0];
                    error = null;
                    return true;
                }

                if (matches.Count > 1)
                {
                    error = $"Multiple players match '{trimmed}'. Please use the full player name.";
                    return false;
                }

                error = $"No active player matched '{trimmed}'.";
                return false;
            }

            if (caller.Player != null && caller.Player.active)
            {
                target = caller.Player;
                error = null;
                return true;
            }

            error = "Console usage requires a target player name.";
            return false;
        }

        internal static bool TryGiveNamedTool(Player target, string toolName, out string displayName, out string error)
        {
            displayName = null;

            if (!ToolAliases.TryGetValue(toolName ?? string.Empty, out ToolGrant tool))
            {
                error = $"Unknown tool '{toolName}'. Valid tools: anchor, eraser, structure, dowser, prefab, all.";
                return false;
            }

            target.QuickSpawnItem(target.GetSource_GiftOrReward(), tool.ItemType);
            displayName = tool.DisplayName;
            error = null;
            return true;
        }

        internal static void GiveAllTools(Player target)
        {
            foreach (int itemType in new[]
            {
                ModContent.ItemType<RealityAnchor>(),
                ModContent.ItemType<RealityEraser>(),
                ModContent.ItemType<StructureAnchorItem>(),
                ModContent.ItemType<BiomeDowser>(),
                ModContent.ItemType<PrefabToolItem>(),
            })
            {
                target.QuickSpawnItem(target.GetSource_GiftOrReward(), itemType);
            }
        }
    }

    public class GiveToolsCommand : ModCommand
    {
        public override CommandType Type => CommandType.World | CommandType.Console;

        public override string Command => "dwtools";

        public override string Usage => "/dwtools [player]";

        public override string Description => "Gives all Dynamic Worlds tools. Console can optionally target a player name.";

        public override void Action(CommandCaller caller, string input, string[] args)
        {
            if (!DynamicWorldsPermissions.CanUseToolCommands(caller, out string deniedReason))
            {
                DynamicWorldsPermissions.ReplyDenied(caller, deniedReason);
                return;
            }

            string targetName = args.Length > 0 ? string.Join(" ", args) : null;
            if (!DynamicWorldsToolCommands.TryResolveTargetPlayer(caller, targetName, out Player target, out string error))
            {
                caller.Reply(error, Color.Red);
                return;
            }

            DynamicWorldsToolCommands.GiveAllTools(target);
            caller.Reply(
                target == caller.Player
                    ? "Given all Dynamic Worlds tools."
                    : $"Given all Dynamic Worlds tools to {target.name}.",
                Color.LimeGreen);
        }
    }

    public class GiveSpecificToolCommand : ModCommand
    {
        public override CommandType Type => CommandType.World | CommandType.Console;

        public override string Command => "dwtool";

        public override string Usage => "/dwtool <anchor|eraser|structure|dowser|prefab|all> [player]";

        public override string Description => "Gives one Dynamic Worlds tool by name. Console can optionally target a player name.";

        public override void Action(CommandCaller caller, string input, string[] args)
        {
            if (!DynamicWorldsPermissions.CanUseToolCommands(caller, out string deniedReason))
            {
                DynamicWorldsPermissions.ReplyDenied(caller, deniedReason);
                return;
            }

            if (args.Length == 0)
            {
                caller.Reply("Usage: /dwtool <anchor|eraser|structure|dowser|prefab|all> [player]", Color.Yellow);
                return;
            }

            string toolName = args[0];
            string targetName = args.Length > 1 ? string.Join(" ", args.Skip(1)) : null;
            if (!DynamicWorldsToolCommands.TryResolveTargetPlayer(caller, targetName, out Player target, out string error))
            {
                caller.Reply(error, Color.Red);
                return;
            }

            if (string.Equals(toolName, "all", StringComparison.OrdinalIgnoreCase))
            {
                DynamicWorldsToolCommands.GiveAllTools(target);
                caller.Reply(
                    target == caller.Player
                        ? "Given all Dynamic Worlds tools."
                        : $"Given all Dynamic Worlds tools to {target.name}.",
                    Color.LimeGreen);
                return;
            }

            if (!DynamicWorldsToolCommands.TryGiveNamedTool(target, toolName, out string displayName, out error))
            {
                caller.Reply(error, Color.Red);
                return;
            }

            caller.Reply(
                target == caller.Player
                    ? $"Given {displayName}."
                    : $"Given {displayName} to {target.name}.",
                Color.LimeGreen);
        }
    }
}
