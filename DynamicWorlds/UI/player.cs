using System.Linq;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace DynamicWorlds
{
    public class DynamicWorldsPlayer : ModPlayer
    {
        // Last saved world position in tile coordinates. -1 = no saved position.
        private int _savedTileX = -1;
        private int _savedTileY = -1;

        // Called by regenworldcommand after it has already teleported the player to spawn,
        // so the pre-regen position is not restored on the next world load.
        public void ClearSavedPosition()
        {
            _savedTileX = -1;
            _savedTileY = -1;
        }

        public override bool PreKill(double damage, int hitDirection, bool pvp, ref bool playSound, ref bool blood, ref Terraria.DataStructures.PlayerDeathReason damageSource)
        {
            // Trigger regen on death if enabled
            if (ModContent.GetInstance<DynamicWorldsConfig>().RegenOnDeath && Main.netMode == NetmodeID.SinglePlayer)
            {
                SingleplayerRegenHelper.RegenerateWorldWithProgress();
                return false; // Prevent death (world will regen)
            }

            return base.PreKill(damage, hitDirection, pvp, ref playSound, ref blood, ref damageSource);
        }

        public override void OnEnterWorld()
        {
            if (ModContent.GetInstance<DynamicWorldsConfig>().AutoGiveTools)
            {
                // Gift Reality Anchor if not already in inventory
                int anchorType = ModContent.ItemType<RealityAnchor>();
                bool hasAnchor = Player.inventory.Any(i => i != null && i.type == anchorType);
                if (!hasAnchor)
                    Player.QuickSpawnItem(Player.GetSource_GiftOrReward(), anchorType);

                // Gift Reality Eraser if not already in inventory
                int eraserType = ModContent.ItemType<RealityEraser>();
                bool hasEraser = Player.inventory.Any(i => i != null && i.type == eraserType);
                if (!hasEraser)
                    Player.QuickSpawnItem(Player.GetSource_GiftOrReward(), eraserType);

                // Gift one Structure Anchor if not already in inventory
                int baType = ModContent.ItemType<StructureAnchorItem>();
                bool hasBA = Player.inventory.Any(i => i != null && i.type == baType);
                if (!hasBA)
                    Player.QuickSpawnItem(Player.GetSource_GiftOrReward(), baType);

                int dowserType = ModContent.ItemType<BiomeDowser>();
                bool hasDowser = Player.inventory.Any(i => i != null && i.type == dowserType);
                if (!hasDowser)
                    Player.QuickSpawnItem(Player.GetSource_GiftOrReward(), dowserType);
            }

            if (DynamicWorldRegenSystem.TryHandlePostRegenEnter(Player))
                return;

            MultiplayerRegenSystem.TryHandlePlayerEnterWorld(Player);

            if (Main.netMode == NetmodeID.MultiplayerClient && Player.whoAmI == Main.myPlayer)
                DynamicWorldsNet.RequestFullSync();

            // Restore last position — singleplayer only, and only if the destination
            // tiles are actually clear so the player doesn't clip into solid terrain.
            if (Main.netMode == NetmodeID.SinglePlayer && _savedTileX > 0 && _savedTileY > 0)
            {
                if (IsPositionSafe(_savedTileX, _savedTileY))
                {
                    Vector2 pos = new Vector2(_savedTileX * 16f, _savedTileY * 16f - 48f);
                    Player.Teleport(pos, 1);
                    Player.fallStart = (int)(Player.position.Y / 16f);
                }
                // Always clear after attempting — don't persist a bad position.
                _savedTileX = -1;
                _savedTileY = -1;
            }
        }

        // Returns true if the two tiles at (x, y) and (x, y-1) are both empty,
        // meaning the player can stand there without clipping into terrain.
        private static bool IsPositionSafe(int x, int y)
        {
            if (!WorldGen.InWorld(x, y, 5) || !WorldGen.InWorld(x, y - 1, 5))
                return false;

            Tile feet = Framing.GetTileSafely(x, y);
            Tile head = Framing.GetTileSafely(x, y - 1);

            return !feet.HasTile && !head.HasTile;
        }

        public override void PreSavePlayer()
        {
            if (DynamicWorldRegenSystem.SuppressPlayerPositionSave)
                return;

            // Capture tile position just before the player file is written.
            _savedTileX = (int)(Player.position.X / 16f);
            _savedTileY = (int)(Player.position.Y / 16f);
        }

        // ── Persistence ──────────────────────────────────────────────────

        public override void SaveData(TagCompound tag)
        {
            if (DynamicWorldRegenSystem.SuppressPlayerPositionSave)
                return;

            // Last known position — only save if we have valid coords.
            if (_savedTileX > 0 && _savedTileY > 0)
            {
                tag["lastTileX"] = _savedTileX;
                tag["lastTileY"] = _savedTileY;
            }
            else if (Player.active && Main.netMode == NetmodeID.SinglePlayer)
            {
                // PreSavePlayer may not have fired yet in all paths — fall back to current pos.
                tag["lastTileX"] = (int)(Player.position.X / 16f);
                tag["lastTileY"] = (int)(Player.position.Y / 16f);
            }

            // Bed spawn backup (safety net for post-regen world reloads).
            if (Player.SpawnX >= 0 && Player.SpawnY >= 0)
            {
                tag["spawnX"] = Player.SpawnX;
                tag["spawnY"] = Player.SpawnY;
            }
        }

        public override void LoadData(TagCompound tag)
        {
            // Restore last position — will be applied in OnEnterWorld once the world is ready.
            if (tag.ContainsKey("lastTileX"))
            {
                _savedTileX = tag.GetInt("lastTileX");
                _savedTileY = tag.GetInt("lastTileY");
            }

            // Restore bed spawn if vanilla lost it.
            if (Player.SpawnX < 0 && tag.ContainsKey("spawnX"))
            {
                Player.SpawnX = tag.GetInt("spawnX");
                Player.SpawnY = tag.GetInt("spawnY");
            }
        }
    }
}
