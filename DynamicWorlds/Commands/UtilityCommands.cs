using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace DynamicWorlds
{
    public class RevealMapCommand : ModCommand
    {
        public override CommandType Type => CommandType.Chat;
        public override string Command => "revealmap";
        public override string Usage => "/revealmap";
        public override string Description =>
            "Reveals the entire world map for your current character.";

        public override void Action(CommandCaller caller, string input, string[] args)
        {
            if (!DynamicWorldsPermissions.CanUseCheats(caller, out string deniedReason))
            {
                DynamicWorldsPermissions.ReplyDenied(caller, deniedReason);
                return;
            }

            if (Main.Map == null)
            {
                caller.Reply("The world map is not ready yet.", new Color(255, 80, 80));
                return;
            }

            caller.Reply("Revealing the full map. This may take a moment...", new Color(180, 220, 255));

            const int blackEdgeWidth = 40;
            int minX = Math.Max(0, blackEdgeWidth);
            int maxX = Math.Max(minX, Main.maxTilesX - blackEdgeWidth);
            int minY = Math.Max(0, blackEdgeWidth);
            int maxY = Math.Max(minY, Main.maxTilesY - blackEdgeWidth);

            for (int x = minX; x < maxX; x++)
            {
                for (int y = minY; y < maxY; y++)
                    Main.Map.Update(x, y, byte.MaxValue);
            }

            Main.refreshMap = true;
            Main.updateMap = true;
            caller.Reply("Revealed the full map for this character.", new Color(150, 255, 150));
        }
    }

    public class KillDuplicateNPCsCommand : ModCommand
    {
        public override CommandType Type => CommandType.World | CommandType.Console;
        public override string Command => "killduplicatenpcs";
        public override string Usage => "/killduplicatenpcs";
        public override string Description =>
            "Removes all duplicate town NPCs, keeping only one of each type.";

        public override void Action(CommandCaller caller, string input, string[] args)
        {
            if (!DynamicWorldsPermissions.CanUseCheats(caller, out string deniedReason))
            {
                DynamicWorldsPermissions.ReplyDenied(caller, deniedReason);
                return;
            }

            var seenTypes = new HashSet<int>();
            var npcsToDie = new List<int>();

            // Find all duplicate NPCs (keep first occurrence of each type)
            for (int i = 0; i < Main.npc.Length; i++)
            {
                NPC npc = Main.npc[i];
                if (npc.active && npc.townNPC && npc.type > 0)
                {
                    if (!seenTypes.Add(npc.type))
                    {
                        // We've already seen this NPC type, so mark it for death
                        npcsToDie.Add(i);
                    }
                }
            }

            // Kill all duplicates
            foreach (int slot in npcsToDie)
            {
                Main.npc[slot].life = 0;
                Main.npc[slot].active = false;
                NetMessage.SendData(MessageID.SyncNPC, -1, -1, null, slot);
            }

            if (npcsToDie.Count == 0)
            {
                caller.Reply("No duplicate town NPCs found.", new Color(150, 200, 255));
            }
            else
            {
                caller.Reply($"Killed {npcsToDie.Count} duplicate town NPC{(npcsToDie.Count == 1 ? "" : "s")}. Kept {seenTypes.Count} unique type{(seenTypes.Count == 1 ? "" : "s")}.", new Color(255, 150, 100));
            }
        }
    }
}
