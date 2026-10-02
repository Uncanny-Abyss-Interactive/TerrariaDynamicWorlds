using System;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace DynamicWorlds
{
    public class HardmodeCommand : ModCommand
    {
        public override CommandType Type => CommandType.World | CommandType.Console;
        public override string Command => "hardmode";
        public override string Usage => "/hardmode [on|off]";
        public override string Description =>
            "Toggle Hardmode for this world (single-player only).";

        public override void Action(CommandCaller caller, string input, string[] args)
        {
            if (!DynamicWorldsPermissions.CanUseCheats(caller, out string deniedReason))
            {
                DynamicWorldsPermissions.ReplyDenied(caller, deniedReason);
                return;
            }

            if (Main.netMode != NetmodeID.SinglePlayer)
            {
                caller.Reply("Hardmode command only works in single player.", new Microsoft.Xna.Framework.Color(255, 80, 80));
                return;
            }

            bool setHardmode = true;
            if (args.Length >= 1)
            {
                string arg = args[0].ToLowerInvariant();
                if (arg == "off" || arg == "false" || arg == "0")
                    setHardmode = false;
            }

            if (setHardmode)
            {
                if (Main.hardMode)
                {
                    caller.Reply("Hardmode is already enabled for this world.", new Microsoft.Xna.Framework.Color(255, 220, 120));
                }
                else
                {
                    WorldGen.StartHardmode();
                    caller.Reply("Hardmode ENABLED for this world using the vanilla transition.", new Microsoft.Xna.Framework.Color(150, 255, 150));
                }
            }
            else
            {
                Main.hardMode = false;
                caller.Reply("Hardmode DISABLED for this world.", new Microsoft.Xna.Framework.Color(255, 150, 150));
            }

            WorldProgressUtil.SaveToFile();
        }
    }

    public class DownCommand : ModCommand
    {
        public override CommandType Type => CommandType.World | CommandType.Console;
        public override string Command => "down";
        public override string Usage => "/down <bossOrEvent>";
        public override string Description =>
            "Mark a boss or event as defeated (e.g., /down eye, /down plantera, /down goblins).";

        public override void Action(CommandCaller caller, string input, string[] args)
        {
            if (!DynamicWorldsPermissions.CanUseCheats(caller, out string deniedReason))
            {
                DynamicWorldsPermissions.ReplyDenied(caller, deniedReason);
                return;
            }

            if (Main.netMode != NetmodeID.SinglePlayer)
            {
                caller.Reply("Down command only works in single player.", new Microsoft.Xna.Framework.Color(255, 80, 80));
                return;
            }

            if (args.Length == 0)
            {
                caller.Reply("Usage: /down <eye|evil|skeletron|queenbee|kingslime|deerclops|mech1|mech2|mech3|plantera|golem|fishron|moonlord|goblins|frost|pirates|martians|pumpkin|frostmoon>", new Microsoft.Xna.Framework.Color(255, 230, 150));
                return;
            }

            string key = args[0].ToLowerInvariant();
            if (!DownFlagHelper.SetDowned(key, out string label))
            {
                caller.Reply($"Unknown boss/event '{key}'.", new Microsoft.Xna.Framework.Color(255, 80, 80));
                caller.Reply("Valid: eye, evil, skeletron, queenbee, kingslime, deerclops, mech1, mech2, mech3, plantera, golem, fishron, moonlord, goblins, frost, pirates, martians, pumpkin, frostmoon", new Microsoft.Xna.Framework.Color(255, 230, 150));
                return;
            }

            caller.Reply($"Marked {label} as defeated.", new Microsoft.Xna.Framework.Color(150, 255, 150));
            WorldProgressUtil.SaveToFile();
        }
    }

    public static class DownFlagHelper
    {
        public static bool SetDowned(string key, out string label)
        {
            label = "";

            switch (key)
            {
                case "eye":
                case "eyeofcthulhu":
                    NPC.downedBoss1 = true; label = "Eye of Cthulhu"; return true;
                case "evil":
                case "eow":
                case "boc":
                case "worldeater":
                case "brainofcthulhu":
                    NPC.downedBoss2 = true; label = "Evil boss (EoW / BoC)"; return true;
                case "skeletron":
                    NPC.downedBoss3 = true; label = "Skeletron"; return true;
                case "queenbee":
                case "qb":
                    NPC.downedQueenBee = true; label = "Queen Bee"; return true;
                case "kingslime":
                case "slimeking":
                    NPC.downedSlimeKing = true; label = "King Slime"; return true;
                case "deerclops":
                    NPC.downedDeerclops = true; label = "Deerclops"; return true;

                case "mech1":
                case "twins":
                    NPC.downedMechBoss1 = true; label = "The Twins"; return true;
                case "mech2":
                case "destroyer":
                    NPC.downedMechBoss2 = true; label = "The Destroyer"; return true;
                case "mech3":
                case "prime":
                case "skeletronprime":
                    NPC.downedMechBoss3 = true; label = "Skeletron Prime"; return true;

                case "plantera":
                    NPC.downedPlantBoss = true; label = "Plantera"; return true;
                case "golem":
                    NPC.downedGolemBoss = true; label = "Golem"; return true;
                case "fishron":
                    NPC.downedFishron = true; label = "Duke Fishron"; return true;
                case "moonlord":
                case "ml":
                    NPC.downedMoonlord = true; label = "Moon Lord"; return true;

                case "goblins":
                case "goblinarmy":
                    NPC.downedGoblins = true; label = "Goblin Army"; return true;
                case "frost":
                case "frostlegion":
                    NPC.downedFrost = true; label = "Frost Legion"; return true;
                case "pirates":
                case "pirateinvasion":
                    NPC.downedPirates = true; label = "Pirate Invasion"; return true;
                case "martians":
                case "martianmadness":
                    NPC.downedMartians = true; label = "Martian Madness"; return true;

                case "pumpkin":
                case "pumpkinmoon":
                    NPC.downedHalloweenKing = true;
                    NPC.downedHalloweenTree = true;
                    label = "Pumpkin Moon (Pumpking + Mourning Wood)";
                    return true;

                case "frostmoon":
                case "fm":
                    NPC.downedChristmasIceQueen = true;
                    NPC.downedChristmasSantank = true;
                    NPC.downedChristmasTree = true;
                    label = "Frost Moon (Ice Queen / Santa-NK1 / Everscream)";
                    return true;

                default:
                    return false;
            }
        }
    }
}
