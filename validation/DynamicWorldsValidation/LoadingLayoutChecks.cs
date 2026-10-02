using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using DynamicWorlds;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.UI;
using Terraria.WorldBuilding;

namespace DynamicWorldsValidation;

// Main.OnPreDraw runs before the normal frame is rendered. Unbinding an offscreen
// target can discard the backbuffer; the following normal frame repaints it. This
// test owns a separate batch and never ends, restarts, or replaces Main.spriteBatch.
public sealed class LoadingLayoutChecksSystem : ModSystem
{
    public override void Load()
    {
        LoadingLayoutChecks.Reset();
        if (!Main.dedServ)
            Main.OnPreDraw += LoadingLayoutChecks.OnPreDraw;
    }

    public override void Unload()
    {
        Main.OnPreDraw -= LoadingLayoutChecks.OnPreDraw;
        LoadingLayoutChecks.Reset();
    }
}

internal static class LoadingLayoutChecks
{
    internal const string ScreenshotFolder = "loading-layout";
    private static readonly Dictionary<string, bool> Checks = new();
    private static bool _attempted;
    private static bool _complete;
    private static Exception _error;

    internal static void Reset()
    {
        Checks.Clear();
        _attempted = false;
        _complete = false;
        _error = null;
    }

    // Merge these checks into ClientSmoke's existing final checks after its 120
    // post-regeneration frames. The screenshots have already run on an earlier
    // OnPreDraw callback; no graphics work occurs inside PostDrawInterface.
    internal static Dictionary<string, bool> ClientChecks()
    {
        if (!_complete)
            throw new InvalidOperationException("Loading-layout screenshots did not complete after regeneration.");
        if (_error != null)
            throw new InvalidOperationException("Loading-layout validation failed.", _error);
        return new Dictionary<string, bool>(Checks);
    }

    internal static void OnPreDraw(GameTime gameTime)
    {
        if (_attempted || Main.dedServ || Main.gameMenu || WorldGen.gen
            || Main.netMode != NetmodeID.SinglePlayer
            || Environment.GetEnvironmentVariable("DW_VALIDATION_MODE") != "client"
            || DynamicWorldRegenSystem.IsBusy || MultiplayerRegenSystem.IsBusy)
            return;
        if (!ValidationGuard.TryGet(out ValidationContext context, out _, requireServer: false)
            || Main.ActiveWorldFileData?.SeedText != context.ExpectedSeed.ToString())
            return;

        _attempted = true;
        try
        {
            Run(context);
        }
        catch (Exception ex)
        {
            _error = ex;
        }
        finally { _complete = true; }
    }

    private static void Run(ValidationContext context)
    {
        Assembly production = typeof(DynamicWorldRegenSystem).Assembly;
        Type uiType = production.GetType("DynamicWorlds.RegenLoadingUI")
            ?? throw new InvalidOperationException("The production loading UI was not found.");
        Type pendingType = production.GetType("DynamicWorlds.PendingRegenContext")
            ?? throw new InvalidOperationException("The production pending context was not found.");
        MethodInfo createLayout = uiType.GetMethod("CreateLayout", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("The production loading layout method was not found.");
        FieldInfo pendingField = typeof(DynamicWorldRegenSystem).GetField("_pending", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("The production pending context field was not found.");
        GraphicsDevice device = Main.instance.GraphicsDevice;
        object originalPending = pendingField.GetValue(null);
        int originalWidth = Main.screenWidth, originalHeight = Main.screenHeight;
        RenderTargetBinding[] originalTargets = device.GetRenderTargets();
        Viewport originalViewport = device.Viewport;
        SpriteBatch originalBatch = Main.spriteBatch;
        Rectangle originalScissor = device.ScissorRectangle;
        BlendState originalBlend = device.BlendState;
        DepthStencilState originalDepth = device.DepthStencilState;
        RasterizerState originalRasterizer = device.RasterizerState;
        SamplerState originalSampler = device.SamplerStates[0];
        Texture originalTexture = device.Textures[0];
        IndexBuffer originalIndices = device.Indices;
        VertexBufferBinding[] originalVertices = device.GetVertexBuffers();
        string directory = Path.Combine(context.Root, ScreenshotFolder);
        Directory.CreateDirectory(directory);
        bool seedUnchanged = true;
        bool screenshotsSaved = true;

        foreach ((int width, int height) in new[] { (800, 600), (1280, 720) })
        {
            foreach (bool longText in new[] { false, true })
            {
                string seed = longText ? new string('W', 200) : "8675309";
                string status = longText ? "Generating terrain " + new string('W', 200) : "Generating world terrain...";
                string detail = "Cycle 2/3 • Seed: " + seed;
                object layout = createLayout.Invoke(null, new object[] { width, height, status, detail });
                string caseName = width + "_" + (longText ? "long" : "short");
                Checks["microfix.loading_layout_" + caseName] = ValidateLayout(layout, width, height, status, detail, longText);

                object pending = Activator.CreateInstance(pendingType, nonPublic: true);
                Set(pending, "SeedLabel", seed);
                Set(pending, "NewSeed", context.ExpectedSeed);
                Set(pending, "CycleIndex", 2);
                Set(pending, "CycleCount", 3);
                Set(pending, "StatusMessage", status);
                var progress = new GenerationProgress { TotalWeight = 1d, Message = status };
                progress.Start(1d);
                progress.Set(0.55d);
                Set(pending, "Progress", progress);
                string path = Path.Combine(directory, width + "x" + height + "-" + (longText ? "long" : "short") + ".png");
                RenderActualUi(device, uiType, pendingField, pending, width, height, path);
                screenshotsSaved &= File.Exists(path) && new FileInfo(path).Length > 1000;
                seedUnchanged &= (string)Get(pending, "SeedLabel") == seed
                    && (int)Get(pending, "NewSeed") == context.ExpectedSeed
                    && (int)Get(pending, "CycleIndex") == 2 && (int)Get(pending, "CycleCount") == 3;
            }
        }
        Checks["microfix.loading_layout_unicode_text_elements"] = ValidateUnicodeLabels(
            createLayout, pendingType, context.ExpectedSeed, out bool unicodeSeedUnchanged);
        Checks["microfix.loading_layout_seed_unchanged"] = seedUnchanged && unicodeSeedUnchanged;
        Checks["microfix.loading_layout_screenshots"] = screenshotsSaved;
        Checks["microfix.loading_layout_state_restored"] = ReferenceEquals(pendingField.GetValue(null), originalPending)
            && Main.screenWidth == originalWidth && Main.screenHeight == originalHeight
            && ReferenceEquals(Main.spriteBatch, originalBatch)
            && device.GetRenderTargets().SequenceEqual(originalTargets) && device.Viewport.Equals(originalViewport)
            && device.ScissorRectangle == originalScissor && ReferenceEquals(device.BlendState, originalBlend)
            && ReferenceEquals(device.DepthStencilState, originalDepth) && ReferenceEquals(device.RasterizerState, originalRasterizer)
            && ReferenceEquals(device.SamplerStates[0], originalSampler) && ReferenceEquals(device.Textures[0], originalTexture)
            && ReferenceEquals(device.Indices, originalIndices) && device.GetVertexBuffers().SequenceEqual(originalVertices);
    }

    private static bool ValidateUnicodeLabels(MethodInfo createLayout, Type pendingType, int expectedSeed,
        out bool seedUnchanged)
    {
        // Vary the leading glyph widths so truncation meets several different
        // boundaries in repeated multi-code-point text elements, without rendering
        // any extra screenshot cases. CR/LF occur before the truncation point.
        string elements = string.Concat(Enumerable.Repeat("e\u0301\u0308", 200));
        string seed = "first\r\nsecond\n" + elements;
        object pending = Activator.CreateInstance(pendingType, nonPublic: true);
        Set(pending, "SeedLabel", seed);
        Set(pending, "NewSeed", expectedSeed);
        bool valid = true;
        for (int padding = 0; padding < 8; padding++)
        {
            string message = "Status\r\nline\n" + new string('W', padding) + elements;
            string detail = "Cycle 2/3 • Seed: " + new string('W', padding) + (string)Get(pending, "SeedLabel");
            object layout = createLayout.Invoke(null, new object[] { 800, 600, message, detail });
            float width = Read<Rectangle>(layout, "Bar").Width - 4f;
            valid &= IsWholeElementPrefix(message, Read<string>(layout, "Message"), width, 1f)
                && IsWholeElementPrefix(detail, Read<string>(layout, "Detail"), width, 0.9f);
        }
        seedUnchanged = (string)Get(pending, "SeedLabel") == seed && (int)Get(pending, "NewSeed") == expectedSeed;
        return valid;

        static bool IsWholeElementPrefix(string source, string displayed, float maxWidth, float scale)
        {
            string normalized = source.Replace('\r', ' ').Replace('\n', ' ');
            if (!displayed.EndsWith("...", StringComparison.Ordinal)
                || displayed.Contains('\r') || displayed.Contains('\n'))
                return false;
            string prefix = displayed.Substring(0, displayed.Length - 3);
            int firstCombiningMark = normalized.IndexOf('\u0301');
            return prefix.Length > firstCombiningMark && prefix.Length < normalized.Length
                && normalized.StartsWith(prefix, StringComparison.Ordinal)
                && StringInfo.ParseCombiningCharacters(normalized).Contains(prefix.Length)
                && FontAssets.MouseText.Value.MeasureString(displayed).X * scale <= maxWidth + 0.01f;
        }
    }

    private static bool ValidateLayout(object layout, int width, int height, string originalMessage, string originalDetail, bool longText)
    {
        Rectangle panel = Read<Rectangle>(layout, "Panel");
        Rectangle bar = Read<Rectangle>(layout, "Bar");
        string message = Read<string>(layout, "Message");
        string detail = Read<string>(layout, "Detail");
        // Compute independent text bounds from the real loaded fonts, including
        // a conservative two-pixel outline. Check actual separation and enclosure,
        // rather than duplicating CreateLayout's offsets as expected values.
        Rectangle messageBounds = TextBounds(message, Read<Vector2>(layout, "MessagePosition"), 1f, 0.5f, 0.5f);
        Rectangle detailBounds = TextBounds(detail, Read<Vector2>(layout, "DetailPosition"), 0.9f, 0.5f, 0.5f);
        Rectangle percentBounds = TextBounds($"{0.55d:P1}", Read<Vector2>(layout, "PercentagePosition"), 0.95f, 1f, 0f);
        Rectangle content = new Rectangle(bar.X, panel.Y, bar.Width, panel.Height);
        Rectangle screen = new Rectangle(0, 0, width, height);
        bool fitted = longText
            ? message.Length < originalMessage.Length && detail.Length < originalDetail.Length
                && message.EndsWith("...", StringComparison.Ordinal) && detail.EndsWith("...", StringComparison.Ordinal)
            : message == originalMessage && detail == originalDetail;
        bool passed = screen.Contains(panel) && panel.Contains(bar) && content.Contains(messageBounds)
            && content.Contains(detailBounds) && content.Contains(percentBounds)
            && !messageBounds.Intersects(detailBounds) && !messageBounds.Intersects(bar)
            && !detailBounds.Intersects(bar) && !detailBounds.Intersects(percentBounds)
            && !bar.Intersects(percentBounds) && percentBounds.Top > bar.Bottom
            && detail.StartsWith("Cycle 2/3 • Seed: ", StringComparison.Ordinal) && fitted;
        if (!passed)
            Console.WriteLine($"[LoadingLayout] {width}x{height} long={longText}: panel={panel}, bar={bar}, " +
                $"message={messageBounds}, detail={detailBounds}, percent={percentBounds}, fitted={fitted}, " +
                $"messageContained={content.Contains(messageBounds)}, detailContained={content.Contains(detailBounds)}, " +
                $"percentContained={content.Contains(percentBounds)}, labelsOverlap={messageBounds.Intersects(detailBounds)}, " +
                $"detailBarOverlap={detailBounds.Intersects(bar)}, detailPrefix={detail.StartsWith("Cycle 2/3 • Seed: ", StringComparison.Ordinal)}");
        return passed;
    }

    private static Rectangle TextBounds(string text, Vector2 position, float scale, float anchorX, float anchorY)
    {
        Vector2 size = FontAssets.MouseText.Value.MeasureString(text) * scale;
        int left = (int)Math.Floor(position.X - size.X * anchorX) - 2;
        int top = (int)Math.Floor(position.Y - size.Y * anchorY) - 2;
        int right = (int)Math.Ceiling(position.X + size.X * (1f - anchorX)) + 2;
        int bottom = (int)Math.Ceiling(position.Y + size.Y * (1f - anchorY)) + 2;
        return new Rectangle(left, top, right - left, bottom - top);
    }

    private static void RenderActualUi(GraphicsDevice device, Type uiType, FieldInfo pendingField,
        object pending, int width, int height, string path)
    {
        object previousPending = pendingField.GetValue(null);
        int previousWidth = Main.screenWidth, previousHeight = Main.screenHeight;
        RenderTargetBinding[] targets = device.GetRenderTargets();
        Viewport viewport = device.Viewport;
        Rectangle scissor = device.ScissorRectangle;
        BlendState blend = device.BlendState;
        DepthStencilState depth = device.DepthStencilState;
        RasterizerState rasterizer = device.RasterizerState;
        SamplerState sampler = device.SamplerStates[0];
        Texture texture = device.Textures[0];
        IndexBuffer indices = device.Indices;
        VertexBufferBinding[] vertices = device.GetVertexBuffers();
        using var target = new RenderTarget2D(device, width, height, false, SurfaceFormat.Color, DepthFormat.None);
        using var batch = new SpriteBatch(device);
        bool begun = false;
        try
        {
            Main.screenWidth = width;
            Main.screenHeight = height;
            pendingField.SetValue(null, pending);
            device.SetRenderTarget(target);
            device.Viewport = new Viewport(0, 0, width, height);
            device.ScissorRectangle = new Rectangle(0, 0, width, height);
            device.Clear(new Color(38, 43, 55));
            var ui = (UIState)Activator.CreateInstance(uiType, nonPublic: true);
            ui.Width.Set(width, 0f);
            ui.Height.Set(height, 0f);
            ui.Activate();
            ui.Recalculate();
            batch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp,
                DepthStencilState.None, RasterizerState.CullNone);
            begun = true;
            ui.Draw(batch);
            batch.End();
            begun = false;
            // A target must be unbound before GPU readback/PNG encoding.
            device.SetRenderTargets(targets);
            using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
            target.SaveAsPng(output, width, height);
        }
        finally
        {
            // Restore production state even if End/readback/restoring GPU state fails.
            pendingField.SetValue(null, previousPending);
            Main.screenWidth = previousWidth;
            Main.screenHeight = previousHeight;
            try { if (begun) batch.End(); }
            finally
            {
                device.SetRenderTargets(targets);
                device.Viewport = viewport;
                device.ScissorRectangle = scissor;
                device.BlendState = blend;
                device.DepthStencilState = depth;
                device.RasterizerState = rasterizer;
                device.SamplerStates[0] = sampler;
                device.Textures[0] = texture;
                device.Indices = indices;
                device.SetVertexBuffers(vertices);
            }
        }
    }

    private static T Read<T>(object value, string name) => (T)(value.GetType().GetProperty(name)?.GetValue(value)
        ?? throw new InvalidOperationException("Missing production layout property: " + name));
    private static object Get(object value, string name) => value.GetType().GetField(name)?.GetValue(value)
        ?? throw new InvalidOperationException("Missing production context field: " + name);
    private static void Set(object value, string name, object fieldValue) =>
        (value.GetType().GetField(name) ?? throw new InvalidOperationException("Missing production context field: " + name))
        .SetValue(value, fieldValue);
}
