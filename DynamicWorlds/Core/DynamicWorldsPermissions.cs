using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace DynamicWorlds
{
    internal static class DynamicWorldsPermissions
    {
        internal static void NormalizeTrustedPlayers(DynamicWorldsConfig config)
        {
            config.TrustedPlayers ??= new List<string>();

            HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
            List<string> normalized = new();
            foreach (string name in config.TrustedPlayers)
            {
                string trimmed = name?.Trim();
                if (string.IsNullOrWhiteSpace(trimmed) || !seen.Add(trimmed))
                    continue;

                normalized.Add(trimmed);
            }

            config.TrustedPlayers = normalized;
        }

        internal static bool IsTrustedPlayer(int whoAmI)
        {
            if (Main.netMode == NetmodeID.SinglePlayer)
                return true;

            if (whoAmI < 0 || whoAmI >= Main.maxPlayers)
                return false;

            Player player = Main.player[whoAmI];
            return player != null && player.active && IsTrustedPlayerName(player.name);
        }

        internal static bool IsTrustedPlayerName(string playerName)
        {
            if (Main.netMode == NetmodeID.SinglePlayer)
                return true;

            if (string.IsNullOrWhiteSpace(playerName))
                return false;

            DynamicWorldsConfig config = ModContent.GetInstance<DynamicWorldsConfig>();
            foreach (string trustedName in config.TrustedPlayers)
            {
                if (string.Equals(trustedName, playerName.Trim(), StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        internal static bool IsConsoleCaller(CommandCaller caller)
        {
            return caller != null && caller.CommandType.HasFlag(CommandType.Console);
        }

        internal static bool CanEditWorldTools(int whoAmI, out string reason)
        {
            if (IsWorldMutationLocked(out reason))
                return false;

            return CanPlayerUseConfiguredMode(
                whoAmI,
                ModContent.GetInstance<DynamicWorldsConfig>().WorldToolEditPermission,
                "World tool edits",
                out reason);
        }

        internal static bool CanManageZones(CommandCaller caller, out string reason)
        {
            if (IsWorldMutationLocked(out reason))
                return false;

            return CanCallerUseConfiguredMode(
                caller,
                ModContent.GetInstance<DynamicWorldsConfig>().ZoneCommandPermission,
                "Zone management",
                out reason);
        }

        internal static bool CanManageZones(int whoAmI, out string reason)
        {
            if (IsWorldMutationLocked(out reason))
                return false;

            return CanPlayerUseConfiguredMode(
                whoAmI,
                ModContent.GetInstance<DynamicWorldsConfig>().ZoneCommandPermission,
                "Zone management",
                out reason);
        }

        internal static bool CanUseToolCommands(CommandCaller caller, out string reason)
        {
            return CanCallerUseConfiguredMode(
                caller,
                ModContent.GetInstance<DynamicWorldsConfig>().ToolCommandPermission,
                "Tool commands",
                out reason);
        }

        internal static bool CanRunRegen(CommandCaller caller, out string reason)
        {
            if (DynamicWorldRegenSystem.IsBusy || MultiplayerRegenSystem.IsBusy)
            {
                reason = "World regeneration is already in progress.";
                return false;
            }

            return CanCallerUseConfiguredMode(
                caller,
                ModContent.GetInstance<DynamicWorldsConfig>().RegenCommandPermission,
                "World regeneration",
                out reason);
        }

        private static bool IsWorldMutationLocked(out string reason)
        {
            if (DynamicWorldRegenSystem.IsBusy || MultiplayerRegenSystem.IsBusy)
            {
                reason = "World regeneration is already in progress.";
                return true;
            }

            reason = null;
            return false;
        }

        internal static bool CanUseCheats(CommandCaller caller, out string reason)
        {
            DynamicWorldsConfig config = ModContent.GetInstance<DynamicWorldsConfig>();
            if (!config.AllowCheats)
            {
                reason = "Cheats are disabled. Enable 'Allow Cheats' in the mod config.";
                return false;
            }

            return CanCallerUseConfiguredMode(
                caller,
                config.CheatCommandPermission,
                "Cheat commands",
                out reason);
        }

        internal static bool CanUseCheats(Player player, out string reason)
        {
            reason = null;

            DynamicWorldsConfig config = ModContent.GetInstance<DynamicWorldsConfig>();
            if (!config.AllowCheats)
            {
                reason = "Cheats are disabled. Enable 'Allow Cheats' in the mod config.";
                return false;
            }

            if (Main.netMode == NetmodeID.SinglePlayer)
            {
                reason = null;
                return true;
            }

            if (player != null && player.active && CanPlayerUseConfiguredMode(
                    player.whoAmI,
                    config.CheatCommandPermission,
                    "Cheat actions",
                    out reason))
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(reason))
                reason = "Cheat actions are not allowed right now.";

            return false;
        }

        internal static bool CanConfigurePermissions(CommandCaller caller, out string reason)
        {
            if (Main.netMode == NetmodeID.SinglePlayer || IsConsoleCaller(caller))
            {
                reason = null;
                return true;
            }

            if (caller?.Player != null && caller.Player.active && IsTrustedPlayerName(caller.Player.name))
            {
                reason = null;
                return true;
            }

            reason = "Only the server console, single-player host, or a trusted player can change Dynamic Worlds multiplayer settings.";
            return false;
        }

        internal static bool CanPlayerUseConfiguredMode(
            int whoAmI,
            DynamicWorldsPermissionMode mode,
            string actionName,
            out string reason)
        {
            if (Main.netMode == NetmodeID.SinglePlayer)
            {
                reason = null;
                return true;
            }

            switch (mode)
            {
                case DynamicWorldsPermissionMode.Anyone:
                    reason = null;
                    return true;

                case DynamicWorldsPermissionMode.TrustedPlayers:
                    if (IsTrustedPlayer(whoAmI))
                    {
                        reason = null;
                        return true;
                    }

                    reason = $"{actionName} are restricted to trusted players.";
                    return false;

                case DynamicWorldsPermissionMode.ConsoleOnly:
                default:
                    reason = $"{actionName} are restricted to the server console.";
                    return false;
            }
        }

        internal static bool CanCallerUseConfiguredMode(
            CommandCaller caller,
            DynamicWorldsPermissionMode mode,
            string actionName,
            out string reason)
        {
            if (Main.netMode == NetmodeID.SinglePlayer)
            {
                reason = null;
                return true;
            }

            if (IsConsoleCaller(caller))
            {
                reason = null;
                return true;
            }

            if (caller?.Player == null)
            {
                reason = $"{actionName} are not available from this context.";
                return false;
            }

            return CanPlayerUseConfiguredMode(caller.Player.whoAmI, mode, actionName, out reason);
        }

        internal static void ReplyDenied(CommandCaller caller, string reason)
        {
            caller?.Reply(reason, new Color(255, 120, 120));
        }
    }
}
