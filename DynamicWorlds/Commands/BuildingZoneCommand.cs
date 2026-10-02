using System;
using System.Collections.Generic;
using Terraria;
using Terraria.ModLoader;
using Microsoft.Xna.Framework;

namespace DynamicWorlds
{
	public class BuildingZoneCommand : ModCommand
	{
		public override CommandType Type => CommandType.World | CommandType.Console;

		public override string Command => "dwzone";

		public override string Usage => "/dwzone [list|clear|clearall] [id]";

			public override string Description => "Manage structure zones in the world.";

		public override void Action(CommandCaller caller, string input, string[] args)
		{
			if (args.Length == 0)
			{
				ListZones(caller);
				return;
			}

			string subcommand = args[0].ToLower();

			switch (subcommand)
			{
				case "list":
					ListZones(caller);
					break;

				case "clear":
				case "remove":
					if (args.Length < 2)
					{
						caller.Reply("Usage: /dwzone clear <zone_id>", Color.Yellow);
						return;
					}

					if (!DynamicWorldsPermissions.CanManageZones(caller, out string deniedReason))
					{
						DynamicWorldsPermissions.ReplyDenied(caller, deniedReason);
						return;
					}

					if (int.TryParse(args[1], out int zoneId))
					{
						RemoveZone(caller, zoneId);
					}
					else
					{
						caller.Reply($"Invalid zone ID: {args[1]}", Color.Red);
					}
					break;

				case "clearall":
					if (!DynamicWorldsPermissions.CanManageZones(caller, out string clearDeniedReason))
					{
						DynamicWorldsPermissions.ReplyDenied(caller, clearDeniedReason);
						return;
					}

					ClearAllZones(caller);
					break;

				default:
					caller.Reply($"Unknown subcommand: {subcommand}", Color.Red);
					caller.Reply($"Available: list, clear <id>, clearall", Color.Yellow);
					break;
			}
		}

		private void ListZones(CommandCaller caller)
		{
			if (StructureAnchorSystem.Zones.Count == 0)
			{
				caller.Reply("No structure zones in this world.", Color.LimeGreen);
				return;
			}

			caller.Reply($"Structure Zones ({StructureAnchorSystem.Zones.Count} total):", Color.DeepSkyBlue);
			foreach (var kv in StructureAnchorSystem.Zones)
			{
				var zone = kv.Value;
				int area = zone.Width * zone.Height;
				caller.Reply(
					$"  Zone #{kv.Key}: {zone.Width}×{zone.Height} ({area} tiles) at ({zone.TopLeft.X}, {zone.TopLeft.Y}) to ({zone.BottomRight.X}, {zone.BottomRight.Y})",
					Color.Cyan);
			}
		}

		private void RemoveZone(CommandCaller caller, int zoneId)
		{
			if (StructureAnchorSystem.Zones.TryGetValue(zoneId, out var zone))
			{
				StructureAnchorSystem.Zones.Remove(zoneId);
				caller.Reply($"Structure zone #{zoneId} removed. ({StructureAnchorSystem.Zones.Count} zones remain)", 
					new Color(255, 150, 100));
			}
			else
			{
				caller.Reply($"Zone #{zoneId} not found.", Color.Red);
			}
		}

		private void ClearAllZones(CommandCaller caller)
		{
			int count = StructureAnchorSystem.Zones.Count;
			if (count == 0)
			{
				caller.Reply("No zones to clear.", Color.Yellow);
				return;
			}

			StructureAnchorSystem.Zones.Clear();
			caller.Reply($"Cleared all {count} structure zone{(count == 1 ? "" : "s")}.", 
				new Color(255, 150, 100));
		}
	}
}
