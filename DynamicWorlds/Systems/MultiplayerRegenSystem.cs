using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Chat;
using Terraria.ID;
using Terraria.IO;
using Terraria.Localization;
using Terraria.ModLoader;

namespace DynamicWorlds
{
    internal enum MultiplayerRegenStage
    {
        Countdown,
        WaitingForDisconnects,
        Regenerating
    }

    internal sealed class MultiplayerPlayerSpawnSnapshot
    {
        public string Name = string.Empty;
        public int SavedSpawnX = -1;
        public int SavedSpawnY = -1;
    }

    internal sealed class QueuedMultiplayerRegen
    {
        public string SeedOverride;
        public string RequestedBy = "Server Console";
        public MultiplayerRegenStage Stage = MultiplayerRegenStage.Countdown;
        public int CountdownTicksRemaining;
        public int DisconnectTimeoutTicksRemaining;
        public int LastAnnouncedSecond = int.MinValue;
        public List<MultiplayerPlayerSpawnSnapshot> PlayerSpawns = new();
    }

    internal sealed class PendingMultiplayerTeleport
    {
        public string PlayerName = string.Empty;
        public RegenExecutionResult Placement = new RegenExecutionResult();
        public int DelayTicks = 30;
    }

    public class MultiplayerRegenSystem : ModSystem
    {
        private static QueuedMultiplayerRegen _pending;
        private static readonly Dictionary<string, RegenExecutionResult> _pendingJoinPlacements =
            new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<int, PendingMultiplayerTeleport> _pendingTeleports = new();

        public static bool IsBusy => _pending != null;

        internal static bool QueueRegen(string seedOverride, CommandCaller caller, out string message)
        {
            string requestedBy = caller?.Player != null && caller.Player.active
                ? caller.Player.name
                : "Server Console";
            return TryQueueRegen(seedOverride, requestedBy, requireManualEnable: true, out message);
        }

        internal static bool QueueScheduledRegen(out string message)
        {
            return TryQueueRegen(null, "Scheduled Regen", requireManualEnable: false, out message);
        }

        internal static bool TryHandlePlayerEnterWorld(Player player)
        {
            if (Main.netMode != NetmodeID.Server || player == null || !player.active || string.IsNullOrWhiteSpace(player.name))
                return false;

            if (!_pendingJoinPlacements.TryGetValue(player.name, out RegenExecutionResult placement))
                return false;

            _pendingTeleports[player.whoAmI] = new PendingMultiplayerTeleport
            {
                PlayerName = player.name,
                Placement = placement,
                DelayTicks = 30
            };

            return true;
        }

        public override void OnWorldUnload()
        {
            _pending = null;
            _pendingTeleports.Clear();
            _pendingJoinPlacements.Clear();
        }

        public override void PostUpdateEverything()
        {
            if (Main.netMode != NetmodeID.Server)
                return;

            UpdatePendingRegen();
            UpdatePendingTeleports();
        }

        private static void UpdatePendingRegen()
        {
            if (_pending == null)
                return;

            switch (_pending.Stage)
            {
                case MultiplayerRegenStage.Countdown:
                    UpdateCountdown();
                    break;

                case MultiplayerRegenStage.WaitingForDisconnects:
                    UpdateDisconnectWait();
                    break;
            }
        }

        private static void UpdateCountdown()
        {
            if (_pending == null)
                return;

            if (_pending.CountdownTicksRemaining <= 0)
            {
                BeginDisconnectPhase();
                return;
            }

            int secondsRemaining = (_pending.CountdownTicksRemaining + 59) / 60;
            if (secondsRemaining != _pending.LastAnnouncedSecond && ShouldAnnounceCountdown(secondsRemaining))
            {
                _pending.LastAnnouncedSecond = secondsRemaining;
                BroadcastStatus(
                    $"Dynamic Worlds regen starts in {secondsRemaining} second{(secondsRemaining == 1 ? "" : "s")}. Please finish up and prepare to reconnect.",
                    new Color(255, 220, 120));
            }

            _pending.CountdownTicksRemaining--;
        }

        private static bool ShouldAnnounceCountdown(int secondsRemaining)
        {
            DynamicWorldsConfig config = ModContent.GetInstance<DynamicWorldsConfig>();
            if (!config.MultiplayerRegenAnnounceCountdown)
                return false;

            return secondsRemaining switch
            {
                <= 5 => true,
                10 => true,
                15 => true,
                30 => true,
                60 => true,
                _ => secondsRemaining == config.MultiplayerRegenCountdownSeconds
            };
        }

        private static void BeginDisconnectPhase()
        {
            if (_pending == null)
                return;

            _pending.Stage = MultiplayerRegenStage.WaitingForDisconnects;
            _pending.PlayerSpawns.Clear();

            for (int i = 0; i < Main.maxPlayers; i++)
            {
                Player player = Main.player[i];
                if (player == null || !player.active || string.IsNullOrWhiteSpace(player.name))
                    continue;

                _pending.PlayerSpawns.Add(new MultiplayerPlayerSpawnSnapshot
                {
                    Name = player.name,
                    SavedSpawnX = player.SpawnX,
                    SavedSpawnY = player.SpawnY
                });
            }

            string disconnectReason = "Dynamic Worlds is regenerating the world. Please reconnect in a moment.";
            BroadcastStatus("Disconnecting players so the server can regenerate the world...", new Color(255, 180, 120));

            for (int i = 0; i < Netplay.Clients.Length; i++)
            {
                RemoteClient client = Netplay.Clients[i];
                if (client != null && client.IsActive)
                    NetMessage.BootPlayer(i, NetworkText.FromLiteral(disconnectReason));
            }

            if (!HasActiveClients())
                ExecuteMultiplayerRegen();
        }

        private static void UpdateDisconnectWait()
        {
            if (_pending == null)
                return;

            if (!HasActiveClients())
            {
                ExecuteMultiplayerRegen();
                return;
            }

            _pending.DisconnectTimeoutTicksRemaining--;
            if (_pending.DisconnectTimeoutTicksRemaining > 0)
                return;

            int connectedCount = CountActiveClients();
            ModContent.GetInstance<DynamicWorlds>().Logger.Warn(
                $"[MultiplayerRegen] Cancelled queued regen because {connectedCount} client(s) were still connected after the timeout.");
            BroadcastStatus(
                "Dynamic Worlds regen was cancelled because some players did not disconnect in time.",
                new Color(255, 120, 120));
            _pending = null;
        }

        private static void ExecuteMultiplayerRegen()
        {
            if (_pending == null)
                return;

            QueuedMultiplayerRegen request = _pending;
            _pending = null;

            DynamicWorlds mod = ModContent.GetInstance<DynamicWorlds>();
            _pendingJoinPlacements.Clear();
            _pendingTeleports.Clear();

            try
            {
                mod.Logger.Info(
                    $"[MultiplayerRegen] Starting server regen requested by {request.RequestedBy} with seed override '{request.SeedOverride ?? "<random>"}'.");

                PendingRegenContext pending = SingleplayerRegenHelper.CreatePendingRegenContext(request.SeedOverride);
                if (pending == null)
                    throw new InvalidOperationException("Failed to create a pending multiplayer regen context.");

                WorldFile.SaveWorld();
                DynamicWorldRegenSystem.TryCreatePreRegenBackup(pending);

                if (Main.ActiveWorldFileData != null)
                    Main.ActiveWorldFileData.SetSeed(pending.NewSeed.ToString());

                WorldGen.GenerateWorld(pending.NewSeed, null);

                RegenExecutionResult executionResult = SingleplayerRegenHelper.ExecutePendingRegen(pending);
                int restoredPylons = PylonRestoreHelper.RestoreTrackedVanillaPylons(forceRefresh: true);

                foreach (MultiplayerPlayerSpawnSnapshot playerSpawn in request.PlayerSpawns)
                {
                    _pendingJoinPlacements[playerSpawn.Name] = SingleplayerRegenHelper.DeterminePlacementForSavedSpawn(
                        playerSpawn.SavedSpawnX,
                        playerSpawn.SavedSpawnY);
                }

                Netplay.ResetSections();
                WorldProgressUtil.SaveToFile();
                WorldFile.SaveWorld();

                mod.Logger.Info(
                    $"[MultiplayerRegen] Server regen complete. Respawned {executionResult.RespawnedNpcCount} NPC(s), restored {executionResult.RestoredHousingCount} home(s), and repaired {restoredPylons} pylon(s).");
            }
            catch (Exception ex)
            {
                mod.Logger.Error("[MultiplayerRegen] Multiplayer regen failed.", ex);
            }
        }

        private static void UpdatePendingTeleports()
        {
            if (_pendingTeleports.Count == 0)
                return;

            List<int> completed = new();

            foreach (KeyValuePair<int, PendingMultiplayerTeleport> kv in _pendingTeleports)
            {
                int whoAmI = kv.Key;
                PendingMultiplayerTeleport pendingTeleport = kv.Value;

                if (whoAmI < 0 || whoAmI >= Main.maxPlayers)
                {
                    completed.Add(whoAmI);
                    continue;
                }

                Player player = Main.player[whoAmI];
                if (player == null || !player.active || !string.Equals(player.name, pendingTeleport.PlayerName, StringComparison.OrdinalIgnoreCase))
                {
                    completed.Add(whoAmI);
                    continue;
                }

                if (pendingTeleport.DelayTicks > 0)
                {
                    pendingTeleport.DelayTicks--;
                    continue;
                }

                TeleportPlayerToRestoredSpawn(player, pendingTeleport.Placement);
                _pendingJoinPlacements.Remove(pendingTeleport.PlayerName);
                completed.Add(whoAmI);
            }

            foreach (int whoAmI in completed)
                _pendingTeleports.Remove(whoAmI);
        }

        private static void TeleportPlayerToRestoredSpawn(Player player, RegenExecutionResult placement)
        {
            Vector2 destination;
            string message;
            Color messageColor;

            if (placement.UsePersonalSpawn)
            {
                player.SpawnX = placement.SpawnTileX;
                player.SpawnY = placement.SpawnTileY;
                destination = new Vector2(placement.SpawnTileX * 16f, placement.SpawnTileY * 16f - 48f);
                message = "Your preserved bed survived the regen, so you were returned there.";
                messageColor = new Color(180, 255, 180);
            }
            else
            {
                player.SpawnX = -1;
                player.SpawnY = -1;
                destination = new Vector2(Main.spawnTileX * 16f, Main.spawnTileY * 16f - 48f);
                message = placement.HadSavedSpawn
                    ? "Your old bed setup could not be preserved, so you were returned to world spawn."
                    : "World regeneration finished. You were returned to world spawn.";
                messageColor = placement.HadSavedSpawn
                    ? new Color(255, 220, 120)
                    : new Color(180, 220, 255);
            }

            if (player.whoAmI >= 0 && player.whoAmI < Netplay.Clients.Length)
                RemoteClient.CheckSection(player.whoAmI, destination, 1);

            player.Teleport(destination, 1, 0);
            player.fallStart = (int)(player.position.Y / 16f);
            player.GetModPlayer<DynamicWorldsPlayer>().ClearSavedPosition();
            player.AddBuff(BuffID.Featherfall, 60 * 10);

            NetMessage.SendData(MessageID.TeleportEntity, -1, -1, null, 0, player.whoAmI, destination.X, destination.Y, 1, 0, 0);
            ChatHelper.SendChatMessageToClient(NetworkText.FromLiteral(message), messageColor, player.whoAmI);
        }

        private static bool HasActiveClients()
        {
            return CountActiveClients() > 0;
        }

        private static int CountActiveClients()
        {
            int count = 0;
            for (int i = 0; i < Netplay.Clients.Length; i++)
            {
                RemoteClient client = Netplay.Clients[i];
                if (client != null && client.IsActive)
                    count++;
            }

            return count;
        }

        private static void BroadcastStatus(string text, Color color)
        {
            if (Main.netMode == NetmodeID.Server)
                ChatHelper.BroadcastChatMessage(NetworkText.FromLiteral(text), color);

            ModContent.GetInstance<DynamicWorlds>().Logger.Info($"[MultiplayerRegen] {text}");
        }

        private static bool TryQueueRegen(string seedOverride, string requestedBy, bool requireManualEnable, out string message)
        {
            if (Main.netMode != NetmodeID.Server)
            {
                message = "Multiplayer regen can only run on a multiplayer server.";
                return false;
            }

            if (IsBusy || DynamicWorldRegenSystem.IsBusy)
            {
                message = "World regeneration is already in progress.";
                return false;
            }

            DynamicWorldsConfig config = ModContent.GetInstance<DynamicWorldsConfig>();
            if (!config.EnableMultiplayerRegen)
            {
                message = "Multiplayer regen is disabled in the Dynamic Worlds config.";
                return false;
            }

            if (!requireManualEnable && !config.EnableScheduledMultiplayerRegen)
            {
                message = "Scheduled multiplayer regen is disabled in the Dynamic Worlds config.";
                return false;
            }

            if (Main.npc.Any(n => n.active && n.boss))
            {
                message = "You cannot regenerate the world while a boss is alive.";
                return false;
            }

            int countdownSeconds = Math.Max(0, config.MultiplayerRegenCountdownSeconds);

            _pending = new QueuedMultiplayerRegen
            {
                SeedOverride = seedOverride,
                RequestedBy = requestedBy,
                Stage = MultiplayerRegenStage.Countdown,
                CountdownTicksRemaining = countdownSeconds * 60,
                DisconnectTimeoutTicksRemaining = Math.Max(5, config.MultiplayerRegenDisconnectTimeoutSeconds) * 60,
                LastAnnouncedSecond = int.MinValue
            };

            if (countdownSeconds > 0)
            {
                BroadcastStatus(
                    $"Dynamic Worlds regen requested by {requestedBy}. Server maintenance begins in {countdownSeconds} second{(countdownSeconds == 1 ? "" : "s")}.",
                    new Color(255, 220, 120));
                message = $"Queued multiplayer regen. Countdown: {countdownSeconds} second{(countdownSeconds == 1 ? "" : "s")}.";
            }
            else
            {
                BroadcastStatus(
                    $"Dynamic Worlds regen requested by {requestedBy}. Disconnecting players and regenerating now.",
                    new Color(255, 220, 120));
                message = "Queued multiplayer regen. Disconnecting players now.";
            }

            return true;
        }
    }
}
