using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DynamicWorlds;
using Terraria;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace DynamicWorldsValidation;

internal static class SchedulerChecks
{
    // Invoke synchronously from the guarded, empty-server fixture callback.
    // Every synthetic dawn starts at day zero of seven. The overdue case disables
    // the multiplayer master switch, so even the old scheduler predicate cannot
    // pass the production queue's independent master-switch guard.
    internal static Dictionary<string, bool> ServerChecks()
    {
        if (!ValidationGuard.TryGet(out _, out string reason))
            throw new InvalidOperationException("Scheduler check guard failed: " + reason);
        if (Main.player.Any(player => player != null && player.active)
            || DynamicWorldRegenSystem.IsBusy || MultiplayerRegenSystem.IsBusy)
            throw new InvalidOperationException("Scheduler checks require an empty, idle disposable server.");

        var checks = new Dictionary<string, bool>();
        var scheduler = ModContent.GetInstance<WorldRegenScheduler>();
        var config = ModContent.GetInstance<DynamicWorldsConfig>();
        bool savedCounter = config.EnableRegenCounter;
        bool savedMaster = config.EnableMultiplayerRegen;
        bool savedScheduled = config.EnableScheduledMultiplayerRegen;
        int savedInterval = config.ScheduledRegenIntervalDays;
        DynamicWorldsScheduleMode savedMode = config.ScheduledRegenMode;
        bool savedDayTime = Main.dayTime;

        // LoadWorldData does not preserve every transient flag. Save all mutable
        // fields declared by this system, including its per-instance dawn trackers,
        // so the fixture leaves the real scheduler exactly as it found it.
        FieldInfo[] fields = typeof(WorldRegenScheduler).GetFields(
            BindingFlags.DeclaredOnly | BindingFlags.NonPublic | BindingFlags.Public
            | BindingFlags.Static | BindingFlags.Instance)
            .Where(field => !field.IsInitOnly && !field.IsLiteral).ToArray();
        object[] savedFields = fields.Select(field => field.GetValue(field.IsStatic ? null : scheduler)).ToArray();
        MethodInfo enabledForMode = typeof(WorldRegenScheduler).GetMethod(
            "IsSchedulerEnabledForCurrentMode", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("The production scheduler enable predicate was not found.");

        try
        {
            config.ScheduledRegenIntervalDays = 7;
            RunCase("both_disabled", master: false, scheduled: false, counter: true);
            RunCase("master_required", master: false, scheduled: true, counter: true);
            RunCase("scheduled_required", master: true, scheduled: false, counter: true);
            RunCase("both_enabled", master: true, scheduled: true, counter: true);
            RunCase("counter_required", master: true, scheduled: true, counter: false);
            config.EnableRegenCounter = true;
            config.EnableMultiplayerRegen = false;
            config.EnableScheduledMultiplayerRegen = true;
            config.ScheduledRegenMode = DynamicWorldsScheduleMode.RealWorldDays;
            scheduler.LoadWorldData(new TagCompound
            {
                ["daysSinceRegen"] = 0,
                ["lastWarnDay"] = -1,
                ["lastRegenCompletedUtcTicks"] = DateTime.UtcNow.AddDays(-8).Ticks,
                ["realWorldWarnedThreeDays"] = true,
                ["realWorldWarnedOneDay"] = false,
                ["realWorldWarnedOneHour"] = true
            });
            var overdueBefore = new TagCompound();
            scheduler.SaveWorldData(overdueBefore);
            for (int tick = 0; tick < 3; tick++)
                scheduler.PostUpdateEverything();
            var overdueAfter = new TagCompound();
            scheduler.SaveWorldData(overdueAfter);
            checks["microfix.scheduler_overdue_disabled_quiet"] = StatusMatches(expectedEnabled: false)
                && !(bool)enabledForMode.Invoke(null, null)
                && overdueAfter.GetBool("realWorldWarnedThreeDays") == overdueBefore.GetBool("realWorldWarnedThreeDays")
                && overdueAfter.GetBool("realWorldWarnedOneDay") == overdueBefore.GetBool("realWorldWarnedOneDay")
                && overdueAfter.GetBool("realWorldWarnedOneHour") == overdueBefore.GetBool("realWorldWarnedOneHour")
                && overdueAfter.GetLong("lastRegenCompletedUtcTicks") == overdueBefore.GetLong("lastRegenCompletedUtcTicks")
                && !DynamicWorldRegenSystem.IsBusy && !MultiplayerRegenSystem.IsBusy;
            checks["microfix.scheduler_never_queued_regen"] =
                !DynamicWorldRegenSystem.IsBusy && !MultiplayerRegenSystem.IsBusy;
        }
        finally
        {
            config.EnableRegenCounter = savedCounter;
            config.EnableMultiplayerRegen = savedMaster;
            config.EnableScheduledMultiplayerRegen = savedScheduled;
            config.ScheduledRegenIntervalDays = savedInterval;
            config.ScheduledRegenMode = savedMode;
            Main.dayTime = savedDayTime;
            for (int i = 0; i < fields.Length; i++)
                fields[i].SetValue(fields[i].IsStatic ? null : scheduler, savedFields[i]);
        }
        return checks;

        void RunCase(string name, bool master, bool scheduled, bool counter)
        {
            bool expectedEnabled = master && scheduled && counter;
            config.EnableMultiplayerRegen = master;
            config.EnableScheduledMultiplayerRegen = scheduled;
            config.EnableRegenCounter = counter;
            config.ScheduledRegenMode = DynamicWorldsScheduleMode.InGameDays;
            scheduler.LoadWorldData(new TagCompound
            {
                ["daysSinceRegen"] = 0,
                ["lastWarnDay"] = -1,
                ["lastRegenCompletedUtcTicks"] = DateTime.UtcNow.Ticks
            });
            Main.dayTime = false;
            scheduler.OnWorldLoad();
            Main.dayTime = true;
            scheduler.PostUpdateTime();
            var data = new TagCompound();
            scheduler.SaveWorldData(data);
            bool inGameCorrect = data.GetInt("daysSinceRegen") == (expectedEnabled ? 1 : 0)
                && (bool)enabledForMode.Invoke(null, null) == expectedEnabled
                && StatusMatches(expectedEnabled);

            config.ScheduledRegenMode = DynamicWorldsScheduleMode.RealWorldDays;
            scheduler.PostUpdateEverything();
            var realWorldData = new TagCompound();
            scheduler.SaveWorldData(realWorldData);
            bool realWorldCorrect = (bool)enabledForMode.Invoke(null, null) == expectedEnabled
                && StatusMatches(expectedEnabled)
                && !realWorldData.GetBool("realWorldWarnedThreeDays")
                && !realWorldData.GetBool("realWorldWarnedOneDay")
                && !realWorldData.GetBool("realWorldWarnedOneHour");
            checks["microfix.scheduler_" + name] = inGameCorrect && realWorldCorrect;
        }

        static bool StatusMatches(bool expectedEnabled)
        {
            string status = WorldRegenScheduler.GetStatusText();
            return expectedEnabled
                ? status.StartsWith("World regen in ", StringComparison.Ordinal)
                : status.Contains("disabled", StringComparison.OrdinalIgnoreCase);
        }
    }
}
