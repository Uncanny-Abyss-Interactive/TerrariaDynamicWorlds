using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Terraria.ModLoader;
using Terraria.ModLoader.Config;

namespace DynamicWorlds
{
    public class DynamicWorldsPermissionCommand : ModCommand
    {
        public override CommandType Type => CommandType.World | CommandType.Console;

        public override string Command => "dwperm";

        public override string Usage =>
            "/dwperm [show|tools|zones|give|regen|cheats|trusted <list|add|remove|clear> ...]";

        public override string Description =>
            "Shows or changes Dynamic Worlds multiplayer permission settings.";

        public override void Action(CommandCaller caller, string input, string[] args)
        {
            if (!DynamicWorldsPermissions.CanConfigurePermissions(caller, out string deniedReason))
            {
                DynamicWorldsPermissions.ReplyDenied(caller, deniedReason);
                return;
            }

            if (args.Length == 0 || Matches(args[0], "show", "list", "status"))
            {
                ReplyCurrentSettings(caller);
                return;
            }

            string subcommand = args[0].ToLowerInvariant();
            switch (subcommand)
            {
                case "tools":
                case "edits":
                case "worldtools":
                    UpdatePermission(caller, args, "world tool edits", config => config.WorldToolEditPermission = ParsePermissionMode(args, 1));
                    break;

                case "zones":
                case "zonecommands":
                    UpdatePermission(caller, args, "zone management", config => config.ZoneCommandPermission = ParsePermissionMode(args, 1));
                    break;

                case "give":
                case "toolcommands":
                    UpdatePermission(caller, args, "tool commands", config => config.ToolCommandPermission = ParsePermissionMode(args, 1));
                    break;

                case "regen":
                    UpdatePermission(caller, args, "regen commands", config => config.RegenCommandPermission = ParsePermissionMode(args, 1));
                    break;

                case "cheats":
                    UpdatePermission(caller, args, "cheat commands", config => config.CheatCommandPermission = ParsePermissionMode(args, 1));
                    break;

                case "trusted":
                    HandleTrustedPlayers(caller, args);
                    break;

                default:
                    caller.Reply("Usage: /dwperm [show|tools|zones|give|regen|cheats|trusted <list|add|remove|clear> ...]", Color.Yellow);
                    break;
            }
        }

        private static void ReplyCurrentSettings(CommandCaller caller)
        {
            DynamicWorldsConfig config = ModContent.GetInstance<DynamicWorldsConfig>();
            caller.Reply("Dynamic Worlds permission settings:", Color.DeepSkyBlue);
            caller.Reply($"  World tool edits: {FormatMode(config.WorldToolEditPermission)}", Color.Cyan);
            caller.Reply($"  Zone management: {FormatMode(config.ZoneCommandPermission)}", Color.Cyan);
            caller.Reply($"  Tool commands: {FormatMode(config.ToolCommandPermission)}", Color.Cyan);
            caller.Reply($"  Regen commands: {FormatMode(config.RegenCommandPermission)}", Color.Cyan);
            caller.Reply($"  Cheat commands: {FormatMode(config.CheatCommandPermission)}", Color.Cyan);
            caller.Reply(
                config.TrustedPlayers.Count == 0
                    ? "  Trusted players: none"
                    : $"  Trusted players: {string.Join(", ", config.TrustedPlayers)}",
                Color.LightGreen);
        }

        private static void HandleTrustedPlayers(CommandCaller caller, string[] args)
        {
            if (args.Length == 1 || Matches(args[1], "list", "show"))
            {
                ReplyCurrentSettings(caller);
                return;
            }

            string action = args[1].ToLowerInvariant();
            switch (action)
            {
                case "add":
                    if (args.Length < 3)
                    {
                        caller.Reply("Usage: /dwperm trusted add <player name>", Color.Yellow);
                        return;
                    }

                    SaveUpdatedConfig(caller, pending =>
                    {
                        pending.TrustedPlayers ??= new List<string>();
                        pending.TrustedPlayers.Add(string.Join(" ", args.Skip(2)).Trim());
                    }, $"Added {string.Join(" ", args.Skip(2)).Trim()} to trusted players.");
                    break;

                case "remove":
                case "delete":
                    if (args.Length < 3)
                    {
                        caller.Reply("Usage: /dwperm trusted remove <player name>", Color.Yellow);
                        return;
                    }

                    string targetName = string.Join(" ", args.Skip(2)).Trim();
                    SaveUpdatedConfig(caller, pending =>
                    {
                        pending.TrustedPlayers = pending.TrustedPlayers
                            .Where(name => !string.Equals(name, targetName, StringComparison.OrdinalIgnoreCase))
                            .ToList();
                    }, $"Removed {targetName} from trusted players.");
                    break;

                case "clear":
                    SaveUpdatedConfig(caller, pending => pending.TrustedPlayers.Clear(), "Cleared the trusted player list.");
                    break;

                default:
                    caller.Reply("Usage: /dwperm trusted <list|add|remove|clear> ...", Color.Yellow);
                    break;
            }
        }

        private static void UpdatePermission(
            CommandCaller caller,
            string[] args,
            string label,
            Action<DynamicWorldsConfig> applyChange)
        {
            if (args.Length < 2)
            {
                caller.Reply($"Usage: /dwperm {args[0]} <anyone|trusted|console>", Color.Yellow);
                return;
            }

            DynamicWorldsPermissionMode mode;
            try
            {
                mode = ParsePermissionMode(args, 1);
            }
            catch (ArgumentException ex)
            {
                caller.Reply(ex.Message, Color.Red);
                return;
            }

            SaveUpdatedConfig(caller, applyChange, $"Set {label} permission to {FormatMode(mode)}.");
        }

        private static void SaveUpdatedConfig(CommandCaller caller, Action<DynamicWorldsConfig> mutate, string successMessage)
        {
            DynamicWorldsConfig activeConfig = ModContent.GetInstance<DynamicWorldsConfig>();
            DynamicWorldsConfig pending = (DynamicWorldsConfig)ConfigManager.GeneratePopulatedClone(activeConfig);
            mutate(pending);
            DynamicWorldsPermissions.NormalizeTrustedPlayers(pending);

            ConfigSaveResult result = activeConfig.SaveChanges(
                pending,
                (text, color) => caller.Reply(text, color),
                silent: true,
                broadcast: false);

            switch (result)
            {
                case ConfigSaveResult.Success:
                    caller.Reply(successMessage, Color.LimeGreen);
                    break;

                case ConfigSaveResult.NeedsReload:
                    caller.Reply("That config change requires a reload and was not applied.", Color.OrangeRed);
                    break;

                case ConfigSaveResult.RequestSentToServer:
                    caller.Reply("Sent the config change request to the server.", Color.LightBlue);
                    break;
            }
        }

        private static DynamicWorldsPermissionMode ParsePermissionMode(string[] args, int index)
        {
            string raw = args[index].ToLowerInvariant();
            return raw switch
            {
                "any" or "anyone" or "all" => DynamicWorldsPermissionMode.Anyone,
                "trusted" or "trustedplayers" or "trustedonly" => DynamicWorldsPermissionMode.TrustedPlayers,
                "console" or "consoleonly" or "server" => DynamicWorldsPermissionMode.ConsoleOnly,
                _ => throw new ArgumentException("Permission mode must be one of: anyone, trusted, console."),
            };
        }

        private static bool Matches(string value, params string[] options)
        {
            foreach (string option in options)
            {
                if (string.Equals(value, option, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static string FormatMode(DynamicWorldsPermissionMode mode)
        {
            return mode switch
            {
                DynamicWorldsPermissionMode.Anyone => "Anyone",
                DynamicWorldsPermissionMode.TrustedPlayers => "Trusted Players",
                DynamicWorldsPermissionMode.ConsoleOnly => "Console Only",
                _ => mode.ToString(),
            };
        }
    }
}
