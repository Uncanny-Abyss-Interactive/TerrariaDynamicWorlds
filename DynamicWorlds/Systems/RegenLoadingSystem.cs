using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.UI;
using Terraria.WorldBuilding;

namespace DynamicWorlds
{
    internal enum RegenLifecycleStage
    {
        PreparingToQuit,
        Generating,
        ReloadingWorld
    }

    internal sealed class PendingRegenContext
    {
        public WorldProgressSnapshot Snapshot;
        public Dictionary<Point16, AnchoredTileData> AnchoredTiles = new();
        public Dictionary<Point16, SavedChestContents> AnchoredChests = new();
        public HashSet<Point16> ErasedTiles = new();
        public Dictionary<int, BuildingZone> BuildingZones = new();
        public Dictionary<int, BiomeDowserZone> BiomeDowserZones = new();
        public int NewSeed;
        public string SeedLabel = string.Empty;
        public int SavedSpawnX = -1;
        public int SavedSpawnY = -1;
        public GenerationProgress Progress;
        public Task GenerationTask;
        public RegenLifecycleStage Stage;
        public string StatusMessage = "Preparing regeneration...";
        public bool GenerationSucceeded;
        public RegenExecutionResult ExecutionResult = new RegenExecutionResult();
        public int CycleIndex = 1;
        public int CycleCount = 1;
        public string SeedOverride;
        public string SnapshotFolderPath;
        public string SourceWorldPath;
    }

    internal sealed class QueuedRepeatRegen
    {
        public int NextCycleIndex = 1;
        public int CycleCount = 1;
        public int DelayTicks = 60;
        public string SeedOverride;
        public string SnapshotFolderPath;
    }

    internal sealed class PendingCycleScreenshot
    {
        public string FolderPath;
        public string FilePath;
        public string SeedLabel;
        public int CycleIndex = 1;
        public int CycleCount = 1;
        public int DelayTicks = 12;
    }

    public class DynamicWorldRegenSystem : ModSystem
    {
        private const int MaxPreRegenWorldBackupsPerWorld = 5;

        private static PendingRegenContext _pending;
        private static RegenLoadingUI _loadingUi;
        private static QueuedRepeatRegen _queuedRepeat;
        private static PendingCycleScreenshot _pendingScreenshot;

        internal static PendingRegenContext CurrentContext => _pending;

        public static bool IsBusy => _pending != null || _queuedRepeat != null || _pendingScreenshot != null;

        public static bool SuppressPlayerPositionSave => _pending != null;

        public static bool ShouldSuppressWorldLoadSnapshot =>
            _pending != null && _pending.Stage == RegenLifecycleStage.ReloadingWorld;

        internal static void QueueRegen(PendingRegenContext pending)
        {
            if (pending == null)
                return;

            _pending = pending;
            _pending.Stage = RegenLifecycleStage.PreparingToQuit;
            _pending.StatusMessage = "Saving current world...";

            WorldGen.SaveAndQuit(() => Main.QueueMainThreadAction(BeginGenerationFromMenuWithBackup));
        }

        public override void OnWorldLoad()
        {
            if (_pending == null)
            {
                _queuedRepeat = null;
                _pendingScreenshot = null;
            }
        }

        public override void OnWorldUnload()
        {
            if (_pending == null)
            {
                _queuedRepeat = null;
                _pendingScreenshot = null;
            }
        }

        public override void PostWorldGen()
        {
            if (_pending == null || _pending.Stage != RegenLifecycleStage.Generating)
                return;

            _pending.ExecutionResult = SingleplayerRegenHelper.ExecutePendingRegen(_pending);
            _pending.GenerationSucceeded = true;

            if (_pending.Progress != null)
            {
                _pending.StatusMessage = "Saving regenerated world...";
                _pending.Progress.Message = _pending.StatusMessage;
                _pending.Progress.Start(1d);
                _pending.Progress.Set(1d);
            }
        }

        public override void PostWorldLoad()
        {
            if (_pending == null || _pending.Stage != RegenLifecycleStage.ReloadingWorld)
                return;

            int restoredPylons = PylonRestoreHelper.RestoreTrackedVanillaPylons(forceRefresh: true);
            if (restoredPylons > 0)
            {
                ModContent.GetInstance<DynamicWorlds>().Logger.Info(
                    $"[Regen] Re-registered {restoredPylons} restored vanilla pylon(s) after reloading the regenerated world.");
            }
        }

        public override void UpdateUI(GameTime gameTime)
        {
            if (_pending == null)
                return;

            if (_pending.Stage != RegenLifecycleStage.Generating)
                return;

            EnsureLoadingUi();
        }

        public override void PostUpdatePlayers()
        {
            if (_pending != null)
                return;

            if (Main.netMode != NetmodeID.SinglePlayer || Main.gameMenu)
                return;

            if (_pendingScreenshot != null)
            {
                if (_pendingScreenshot.DelayTicks > 0)
                    _pendingScreenshot.DelayTicks--;

                return;
            }

            if (_queuedRepeat == null)
                return;

            if (_queuedRepeat.DelayTicks > 0)
            {
                _queuedRepeat.DelayTicks--;
                return;
            }

            QueuedRepeatRegen queuedRepeat = _queuedRepeat;
            _queuedRepeat = null;
            SingleplayerRegenHelper.RegenerateWorldWithProgress(
                string.IsNullOrWhiteSpace(queuedRepeat.SeedOverride) ? null : queuedRepeat.SeedOverride,
                queuedRepeat.NextCycleIndex,
                queuedRepeat.CycleCount,
                queuedRepeat.SnapshotFolderPath);
        }

        public override void PostDrawInterface(SpriteBatch spriteBatch)
        {
            if (_pendingScreenshot == null || _pendingScreenshot.DelayTicks > 0)
                return;

            PendingCycleScreenshot screenshot = _pendingScreenshot;
            _pendingScreenshot = null;

            if (!TrySaveCurrentFrame(screenshot, out string savedPath))
            {
                Main.NewText("Cycle screenshot capture failed. Continuing multiregen.", 255, 200, 100);
                return;
            }

            ModContent.GetInstance<DynamicWorlds>().Logger.Info(
                $"[Regen] Saved multiregen screenshot for cycle {screenshot.CycleIndex}/{screenshot.CycleCount}: {savedPath}");

            if (screenshot.CycleCount > 1 && screenshot.CycleIndex >= screenshot.CycleCount)
                Main.NewText($"Completed {screenshot.CycleCount} world regeneration cycles!", 80, 255, 80);
        }

        public static bool TryHandlePostRegenEnter(Player player)
        {
            if (_pending == null || _pending.Stage != RegenLifecycleStage.ReloadingWorld)
                return false;

            PendingRegenContext completedCycle = _pending;
            RegenExecutionResult result = completedCycle.ExecutionResult ?? new RegenExecutionResult();
            bool suppressCycleChat = completedCycle.CycleCount > 1 && !string.IsNullOrWhiteSpace(completedCycle.SnapshotFolderPath);
            Vector2 spawnPos;

            if (result.UsePersonalSpawn)
            {
                player.SpawnX = result.SpawnTileX;
                player.SpawnY = result.SpawnTileY;
                spawnPos = new Vector2(result.SpawnTileX * 16f, result.SpawnTileY * 16f - 48f);
                if (!suppressCycleChat)
                    Main.NewText("Your bed survived — spawning there.", 180, 255, 180);
            }
            else
            {
                player.SpawnX = -1;
                player.SpawnY = -1;
                spawnPos = new Vector2(Main.spawnTileX * 16f, Main.spawnTileY * 16f - 48f);

                if (result.HadSavedSpawn && !suppressCycleChat)
                    Main.NewText("Your bed was not preserved — spawning at world spawn.", 255, 200, 100);
            }

            player.Teleport(spawnPos, 1);
            player.fallStart = (int)(player.position.Y / 16f);
            player.GetModPlayer<DynamicWorldsPlayer>().ClearSavedPosition();
            player.AddBuff(BuffID.Featherfall, 60 * 10);

            if (result.RespawnedNpcCount > 0 && !suppressCycleChat)
            {
                Main.NewText($"Respawned {result.RespawnedNpcCount} town NPC{(result.RespawnedNpcCount == 1 ? "" : "s")}.", 150, 200, 255);

                if (result.RestoredHousingCount > 0)
                {
                    Main.NewText(
                        $"Reassigned {result.RestoredHousingCount} preserved home{(result.RestoredHousingCount == 1 ? "" : "s")}.",
                        180, 255, 180);
                }
            }

            if (!suppressCycleChat)
                WorldProgressUtil.PrintSnapshotToChat("After regen", WorldProgressUtil.Capture());
            WorldProgressUtil.SaveToFile();
            ScheduleCycleScreenshot(completedCycle);
            bool hasQueuedRepeat = completedCycle.CycleIndex < completedCycle.CycleCount;
            if (hasQueuedRepeat)
            {
                QueueFollowupRegen(completedCycle);
                if (!suppressCycleChat)
                {
                    Main.NewText(
                        $"Regen cycle {completedCycle.CycleIndex}/{completedCycle.CycleCount} complete. Next cycle starts shortly...",
                        120,
                        220,
                        255);
                }
            }
            else if (completedCycle.CycleCount > 1 && !suppressCycleChat)
            {
                Main.NewText($"Completed {completedCycle.CycleCount} world regeneration cycles!", 80, 255, 80);
            }
            else if (!suppressCycleChat)
            {
                Main.NewText("World regeneration complete!", 80, 255, 80);
            }

            WorldGenerator.CurrentGenerationProgress = null;
            Main.statusText = string.Empty;
            _pending = null;
            return true;
        }

        private static void BeginGenerationFromMenu()
        {
            if (_pending == null)
                return;

            _pending.Stage = RegenLifecycleStage.Generating;
            _pending.GenerationSucceeded = false;
            _pending.StatusMessage = "Generating world terrain...";
            _pending.Progress = new GenerationProgress();

            if (Main.ActiveWorldFileData != null)
                Main.ActiveWorldFileData.SetSeed(_pending.NewSeed.ToString());

            EnsureLoadingUi();
            _pending.GenerationTask = WorldGen.CreateNewWorld(_pending.Progress);
            _pending.GenerationTask.ContinueWith(task =>
            {
                Main.QueueMainThreadAction(() => HandleGenerationCompleted(task));
            }, TaskScheduler.Default);
            EnsureLoadingUi();
        }

        private static void BeginGenerationFromMenuWithBackup()
        {
            if (_pending == null)
                return;

            TryCreatePreRegenBackup(_pending);
            BeginGenerationFromMenu();
        }

        private static void HandleGenerationCompleted(Task completedTask)
        {
            if (_pending == null || _pending.Stage != RegenLifecycleStage.Generating)
                return;

            if (!ReferenceEquals(completedTask, _pending.GenerationTask))
                return;

            if (completedTask.IsFaulted || !_pending.GenerationSucceeded)
            {
                if (completedTask.Exception != null)
                {
                    ModContent.GetInstance<DynamicWorlds>().Logger.Error(
                        "World regeneration failed before the world could be reloaded.",
                        completedTask.Exception);
                }

                FailPendingRegen();
                return;
            }

            PersistPlayerForReload(_pending.ExecutionResult);
            _pending.StatusMessage = "Loading regenerated world...";
            _pending.Stage = RegenLifecycleStage.ReloadingWorld;
            _pending.GenerationTask = null;
            WorldGenerator.CurrentGenerationProgress = null;
            Main.statusText = _pending.StatusMessage;
            WorldGen.playWorld();
            Main.menuMode = 10;
        }

        private static void EnsureLoadingUi()
        {
            _loadingUi ??= new RegenLoadingUI();

            Main.menuMode = 888;
            if (Main.MenuUI.CurrentState != _loadingUi)
                Main.MenuUI.SetState(_loadingUi);
        }

        internal static void TryCreatePreRegenBackup(PendingRegenContext pending)
        {
            if (pending == null || string.IsNullOrWhiteSpace(pending.SourceWorldPath))
                return;

            try
            {
                string sourceWorldPath = pending.SourceWorldPath;
                if (!File.Exists(sourceWorldPath))
                    return;

                string worldFileName = Path.GetFileNameWithoutExtension(sourceWorldPath);
                string worldDirectory = Path.GetDirectoryName(sourceWorldPath)!;
                string backupRoot = Path.Combine(
                    worldDirectory,
                    "DynamicWorldsBackups",
                    SanitizeFileNamePart(worldFileName));
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss_fff");
                string backupFolder = Path.Combine(backupRoot, timestamp);

                Directory.CreateDirectory(backupFolder);

                CopyIfExists(sourceWorldPath, Path.Combine(backupFolder, Path.GetFileName(sourceWorldPath)));

                string tmodWorldPath = Path.ChangeExtension(sourceWorldPath, ".twld");
                CopyIfExists(tmodWorldPath, Path.Combine(backupFolder, Path.GetFileName(tmodWorldPath)));

                string progressPath = WorldProgressUtil.GetProgressFilePathForWorld(sourceWorldPath);
                CopyIfExists(progressPath, Path.Combine(backupFolder, Path.GetFileName(progressPath)));

                string backupInfoPath = Path.Combine(backupFolder, "backup_info.txt");
                File.WriteAllLines(
                    backupInfoPath,
                    new[]
                    {
                        $"WorldName: {pending.Snapshot?.worldName ?? Main.worldName}",
                        $"WorldId: {pending.Snapshot?.worldId ?? Main.worldID}",
                        $"OriginalWorldPath: {sourceWorldPath}",
                        $"CreatedLocal: {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
                        $"Cycle: {pending.CycleIndex}/{pending.CycleCount}",
                        $"SeedLabel: {pending.SeedLabel}",
                    });

                RotateBackups(backupRoot, MaxPreRegenWorldBackupsPerWorld);

                ModContent.GetInstance<DynamicWorlds>().Logger.Info(
                    $"[Regen] Created pre-regen world backup at: {backupFolder}");
            }
            catch (Exception ex)
            {
                ModContent.GetInstance<DynamicWorlds>().Logger.Warn(
                    $"[Regen] Failed to create pre-regen world backup: {ex.Message}");
            }
        }

        private static void CopyIfExists(string sourcePath, string destinationPath)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
                return;

            File.Copy(sourcePath, destinationPath, overwrite: true);
        }

        private static void RotateBackups(string backupRoot, int keepCount)
        {
            if (keepCount < 1 || string.IsNullOrWhiteSpace(backupRoot) || !Directory.Exists(backupRoot))
                return;

            string[] backupDirectories = Directory.GetDirectories(backupRoot)
                .OrderByDescending(Path.GetFileName)
                .ToArray();

            for (int i = keepCount; i < backupDirectories.Length; i++)
            {
                try
                {
                    Directory.Delete(backupDirectories[i], recursive: true);
                }
                catch (Exception ex)
                {
                    ModContent.GetInstance<DynamicWorlds>().Logger.Warn(
                        $"[Regen] Failed to prune old backup folder '{backupDirectories[i]}': {ex.Message}");
                }
            }
        }

        private static void PersistPlayerForReload(RegenExecutionResult result)
        {
            Player player = Main.ActivePlayerFileData?.Player ?? Main.LocalPlayer;
            if (player == null)
                return;

            if (result.UsePersonalSpawn)
            {
                player.SpawnX = result.SpawnTileX;
                player.SpawnY = result.SpawnTileY;
            }
            else
            {
                player.SpawnX = -1;
                player.SpawnY = -1;
            }

            player.GetModPlayer<DynamicWorldsPlayer>().ClearSavedPosition();

            if (Main.ActivePlayerFileData != null)
            {
                Main.ActivePlayerFileData.Player = player;
                Player.SavePlayer(Main.ActivePlayerFileData);
            }
        }

        private static void FailPendingRegen()
        {
            WorldGenerator.CurrentGenerationProgress = null;
            Main.statusText = "World regeneration failed.";
            _queuedRepeat = null;
            _pendingScreenshot = null;
            Main.LoadWorlds();
            Main.GoToWorldSelect();
            _pending = null;
        }

        private static void QueueFollowupRegen(PendingRegenContext completedCycle)
        {
            _queuedRepeat = new QueuedRepeatRegen
            {
                NextCycleIndex = completedCycle.CycleIndex + 1,
                CycleCount = completedCycle.CycleCount,
                DelayTicks = 60,
                SeedOverride = completedCycle.SeedOverride,
                SnapshotFolderPath = completedCycle.SnapshotFolderPath
            };
        }

        private static void ScheduleCycleScreenshot(PendingRegenContext completedCycle)
        {
            if (completedCycle == null ||
                completedCycle.CycleCount <= 1 ||
                string.IsNullOrWhiteSpace(completedCycle.SnapshotFolderPath))
                return;

            _pendingScreenshot = new PendingCycleScreenshot
            {
                FolderPath = completedCycle.SnapshotFolderPath,
                FilePath = BuildCycleScreenshotPath(
                    completedCycle.SnapshotFolderPath,
                    completedCycle.CycleIndex,
                    completedCycle.CycleCount,
                    completedCycle.SeedLabel),
                SeedLabel = completedCycle.SeedLabel,
                CycleIndex = completedCycle.CycleIndex,
                CycleCount = completedCycle.CycleCount,
                DelayTicks = 12
            };
        }

        private static string BuildCycleScreenshotPath(string folderPath, int cycleIndex, int cycleCount, string seedLabel)
        {
            string cyclePart = cycleCount > 1
                ? $"cycle-{cycleIndex:D2}-of-{cycleCount:D2}"
                : $"cycle-{cycleIndex:D2}";
            string seedPart = string.IsNullOrWhiteSpace(seedLabel)
                ? string.Empty
                : $"_seed-{SanitizeFileNamePart(seedLabel)}";
            return Path.Combine(folderPath, $"{cyclePart}{seedPart}.png");
        }

        private static string SanitizeFileNamePart(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "unknown";

            char[] chars = value.Trim().ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                if (Array.IndexOf(Path.GetInvalidFileNameChars(), chars[i]) >= 0 || char.IsWhiteSpace(chars[i]))
                    chars[i] = '_';
            }

            string sanitized = new string(chars).Trim('_');
            if (string.IsNullOrWhiteSpace(sanitized))
                sanitized = "unknown";

            return sanitized.Length > 48 ? sanitized.Substring(0, 48) : sanitized;
        }

        private static bool TrySaveCurrentFrame(PendingCycleScreenshot screenshot, out string savedPath)
        {
            savedPath = screenshot?.FilePath ?? string.Empty;

            try
            {
                if (screenshot == null || string.IsNullOrWhiteSpace(screenshot.FolderPath) || string.IsNullOrWhiteSpace(screenshot.FilePath))
                    return false;

                GraphicsDevice graphicsDevice = Main.instance?.GraphicsDevice;
                if (graphicsDevice == null)
                    return false;

                int width = graphicsDevice.PresentationParameters.BackBufferWidth;
                int height = graphicsDevice.PresentationParameters.BackBufferHeight;
                if (width <= 0 || height <= 0)
                    return false;

                Directory.CreateDirectory(screenshot.FolderPath);

                Color[] pixels = new Color[width * height];
                graphicsDevice.GetBackBufferData(pixels);

                using var texture = new Texture2D(graphicsDevice, width, height, false, SurfaceFormat.Color);
                texture.SetData(pixels);

                using (FileStream stream = File.Create(screenshot.FilePath))
                    texture.SaveAsPng(stream, width, height);

                AppendScreenshotManifestEntry(screenshot);
                savedPath = screenshot.FilePath;
                return true;
            }
            catch (Exception ex)
            {
                ModContent.GetInstance<DynamicWorlds>().Logger.Warn(
                    $"[Regen] Failed to save multiregen screenshot for cycle {screenshot?.CycleIndex}/{screenshot?.CycleCount}.",
                    ex);
                return false;
            }
        }

        private static void AppendScreenshotManifestEntry(PendingCycleScreenshot screenshot)
        {
            try
            {
                string manifestPath = Path.Combine(screenshot.FolderPath, "manifest.txt");
                bool writeHeader = !File.Exists(manifestPath);
                using var writer = new StreamWriter(manifestPath, append: true);

                if (writeHeader)
                {
                    writer.WriteLine("Dynamic Worlds multiregen screenshots");
                    writer.WriteLine($"World: {Main.worldName}");
                    writer.WriteLine($"Created: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    writer.WriteLine();
                }

                writer.WriteLine(
                    $"Cycle {screenshot.CycleIndex}/{screenshot.CycleCount} | Seed {screenshot.SeedLabel ?? "unknown"} | {Path.GetFileName(screenshot.FilePath)}");
            }
            catch (Exception ex)
            {
                ModContent.GetInstance<DynamicWorlds>().Logger.Warn("[Regen] Failed to update multiregen screenshot manifest.", ex);
            }
        }
    }

    internal readonly record struct RegenLoadingLayout(
        Rectangle Panel, Rectangle Bar, Vector2 MessagePosition, Vector2 DetailPosition,
        Vector2 PercentagePosition, string Message, string Detail);

    internal sealed class RegenLoadingUI : UIState
    {
        internal static RegenLoadingLayout CreateLayout(int screenWidth, int screenHeight, string message, string detail)
        {
            int panelWidth = Math.Min(620, screenWidth - 80);
            const int panelHeight = 230;
            Rectangle panel = new Rectangle((screenWidth - panelWidth) / 2,
                (screenHeight - panelHeight) / 2, panelWidth, panelHeight);
            Rectangle bar = new Rectangle(panel.X + 36, panel.Bottom - 74, panel.Width - 72, 24);
            // Leave room for the text outline at both edges of the content area.
            float textWidth = bar.Width - 4f;
            return new RegenLoadingLayout(panel, bar,
                new Vector2(panel.Center.X, panel.Y + 90),
                new Vector2(panel.Center.X, panel.Y + 126),
                new Vector2(bar.Right - 2, bar.Bottom + 10),
                FitDisplayText(message, textWidth, 1f),
                FitDisplayText(detail, textWidth, 0.9f));
        }

        private static string FitDisplayText(string text, float maxWidth, float scale)
        {
            // Only the displayed label is shortened; the pending seed stays intact.
            text = (text ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ');
            var font = FontAssets.MouseText.Value;
            if (font.MeasureString(text).X * scale <= maxWidth)
                return text;

            const string ellipsis = "...";
            if (font.MeasureString(ellipsis).X * scale > maxWidth)
                return string.Empty;
            int[] starts = StringInfo.ParseCombiningCharacters(text);
            int low = 0, high = starts.Length;
            while (low < high)
            {
                int count = (low + high + 1) / 2;
                int end = count == starts.Length ? text.Length : starts[count];
                if (font.MeasureString(text.Substring(0, end) + ellipsis).X * scale <= maxWidth)
                    low = count;
                else
                    high = count - 1;
            }
            int prefixLength = low == starts.Length ? text.Length : starts[low];
            return text.Substring(0, prefixLength) + ellipsis;
        }

        protected override void DrawSelf(SpriteBatch spriteBatch)
        {
            PendingRegenContext pending = DynamicWorldRegenSystem.CurrentContext;
            if (pending == null)
                return;

            GenerationProgress progress = pending.Progress;
            float overallProgress = progress != null ? MathHelper.Clamp((float)progress.TotalProgress, 0f, 1f) : 0f;
            string message = !string.IsNullOrWhiteSpace(progress?.Message) ? progress.Message : pending.StatusMessage;
            string cycleLabel = pending.CycleCount > 1
                ? $"Cycle {pending.CycleIndex}/{pending.CycleCount}"
                : string.Empty;
            string seedLabel = !string.IsNullOrWhiteSpace(pending.SeedLabel)
                ? $"Seed: {pending.SeedLabel}"
                : string.Empty;
            string detail = !string.IsNullOrWhiteSpace(cycleLabel) && !string.IsNullOrWhiteSpace(seedLabel)
                ? $"{cycleLabel} • {seedLabel}"
                : !string.IsNullOrWhiteSpace(cycleLabel)
                    ? cycleLabel
                    : !string.IsNullOrWhiteSpace(seedLabel)
                        ? seedLabel
                        : "Preserving anchored tiles, housing, and progression";

            RegenLoadingLayout layout = CreateLayout(Main.screenWidth, Main.screenHeight, message, detail);
            Texture2D pixel = TextureAssets.MagicPixel.Value;
            Rectangle panel = layout.Panel;
            Rectangle border = new Rectangle(panel.X - 3, panel.Y - 3, panel.Width + 6, panel.Height + 6);
            Rectangle bar = layout.Bar;
            Rectangle fill = new Rectangle(bar.X + 4, bar.Y + 4, (int)((bar.Width - 8) * overallProgress), bar.Height - 8);

            float pulse = 0.7f + 0.3f * (float)Math.Sin(Main.GlobalTimeWrappedHourly * MathHelper.TwoPi * 1.6f);

            spriteBatch.Draw(pixel, border, new Color(18, 28, 52, 255));
            spriteBatch.Draw(pixel, panel, new Color(8, 12, 28, 235));
            spriteBatch.Draw(pixel, bar, new Color(24, 34, 58, 255));

            if (fill.Width > 0)
                spriteBatch.Draw(pixel, fill, Color.Lerp(new Color(80, 170, 255), new Color(165, 235, 255), pulse));

            Utils.DrawBorderStringBig(
                spriteBatch,
                "Regenerating World",
                new Vector2(panel.Center.X, panel.Y + 38),
                Color.White,
                0.7f,
                0.5f,
                0.5f);

            Utils.DrawBorderString(
                spriteBatch,
                layout.Message,
                layout.MessagePosition,
                new Color(200, 230, 255),
                1f,
                0.5f,
                0.5f);

            Utils.DrawBorderString(
                spriteBatch,
                layout.Detail,
                layout.DetailPosition,
                new Color(150, 185, 220),
                0.9f,
                0.5f,
                0.5f);

            Utils.DrawBorderString(
                spriteBatch,
                $"{overallProgress:P1}",
                layout.PercentagePosition,
                Color.White,
                0.95f,
                1f,
                0f);
        }
    }
}
