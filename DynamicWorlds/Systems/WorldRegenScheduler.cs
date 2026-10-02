using System;
using System.Linq;
using Terraria;
using Terraria.Chat;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace DynamicWorlds
{
    /// <summary>
    /// Automatically triggers world regen on the configured in-game day interval.
    /// Broadcasts spooky countdown messages at 3 days, 1 day, and dawn of regen day.
    /// Persists the day counter in the world save so it survives restarts.
    /// Single-player can always use the scheduler when enabled. Multiplayer servers
    /// must also enable both multiplayer regen and scheduled multiplayer regen.
    /// </summary>
    public class WorldRegenScheduler : ModSystem
    {
        // Total in-game days elapsed since the last regen (or world creation).
        private static int _daysSinceRegen = 0;
        private static long _lastRegenCompletedUtcTicks = 0;

        // Countdown warning thresholds (days remaining).
        private static readonly int[] WarnAtDays = { 3, 1 };

        // Track the last day we warned about, so we only message once per day.
        private static int _lastWarnDay = -1;

        // Whether we've shown the "dawn of regen day" warning this cycle.
        private static bool _dawnWarningShown = false;
        private static bool _realWorldWarnedThreeDays = false;
        private static bool _realWorldWarnedOneDay = false;
        private static bool _realWorldWarnedOneHour = false;
        private static bool _realWorldWarnedOverdueBlocked = false;

        // ── Persistence ──────────────────────────────────────────────────

        public override void SaveWorldData(TagCompound tag)
        {
            tag["daysSinceRegen"]   = _daysSinceRegen;
            tag["lastWarnDay"]      = _lastWarnDay;
            tag["dawnWarningShown"] = _dawnWarningShown;
            tag["lastRegenCompletedUtcTicks"] = _lastRegenCompletedUtcTicks;
            tag["realWorldWarnedThreeDays"] = _realWorldWarnedThreeDays;
            tag["realWorldWarnedOneDay"] = _realWorldWarnedOneDay;
            tag["realWorldWarnedOneHour"] = _realWorldWarnedOneHour;
        }

        public override void LoadWorldData(TagCompound tag)
        {
            _daysSinceRegen   = tag.GetInt("daysSinceRegen");
            _lastWarnDay      = tag.ContainsKey("lastWarnDay") ? tag.GetInt("lastWarnDay") : -1;
            _dawnWarningShown = tag.ContainsKey("dawnWarningShown") && tag.GetBool("dawnWarningShown");
            _lastRegenCompletedUtcTicks = tag.ContainsKey("lastRegenCompletedUtcTicks")
                ? tag.GetLong("lastRegenCompletedUtcTicks")
                : 0L;
            _realWorldWarnedThreeDays = tag.ContainsKey("realWorldWarnedThreeDays") && tag.GetBool("realWorldWarnedThreeDays");
            _realWorldWarnedOneDay = tag.ContainsKey("realWorldWarnedOneDay") && tag.GetBool("realWorldWarnedOneDay");
            _realWorldWarnedOneHour = tag.ContainsKey("realWorldWarnedOneHour") && tag.GetBool("realWorldWarnedOneHour");
            _realWorldWarnedOverdueBlocked = false;
        }

        public override void OnWorldLoad()
        {
            // Reset transient state when entering a world.
            _lastWarnDay = -1;
            _wasNight = !Main.dayTime;
            _wasDay = Main.dayTime;
            _realWorldWarnedOverdueBlocked = false;

            if (_lastRegenCompletedUtcTicks <= 0)
                _lastRegenCompletedUtcTicks = DateTime.UtcNow.Ticks;
        }

        // ── Time tracking ─────────────────────────────────────────────────

        // PostUpdateTime fires every game tick after Main.time advances.
        // Dawn (start of a new day) is when Main.dayTime becomes true and
        // Main.time is near 0. We detect the transition by watching for the
        // moment the sun rises.
        private bool _wasNight = false;

        public override void PostUpdateTime()
        {
            if (!ShouldTrackTimeInCurrentMode())
                return;

            bool isNight = !Main.dayTime;

            if (!IsSchedulerEnabledForCurrentMode())
            {
                _wasNight = isNight;
                return;
            }

            // Detect dawn: we were in night, now it's daytime.
            if (_wasNight && !isNight)
                OnNewDay();

            _wasNight = isNight;
        }

        public override void PostUpdateEverything()
        {
            if (!ShouldTrackTimeInCurrentMode() || !IsSchedulerEnabledForCurrentMode())
                return;

            if (GetScheduleMode() != DynamicWorldsScheduleMode.RealWorldDays)
                return;

            UpdateRealWorldScheduler();
        }

        private void OnNewDay()
        {
            if (GetScheduleMode() != DynamicWorldsScheduleMode.InGameDays)
                return;

            int regenEveryDays = GetRegenEveryDays();
            _daysSinceRegen++;
            _dawnWarningShown = false; // reset for the new day

            int daysRemaining = regenEveryDays - _daysSinceRegen;

            // ── Countdown warnings ────────────────────────────────────────
            foreach (int warnDay in WarnAtDays)
            {
                if (daysRemaining == warnDay && _lastWarnDay != _daysSinceRegen)
                {
                    _lastWarnDay = _daysSinceRegen;
                    BroadcastCountdown(daysRemaining);
                    return;
                }
            }

            // ── Dawn-of-regen-day warning ─────────────────────────────────
            if (daysRemaining == 0 && !_dawnWarningShown)
            {
                _dawnWarningShown = true;
                Main.NewText("☠ The world stirs... regeneration begins at midnight. ☠", 200, 80, 255);
                return;
            }

            // ── Trigger regen at the next midnight (start of night) ───────
            // We do this by setting a flag and actually triggering at nightfall.
            // See PostUpdateTime → OnMidnight below. Nothing else to do here.
        }

        // Detect midnight (dayTime → night transition) to fire the regen.
        private bool _wasDay = false;

        public override void PreUpdateTime()
        {
            if (!ShouldTrackTimeInCurrentMode())
                return;

            bool isDay = Main.dayTime;

            if (!IsSchedulerEnabledForCurrentMode())
            {
                _wasDay = isDay;
                return;
            }

            // Detect midnight: we were in daytime, now it's night.
            if (_wasDay && !isDay)
                OnMidnight();

            _wasDay = isDay;
        }

        private void OnMidnight()
        {
            if (GetScheduleMode() != DynamicWorldsScheduleMode.InGameDays)
                return;

            int daysRemaining = GetRegenEveryDays() - _daysSinceRegen;

            if (daysRemaining <= 0)
            {
                TryStartScheduledRegen(
                    "☠ The world tears itself apart and is reborn... ☠",
                    "☠ Scheduled regen was delayed");
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────

        private static bool IsSchedulerEnabled()
        {
            return ModContent.GetInstance<DynamicWorldsConfig>().EnableRegenCounter;
        }

        private static DynamicWorldsScheduleMode GetScheduleMode()
        {
            return ModContent.GetInstance<DynamicWorldsConfig>().ScheduledRegenMode;
        }

        private static bool IsSchedulerEnabledForCurrentMode()
        {
            if (!IsSchedulerEnabled())
                return false;

            return Main.netMode != NetmodeID.Server || IsMultiplayerSchedulerEnabled();
        }

        private static bool IsMultiplayerSchedulerEnabled()
        {
            var config = ModContent.GetInstance<DynamicWorldsConfig>();
            return config.EnableMultiplayerRegen && config.EnableScheduledMultiplayerRegen;
        }

        private static bool ShouldTrackTimeInCurrentMode()
        {
            return Main.netMode switch
            {
                NetmodeID.SinglePlayer => true,
                NetmodeID.Server => true,
                _ => false
            };
        }

        private static int GetRegenEveryDays()
        {
            return Math.Max(1, ModContent.GetInstance<DynamicWorldsConfig>().ScheduledRegenIntervalDays);
        }

        private static void UpdateRealWorldScheduler()
        {
            if (_lastRegenCompletedUtcTicks <= 0)
            {
                _lastRegenCompletedUtcTicks = DateTime.UtcNow.Ticks;
                return;
            }

            DateTime lastRegenUtc = new DateTime(_lastRegenCompletedUtcTicks, DateTimeKind.Utc);
            TimeSpan interval = TimeSpan.FromDays(GetRegenEveryDays());
            TimeSpan elapsed = DateTime.UtcNow - lastRegenUtc;
            TimeSpan remaining = interval - elapsed;

            if (remaining <= TimeSpan.Zero)
            {
                if (DynamicWorldRegenSystem.IsBusy || MultiplayerRegenSystem.IsBusy)
                    return;

                if (Main.npc.Any(n => n.active && n.boss))
                {
                    if (!_realWorldWarnedOverdueBlocked)
                    {
                        _realWorldWarnedOverdueBlocked = true;
                        BroadcastMessage("☠ Scheduled real-world regen is overdue, but a boss is still alive. It will start once the fight is over. ☠");
                    }

                    return;
                }

                _realWorldWarnedOverdueBlocked = false;
                TryStartScheduledRegen(
                    "☠ The real-world timer has expired. The world tears itself apart and is reborn... ☠",
                    "☠ Scheduled real-world regen was delayed");
                return;
            }

            _realWorldWarnedOverdueBlocked = false;

            if (!_realWorldWarnedThreeDays && remaining <= TimeSpan.FromDays(3) && interval > TimeSpan.FromDays(3))
            {
                _realWorldWarnedThreeDays = true;
                BroadcastMessage("☠ The real-world timer is running low. The world will regenerate in about 3 days... ☠");
            }

            if (!_realWorldWarnedOneDay && remaining <= TimeSpan.FromDays(1))
            {
                _realWorldWarnedOneDay = true;
                BroadcastMessage("☠ The real-world timer is almost up. The world regenerates in about 1 day... ☠");
            }

            if (!_realWorldWarnedOneHour && remaining <= TimeSpan.FromHours(1))
            {
                _realWorldWarnedOneHour = true;
                BroadcastMessage("☠ The real-world timer is nearly expired. The world regenerates in about 1 hour... ☠");
            }
        }

        internal static void NotifyRegenCompleted()
        {
            _daysSinceRegen = 0;
            _lastWarnDay = -1;
            _dawnWarningShown = false;
            _realWorldWarnedThreeDays = false;
            _realWorldWarnedOneDay = false;
            _realWorldWarnedOneHour = false;
            _realWorldWarnedOverdueBlocked = false;
            _lastRegenCompletedUtcTicks = DateTime.UtcNow.Ticks;
        }

        private static bool TryStartScheduledRegen(string successMessage, string delayPrefix)
        {
            if (DynamicWorldRegenSystem.IsBusy || MultiplayerRegenSystem.IsBusy)
            {
                BroadcastMessage($"{delayPrefix} because another regen is already in progress. ☠");
                return false;
            }

            if (Main.npc.Any(n => n.active && n.boss))
            {
                BroadcastMessage($"{delayPrefix} because a boss is still alive. ☠");
                return false;
            }

            if (Main.netMode == NetmodeID.Server)
            {
                if (!IsMultiplayerSchedulerEnabled())
                    return false;

                if (!MultiplayerRegenSystem.QueueScheduledRegen(out string queueMessage))
                {
                    BroadcastMessage($"{delayPrefix}: {queueMessage} ☠");
                    return false;
                }
            }
            else
            {
                SingleplayerRegenHelper.RegenerateWorldWithProgress();
            }

            BroadcastMessage(successMessage);
            return true;
        }

        private static void BroadcastCountdown(int daysRemaining)
        {
            string msg = daysRemaining switch
            {
                3 => "☠ The ground trembles. The world will regenerate in 3 days... ☠",
                1 => "☠ Something wicked approaches. The world regenerates TOMORROW. ☠",
                _ => $"☠ The world regenerates in {daysRemaining} days. ☠"
            };

            BroadcastMessage(msg);
        }

        private static void BroadcastMessage(string text)
        {
            if (Main.netMode == NetmodeID.Server)
                ChatHelper.BroadcastChatMessage(NetworkText.FromLiteral(text), new Microsoft.Xna.Framework.Color(200, 80, 255));
            else
                Main.NewText(text, 200, 80, 255);
        }

        // ── Debug command ─────────────────────────────────────────────────

        /// <summary>Returns current scheduler state for the /snap command.</summary>
        public static string GetStatusText()
        {
            if (Main.netMode == NetmodeID.MultiplayerClient)
                return "Scheduled world regeneration is managed by the server.";

            int regenEveryDays = GetRegenEveryDays();

            if (!IsSchedulerEnabled())
            {
                if (GetScheduleMode() == DynamicWorldsScheduleMode.RealWorldDays)
                    return $"Scheduled world regeneration is disabled. Real-world timer is paused at an interval of {regenEveryDays} day(s).";

                return $"Scheduled world regeneration is disabled. Progress is paused at day {_daysSinceRegen}/{regenEveryDays}.";
            }

            if (Main.netMode == NetmodeID.Server && !IsMultiplayerSchedulerEnabled())
            {
                if (GetScheduleMode() == DynamicWorldsScheduleMode.RealWorldDays)
                    return $"Scheduled multiplayer regen is disabled. Real-world timer is paused at an interval of {regenEveryDays} day(s).";

                return $"Scheduled multiplayer regen is disabled. Progress is paused at day {_daysSinceRegen}/{regenEveryDays}.";
            }

            if (GetScheduleMode() == DynamicWorldsScheduleMode.RealWorldDays)
            {
                DateTime lastRegenUtc = _lastRegenCompletedUtcTicks > 0
                    ? new DateTime(_lastRegenCompletedUtcTicks, DateTimeKind.Utc)
                    : DateTime.UtcNow;
                DateTime nextRegenLocal = lastRegenUtc.AddDays(regenEveryDays).ToLocalTime();
                TimeSpan remaining = nextRegenLocal - DateTime.Now;
                if (remaining <= TimeSpan.Zero)
                    return $"World regen is due now (real-world schedule, every {regenEveryDays} day(s)).";

                return $"World regen in {FormatRealWorldRemaining(remaining)} (real-world schedule, every {regenEveryDays} day(s); next window {nextRegenLocal:g}).";
            }

            int daysRemaining = regenEveryDays - _daysSinceRegen;
            return $"World regen in {Math.Max(0, daysRemaining)} day(s) " +
                   $"(day {_daysSinceRegen}/{regenEveryDays})";
        }

        private static string FormatRealWorldRemaining(TimeSpan remaining)
        {
            if (remaining.TotalDays >= 2d)
                return $"{Math.Ceiling(remaining.TotalDays)} real day(s)";

            if (remaining.TotalHours >= 2d)
                return $"{Math.Ceiling(remaining.TotalHours)} hour(s)";

            if (remaining.TotalMinutes >= 2d)
                return $"{Math.Ceiling(remaining.TotalMinutes)} minute(s)";

            return "less than a minute";
        }
    }
}
