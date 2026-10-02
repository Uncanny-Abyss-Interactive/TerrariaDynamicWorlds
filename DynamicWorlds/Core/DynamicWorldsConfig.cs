using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Terraria;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.ModLoader.Config;
using System.Text.Json.Serialization;

namespace DynamicWorlds
{
	public enum DynamicWorldsPermissionMode
	{
		Anyone,
		TrustedPlayers,
		ConsoleOnly,
	}

	public enum DynamicWorldsScheduleMode
	{
		InGameDays,
		RealWorldDays,
	}

	public class DynamicWorldsConfig : ModConfig
	{
		public override ConfigScope Mode => ConfigScope.ServerSide;

		public bool EnableRegenCounter = false;

		[Range(1, 100)]
		public int ScheduledRegenIntervalDays = 7;

		public DynamicWorldsScheduleMode ScheduledRegenMode = DynamicWorldsScheduleMode.InGameDays;

		public bool AllowCheats = true;

		public bool RegenOnDeath = false;

		[DefaultValue(0)]
		[Range(0, 1_000_000)]
		public int AnchorTileCapOverride = 0;

		// Chat/debug logging
		public bool BiomeDowserRegenChatLog = false;

		public DynamicWorldsPermissionMode WorldToolEditPermission = DynamicWorldsPermissionMode.TrustedPlayers;

		public DynamicWorldsPermissionMode ZoneCommandPermission = DynamicWorldsPermissionMode.TrustedPlayers;

		public DynamicWorldsPermissionMode ToolCommandPermission = DynamicWorldsPermissionMode.Anyone;

		public DynamicWorldsPermissionMode RegenCommandPermission = DynamicWorldsPermissionMode.ConsoleOnly;

		public DynamicWorldsPermissionMode CheatCommandPermission = DynamicWorldsPermissionMode.ConsoleOnly;

		public List<string> TrustedPlayers = new();

		public bool EnableMultiplayerRegen = true;

		public bool EnableScheduledMultiplayerRegen = false;

		[Range(0, 300)]
		public int MultiplayerRegenCountdownSeconds = 15;

		[Range(5, 120)]
		public int MultiplayerRegenDisconnectTimeoutSeconds = 20;

		public bool MultiplayerRegenAnnounceCountdown = true;

		// ── World Generation Settings ─────────────────────────────────────
		public bool PreserveEvilType = true;

		[JsonIgnore]
		public bool PreserveDungeonSide = true;

		[JsonIgnore]
		public bool PreserveBiomeFeatures = true;

		public bool RandomizeSeedEachRegen = true;

		public override void OnLoaded()
		{
			DynamicWorldsPermissions.NormalizeTrustedPlayers(this);
		}

		public override void OnChanged()
		{
			DynamicWorldsPermissions.NormalizeTrustedPlayers(this);
		}

		public override ModConfig Clone()
		{
			DynamicWorldsConfig clone = (DynamicWorldsConfig)base.Clone();
			clone.TrustedPlayers = TrustedPlayers?.ToList() ?? new List<string>();
			return clone;
		}

		public override bool AcceptClientChanges(ModConfig pendingConfig, int whoAmI, ref NetworkText message)
		{
			if (Main.netMode == NetmodeID.SinglePlayer)
				return true;

			if (pendingConfig is DynamicWorldsConfig pending)
				DynamicWorldsPermissions.NormalizeTrustedPlayers(pending);

			if (DynamicWorldsPermissions.IsTrustedPlayer(whoAmI))
				return true;

			message = NetworkText.FromLiteral("Only trusted players can change Dynamic Worlds server config.");
			return false;
		}
	}
}
