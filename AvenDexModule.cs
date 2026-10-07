using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace AVEIN
{
    public sealed class AvenDexModule
    {
        private readonly LocalAiModule _ai;
        private readonly string _projectRoot;

        public static event Action<string> ActivityLogged;

        public AvenDexModule(LocalAiModule ai, string projectRoot)
        {
            _ai = ai;
            _projectRoot = projectRoot;
        }

        public async Task<string> ExecuteCodeTaskAsync(string userRequest)
        {
            if (!Directory.Exists(_projectRoot))
                return $"Project root not found: {_projectRoot}";

            // Build a lightweight file tree (max 200 files to keep prompt small)
            var fileList = BuildFileTree(_projectRoot, 200);

            var systemPrompt =
                "You are Aven Dex, an expert AI coding assistant integrated into the AVEIN application.\n" +
                $"Project root: {_projectRoot}\n\n" +
                "Files in project:\n" + fileList + "\n\n" +
                "RULES:\n" +
                "1. Explain your plan in plain English first.\n" +
                "2. When you want to write or modify a file, output it using EXACTLY this format:\n" +
                "   [FILE_WRITE:relative/path/file.ext]\n" +
                "   <file content here>\n" +
                "   [END_FILE_WRITE]\n" +
                "3. Do NOT use markdown code fences. Do NOT wrap the block in ```.\n" +
                "4. Only output [FILE_WRITE] blocks for files you actually want to change.\n" +
                "5. Keep explanations concise.\n";

            var rawResponse = await _ai.AskAsync(userRequest, "Phi4-mini", false);

            var applied = ApplyFileWrites(rawResponse);
            return string.IsNullOrEmpty(applied)
                ? rawResponse
                : rawResponse + "\n\n── Actions taken ──\n" + applied;
        }

        private string BuildFileTree(string root, int maxFiles)
        {
            try
            {
                var sb = new System.Text.StringBuilder();
                int count = 0;
                foreach (var file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
                {
                    if (count++ >= maxFiles) { sb.AppendLine("... (truncated)"); break; }
                    if (file.Contains("\\bin\\") || file.Contains("\\obj\\") || file.Contains("\\.git\\")) continue;
                    sb.AppendLine("- " + Path.GetRelativePath(root, file).Replace('\\', '/'));
                }
                return sb.ToString().Trim();
            }
            catch (Exception ex)
            {
                return "(error listing files: " + ex.Message + ")";
            }
        }

        private string ApplyFileWrites(string response)
        {
            var result = "";
            const string startTag = "[FILE_WRITE:";
            const string endTag = "[END_FILE_WRITE]";

            int startIndex = response.IndexOf(startTag, StringComparison.Ordinal);
            while (startIndex != -1)
            {
                int pathEnd = response.IndexOf(']', startIndex);
                if (pathEnd == -1) break;

                int endIndex = response.IndexOf(endTag, pathEnd, StringComparison.Ordinal);
                if (endIndex == -1) break;

                string relativePath = response.Substring(startIndex + startTag.Length,
                    pathEnd - startIndex - startTag.Length).Trim();

                string content = response.Substring(pathEnd + 1, endIndex - pathEnd - 1);
                content = content.TrimStart('\r', '\n').TrimEnd();

                try
                {
                    var fullPath = Path.Combine(_projectRoot, relativePath);
                    var dir = Path.GetDirectoryName(fullPath);
                    if (!string.IsNullOrEmpty(dir))
                        Directory.CreateDirectory(dir);

                    File.WriteAllText(fullPath, content);
                    result += $"[✓] wrote {relativePath} ({content.Length} chars)\n";
                    Log("Wrote file: " + relativePath);
                }
                catch (Exception ex)
                {
                    result += $"[✗] failed {relativePath}: {ex.Message}\n";
                    Log("Failed to write: " + relativePath + " - " + ex.Message);
                }

                startIndex = response.IndexOf(startTag, endIndex, StringComparison.Ordinal);
            }

            return result.Trim();
        }

        private static void Log(string message)
        {
            ActivityLogged?.Invoke(DateTime.Now.ToString("HH:mm:ss") + " - [AvenDex] " + message);
        }
    }
}
