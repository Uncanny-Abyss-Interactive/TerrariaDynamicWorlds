using System;
using Microsoft.Xna.Framework;
using Terraria.ModLoader;
using Terraria.ModLoader.Config;

namespace DynamicWorlds
{
    public class MultiplayerRegenConfigCommand : ModCommand
    {
        public override CommandType Type => CommandType.World | CommandType.Console;

        public override string Command => "dwregenconfig";

        public override string Usage =>
            "/dwregenconfig [show|scheduler <on|off>|interval <days>|clock <ingame|real>|enable <on|off>|schedule <on|off>|countdown <seconds>|timeout <seconds>|announce <on|off>]";

        public override string Description =>
            "Shows or changes Dynamic Worlds multiplayer regen server settings.";

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

            switch (args[0].ToLowerInvariant())
            {
                case "enable":
                    UpdateBoolSetting(caller, args, "multiplayer regen", value => value.EnableMultiplayerRegen = ParseBoolArg(args, 1));
                    break;

                case "scheduler":
                case "scheduledregen":
                    UpdateBoolSetting(caller, args, "scheduled regen", value => value.EnableRegenCounter = ParseBoolArg(args, 1));
                    break;

                case "interval":
                case "days":
                    UpdateIntSetting(caller, args, 1, 1, 100, "scheduled regen interval", value => value.ScheduledRegenIntervalDays = ParseIntArg(args, 1, 1, 100));
                    break;

                case "clock":
                case "mode":
                    UpdateScheduleMode(caller, args);
                    break;

                case "schedule":
                case "scheduled":
                    UpdateBoolSetting(caller, args, "scheduled multiplayer regen", value => value.EnableScheduledMultiplayerRegen = ParseBoolArg(args, 1));
                    break;

                case "countdown":
                    UpdateIntSetting(caller, args, 1, 0, 300, "multiplayer regen countdown", value => value.MultiplayerRegenCountdownSeconds = ParseIntArg(args, 1, 0, 300));
                    break;

                case "timeout":
                case "disconnecttimeout":
                    UpdateIntSetting(caller, args, 1, 5, 120, "multiplayer disconnect timeout", value => value.MultiplayerRegenDisconnectTimeoutSeconds = ParseIntArg(args, 1, 5, 120));
                    break;

                case "announce":
                    UpdateBoolSetting(caller, args, "multiplayer countdown announcements", value => value.MultiplayerRegenAnnounceCountdown = ParseBoolArg(args, 1));
                    break;

                default:
                    caller.Reply("Usage: /dwregenconfig [show|scheduler <on|off>|interval <days>|clock <ingame|real>|enable <on|off>|schedule <on|off>|countdown <seconds>|timeout <seconds>|announce <on|off>]", Color.Yellow);
                    break;
            }
        }

        private static void ReplyCurrentSettings(CommandCaller caller)
        {
            DynamicWorldsConfig config = ModContent.GetInstance<DynamicWorldsConfig>();
            caller.Reply("Dynamic Worlds multiplayer regen settings:", Color.DeepSkyBlue);
            caller.Reply($"  Scheduler: {(config.EnableRegenCounter ? "On" : "Off")}", Color.Cyan);
            caller.Reply($"  Scheduler clock: {FormatScheduleMode(config.ScheduledRegenMode)}", Color.Cyan);
            caller.Reply($"  Scheduler interval: {config.ScheduledRegenIntervalDays} day(s)", Color.Cyan);
            caller.Reply($"  Enabled: {(config.EnableMultiplayerRegen ? "On" : "Off")}", Color.Cyan);
            caller.Reply($"  Scheduled regen: {(config.EnableScheduledMultiplayerRegen ? "On" : "Off")}", Color.Cyan);
            caller.Reply($"  Countdown: {config.MultiplayerRegenCountdownSeconds} second(s)", Color.Cyan);
            caller.Reply($"  Disconnect timeout: {config.MultiplayerRegenDisconnectTimeoutSeconds} second(s)", Color.Cyan);
            caller.Reply($"  Countdown announcements: {(config.MultiplayerRegenAnnounceCountdown ? "On" : "Off")}", Color.Cyan);
            caller.Reply($"  Status: {WorldRegenScheduler.GetStatusText()}", Color.LightGreen);
        }

        private static void UpdateBoolSetting(CommandCaller caller, string[] args, string label, Action<DynamicWorldsConfig> mutate)
        {
            if (args.Length < 2)
            {
                caller.Reply($"Usage: /dwregenconfig {args[0]} <on|off>", Color.Yellow);
                return;
            }

            try
            {
                bool enabled = ParseBoolArg(args, 1);
                SaveUpdatedConfig(caller, mutate, $"Set {label} to {(enabled ? "On" : "Off")}.");
            }
            catch (ArgumentException ex)
            {
                caller.Reply(ex.Message, Color.Red);
            }
        }

        private static void UpdateIntSetting(
            CommandCaller caller,
            string[] args,
            int valueIndex,
            int min,
            int max,
            string label,
            Action<DynamicWorldsConfig> mutate)
        {
            if (args.Length <= valueIndex)
            {
                caller.Reply($"Usage: /dwregenconfig {args[0]} <{min}-{max}>", Color.Yellow);
                return;
            }

            try
            {
                int value = ParseIntArg(args, valueIndex, min, max);
                SaveUpdatedConfig(caller, mutate, $"Set {label} to {value}.");
            }
            catch (ArgumentException ex)
            {
                caller.Reply(ex.Message, Color.Red);
            }
        }

        private static void UpdateScheduleMode(CommandCaller caller, string[] args)
        {
            if (args.Length < 2)
            {
                caller.Reply("Usage: /dwregenconfig clock <ingame|real>", Color.Yellow);
                return;
            }

            try
            {
                DynamicWorldsScheduleMode mode = ParseScheduleMode(args[1]);
                SaveUpdatedConfig(caller, pending => pending.ScheduledRegenMode = mode, $"Set scheduled regen clock to {FormatScheduleMode(mode)}.");
            }
            catch (ArgumentException ex)
            {
                caller.Reply(ex.Message, Color.Red);
            }
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

        private static bool ParseBoolArg(string[] args, int index)
        {
            return args[index].ToLowerInvariant() switch
            {
                "on" or "true" or "yes" or "enable" or "enabled" => true,
                "off" or "false" or "no" or "disable" or "disabled" => false,
                _ => throw new ArgumentException("Value must be one of: on, off."),
            };
        }

        private static int ParseIntArg(string[] args, int index, int min, int max)
        {
            if (!int.TryParse(args[index], out int value) || value < min || value > max)
                throw new ArgumentException($"Value must be a whole number between {min} and {max}.");

            return value;
        }

        private static DynamicWorldsScheduleMode ParseScheduleMode(string raw)
        {
            return raw.ToLowerInvariant() switch
            {
                "ingame" or "in-game" or "game" => DynamicWorldsScheduleMode.InGameDays,
                "real" or "realworld" or "real-world" or "irl" => DynamicWorldsScheduleMode.RealWorldDays,
                _ => throw new ArgumentException("Schedule clock must be one of: ingame, real."),
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

        private static string FormatScheduleMode(DynamicWorldsScheduleMode mode)
        {
            return mode switch
            {
                DynamicWorldsScheduleMode.InGameDays => "In-Game Days",
                DynamicWorldsScheduleMode.RealWorldDays => "Real-World Days",
                _ => mode.ToString(),
            };
        }
    }
}
