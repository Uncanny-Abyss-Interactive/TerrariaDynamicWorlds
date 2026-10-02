using System.Collections.Generic;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace DynamicWorldsValidation;

internal static class MicrofixChecks
{
    internal static Dictionary<string, bool> ClientChecks()
    {
        var checks = new Dictionary<string, bool>();
        int slot = Main.maxNPCs - 1;
        NPC previousNpc = Main.npc[slot];
        int previousTalkNpc = Main.LocalPlayer.talkNPC;
        var guide = new NPC { whoAmI = slot };
        guide.SetDefaults(NPCID.Guide);
        string previousChat = Main.npcChatText;
        try
        {
            Main.npc[slot] = guide;
            Main.LocalPlayer.SetTalkNPC(slot);
            Main.npcChatText = "Guide crafting regression sentinel";
            bool craftingAllowed = NPCLoader.PreChatButtonClicked(firstButton: false);
            bool repeatedCraftingAllowed = NPCLoader.PreChatButtonClicked(firstButton: false);
            bool helpAllowed = NPCLoader.PreChatButtonClicked(firstButton: true);
            checks["microfix.guide_vanilla_buttons"] = craftingAllowed && repeatedCraftingAllowed && helpAllowed;
            checks["microfix.guide_dialogue_unchanged"] = Main.npcChatText == "Guide crafting regression sentinel";
        }
        finally
        {
            Main.npcChatText = previousChat;
            Main.LocalPlayer.SetTalkNPC(previousTalkNpc);
            Main.npc[slot] = previousNpc;
        }
        foreach (var check in PacketDirectionChecks.ClientChecks())
            checks[check.Key] = check.Value;
        return checks;
    }
}
