using System;
using System.IO;
using System.Text.Json;
using Terraria;
using Terraria.ID;

namespace DynamicWorldsValidation;

internal sealed record ValidationContext(string RunId, string Root, string ResultPath, int ExpectedSeed);

internal static class ValidationGuard
{
    internal static bool TryGet(out ValidationContext context, out string reason, bool requireWorld = true, bool requireServer = true)
    {
        context = null;
        reason = "Validation is not explicitly enabled.";
        try
        {
            string runId = Environment.GetEnvironmentVariable("DW_VALIDATION_RUN_ID");
            string root = Environment.GetEnvironmentVariable("DW_VALIDATION_ROOT");
            string result = Environment.GetEnvironmentVariable("DW_VALIDATION_RESULT");
            if (string.IsNullOrWhiteSpace(runId) || string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(result)
                || !int.TryParse(Environment.GetEnvironmentVariable("DW_VALIDATION_REGEN_SEED"), out int seed)) return false;
            root = Path.GetFullPath(root);
            result = Path.GetFullPath(result);
            if (root == Path.GetPathRoot(root) || !IsWithin(result, root)) return false;
            // Reject symlinked save roots/world paths so a disposable fixture cannot escape its sandbox.
            if (!NoLinks(root) || !NoLinks(Path.GetDirectoryName(result))) return false;
            string marker = Path.Combine(root, ".dynamic-worlds-validation");
            if (!File.Exists(marker) || new FileInfo(marker).LinkTarget != null || File.ReadAllText(marker).Trim() != runId) return false;
            if (requireServer && (!Main.dedServ || Main.netMode != NetmodeID.Server
                || Environment.GetEnvironmentVariable("DW_VALIDATION_MODE") == "client")) return false;
            if (requireWorld)
            {
                string world = Main.ActiveWorldFileData?.Path;
                if (string.IsNullOrWhiteSpace(world) || !IsWithin(world, Path.Combine(root, "Worlds")) || !NoLinks(world)) return false;
                if (Main.maxTilesX < 1000 || Main.maxTilesY < 300 || WorldGen.gen) return false;
            }
            context = new ValidationContext(runId, root, result, seed);
            reason = null;
            return true;
        }
        catch (Exception ex) { reason = "Validation guard rejected the environment: " + ex.Message; return false; }
    }

    private static bool IsWithin(string child, string parent) => Path.GetFullPath(child).StartsWith(
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent)) + Path.DirectorySeparatorChar, StringComparison.Ordinal);

    private static bool NoLinks(string path)
    {
        for (string current = Path.GetFullPath(path); !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
        {
            FileSystemInfo entry = Directory.Exists(current) ? new DirectoryInfo(current) : new FileInfo(current);
            if (entry.LinkTarget != null) return false;
            if (current == Path.GetPathRoot(current)) break;
        }
        return true;
    }

    internal static void WriteResult(ValidationContext context, object report)
    {
        string temp = context.ResultPath + "." + Environment.ProcessId + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) + "\n");
        File.Move(temp, context.ResultPath, overwrite: true);
    }
}
