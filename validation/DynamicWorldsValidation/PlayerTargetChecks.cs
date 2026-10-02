using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DynamicWorlds;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;

namespace DynamicWorldsValidation;

internal static class PlayerTargetChecks
{
    // Run the production resolver with a local roster. The live server network
    // thread deactivates disconnected Main.player slots concurrently, so synthetic
    // players must never be installed there. No items or network messages are sent.
    internal static Dictionary<string, bool> ServerChecks()
    {
        if (!ValidationGuard.TryGet(out _, out string reason))
            throw new InvalidOperationException("Player-target check guard failed: " + reason);
        if (Main.player.Any(player => player != null && player.active)
            || DynamicWorldRegenSystem.IsBusy || MultiplayerRegenSystem.IsBusy)
            throw new InvalidOperationException("Player-target checks require an empty, idle disposable server.");

        Type productionHelper = typeof(GiveToolsCommand).Assembly.GetType("DynamicWorlds.DynamicWorldsToolCommands")
            ?? throw new InvalidOperationException("The production tool-command helper was not found.");
        MethodInfo resolver = productionHelper.GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .Single(method => method.Name == "TryResolveTargetPlayer" && method.GetParameters().Length == 5);
        var checks = new Dictionary<string, bool>();
        var alex = new Player { name = "Alex", active = true, whoAmI = 0 };
        var alexander = new Player { name = "Alexander", active = true, whoAmI = 1 };
        var duplicate = new Player { name = "aLEX", active = false, whoAmI = 2 };
        var nameless = new Player { name = null, active = true, whoAmI = 3 };
        var console = new SilentCaller(null);
        Player[] roster = { alex, alexander, duplicate, nameless };
        checks["microfix.player_target_exact_before_prefix"] = Resolves(console, "Alex", alex);
        checks["microfix.player_target_case_whitespace"] = Resolves(console, "  aLeX  ", alex);
        checks["microfix.player_target_unique_prefix"] = Resolves(console, "Alexan", alexander);
        checks["microfix.player_target_ambiguous_prefix"] = Rejects(console, "Al", "Multiple players");
        checks["microfix.player_target_unknown_name"] = Rejects(console, "NobodyMatches", "No active player");

        alex.active = false;
        checks["microfix.player_target_inactive_ignored"] = Resolves(console, "Alex", alexander);
        alex.active = true;
        duplicate.active = true;
        checks["microfix.player_target_duplicate_exact_ambiguous"] = Rejects(console, "Alex", "Multiple players");
        duplicate.active = false;

        var caller = new SilentCaller(alex);
        checks["microfix.player_target_caller_fallback"] = Resolves(caller, null, alex)
            && Resolves(caller, "   ", alex);
        checks["microfix.player_target_console_requires_name"] =
            Rejects(console, null, "Console usage requires a target player name");
        return checks;

        bool Resolves(CommandCaller caller, string requestedName, Player expected)
        {
            object[] arguments = { roster, caller, requestedName, null, null };
            bool result = (bool)resolver.Invoke(null, arguments);
            bool passed = result && ReferenceEquals(arguments[3], expected) && arguments[4] == null;
            if (!passed)
                throw new InvalidOperationException($"Player target '{requestedName}' expected {expected.name}, " +
                    $"resolved={result}, target={(arguments[3] as Player)?.name ?? "<null>"}, " +
                    $"same={ReferenceEquals(arguments[3], expected)}, error={arguments[4] ?? "<null>"}.");
            return passed;
        }

        bool Rejects(CommandCaller caller, string requestedName, string expectedError)
        {
            object[] arguments = { roster, caller, requestedName, null, null };
            bool result = (bool)resolver.Invoke(null, arguments);
            return !result && arguments[3] == null && arguments[4] is string error
                && error.Contains(expectedError, StringComparison.OrdinalIgnoreCase);
        }
    }

    private sealed class SilentCaller : CommandCaller
    {
        internal SilentCaller(Player player) => Player = player;
        public Player Player { get; }
        public CommandType CommandType => Player == null
            ? Terraria.ModLoader.CommandType.Console : Terraria.ModLoader.CommandType.World;
        public void Reply(string text, Color color = default) =>
            throw new InvalidOperationException("Target resolution unexpectedly attempted to send a message.");
    }
}
