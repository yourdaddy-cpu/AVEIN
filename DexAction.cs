using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace AVEIN
{
    public enum DexPermissionMode
    {
        Default,
        AcceptEdits,
        Plan,
        Auto
    }

    public sealed class AvenDexModule
    {
        private readonly LocalAiModule _ai;
        private readonly string _projectRoot;

        public static event Action<string> ActivityLogged;
        public event Action<string> StepReport;

        public DexPermissionMode PermissionMode { get; set; } = DexPermissionMode.Default;

        private const int MaxIterations = 25;
        private const int MaxContextChars = 24000;
        private const int CompactTriggerChars = 20000;

        private readonly StringBuilder _conversationHistory = new StringBuilder();

        public AvenDexModule(LocalAiModule ai, string projectRoot)
        {
            _ai = ai;
            _projectRoot = projectRoot;
        }

        // ─────────────────────────────────────────────────────────────
        //  Main agent loop
        // ─────────────────────────────────────────────────────────────

        public async Task<string> ExecuteCodeTaskAsync(string userRequest)
        {
            if (!Directory.Exists(_projectRoot))
                return $"Project root not found: {_projectRoot}";

            _conversationHistory.Clear();

            Report($"Project: {_projectRoot}");
            Report($"Permission mode: {PermissionMode}");

            var fileTree = BuildFileTree(_projectRoot, 300);
            var contextFiles = ReadContextFiles(_projectRoot);
            var systemPrompt = BuildSystemPrompt(fileTree, contextFiles);

            _conversationHistory.AppendLine("TASK: " + userRequest);
            _conversationHistory.AppendLine();

            string lastResponse = "";
            bool taskDone = false;

            for (int iter = 1; iter <= MaxIterations && !taskDone; iter++)
            {
                Report($"── Step {iter}/{MaxIterations} ──");

                var history = _conversationHistory.ToString();
                if (history.Length > CompactTriggerChars)
                {
                    history = CompactHistory(history);
                    Report("[compact] History compacted.");
                }
                if (history.Length > MaxContextChars)
                    history = history.Substring(history.Length - MaxContextChars);

                var fullPrompt = history + "\n\nNow decide your next action(s). Use the tags. End with [DONE] when finished.\n";

                string response;
                try
                {
                    response = await _ai.AskRawAsync(systemPrompt, fullPrompt, 1200, 0.15f);
                }
                catch (Exception ex)
                {
                    return "(agent error: " + ex.Message + ")";
                }

                lastResponse = response;

                var said = StripActionBlocks(response);
                if (!string.IsNullOrWhiteSpace(said))
                    Report("AI: " + Trim(said, 500));

                var actions = ParseActions(response);
                if (actions.Count == 0)
                {
                    Report("(no actions detected — stopping)");
                    break;
                }

                if (actions.Exists(a => a.Type == DexActionType.Done))
                {
                    Report("Agent signalled [DONE].");
                    taskDone = true;
                    break;
                }

                _conversationHistory.AppendLine($"--- Turn {iter} ---");
                _conversationHistory.AppendLine("AI said: " + Trim(said, 400));

                foreach (var action in actions)
                {
                    var result = await ExecuteActionAsync(action);
                    _conversationHistory.AppendLine($"[{action.Type}: {action.Arg}]");
                    if (!string.IsNullOrWhiteSpace(result))
                    {
                        _conversationHistory.AppendLine("Result:");
                        _conversationHistory.AppendLine(Trim(result, 1500));
                    }
                    _conversationHistory.AppendLine();
                }
            }

            return "Task finished.\n\n" + StripActionBlocks(lastResponse).Trim();
        }

        // ─────────────────────────────────────────────────────────────
        //  System prompt
        // ─────────────────────────────────────────────────────────────

        private string BuildSystemPrompt(string fileTree, string contextFiles)
        {
            return
                "You are Aven Dex, an autonomous coding agent inside the AVEIN app.\n" +
                "You work on a project folder. You take ONE action at a time, observe the result, then decide the next step.\n\n" +
                "PROJECT ROOT: " + _projectRoot + "\n" +
                "PERMISSION MODE: " + PermissionMode + "\n\n" +
                "FILE TREE:\n" + fileTree + "\n\n" +
                "KEY FILE CONTENTS:\n" + contextFiles + "\n\n" +
                "AVAILABLE ACTIONS:\n" +
                "  [READ_FILE:path]                        — Read a file. ALWAYS read before editing.\n" +
                "  [EDIT_FILE:path]                        — Replace an exact string in a file.\n" +
                "     old:<<<old text here>>>\n" +
                "     new:<<<new text here>>>\n" +
                "     [END_EDIT]\n" +
                "  [WRITE_FILE:path]                       — Create or overwrite a file.\n" +
                "     <<<full file content>>>\n" +
                "     [END_WRITE]\n" +
                "  [RUN_CMD:command]                       — Run a shell command in the project folder.\n" +
                "  [TODO:item1 | item2 | item3]            — Track a multi-step task list.\n" +
                "  [DONE]                                  — Task complete.\n\n" +
                "RULES:\n" +
                "1. For existing files, PREFER [EDIT_FILE] over [WRITE_FILE]. Read first, then edit.\n" +
                "2. For new files, use [WRITE_FILE].\n" +
                "3. If the task has 3+ steps, output [TODO:...] first to plan.\n" +
                "4. Never guess file contents. [READ_FILE] first.\n" +
                "5. Use relative paths only.\n" +
                "6. Do not use markdown code fences around tags.\n" +
                "7. When done, output [DONE] on its own line.\n";
        }

        // ─────────────────────────────────────────────────────────────
        //  Action parsing
        // ─────────────────────────────────────────────────────────────

        private List<DexAction> ParseActions(string response)
        {
            var actions = new List<DexAction>();

            foreach (Match m in Regex.Matches(response, @"\[READ_FILE:([^\]]+)\]"))
                actions.Add(new DexAction { Type = DexActionType.ReadFile, Arg = m.Groups[1].Value.Trim() });

            foreach (Match m in Regex.Matches(response, @"\[RUN_CMD:([^\]]+)\]"))
                actions.Add(new DexAction { Type = DexActionType.RunCmd, Arg = m.Groups[1].Value.Trim() });

            foreach (Match m in Regex.Matches(response, @"\[WRITE_FILE:([^\]]+)\]\s*<<<([\s\S]*?)>>>\s*\[END_WRITE\]"))
            {
                actions.Add(new DexAction
                {
                    Type = DexActionType.WriteFile,
                    Arg = m.Groups[1].Value.Trim(),
                    Content = m.Groups[2].Value
                });
            }

            foreach (Match m in Regex.Matches(response, @"\[EDIT_FILE:([^\]]+)\]\s*old:<<<([\s\S]*?)>>>\s*new:<<<([\s\S]*?)>>>\s*\[END_EDIT\]"))
            {
                actions.Add(new DexAction
                {
                    Type = DexActionType.EditFile,
                    Arg = m.Groups[1].Value.Trim(),
                    OldString = m.Groups[2].Value,
                    Content = m.Groups[3].Value
                });
            }

            foreach (Match m in Regex.Matches(response, @"\[TODO:([^\]]+)\]"))
            {
                actions.Add(new DexAction
                {
                    Type = DexActionType.Todo,
                    Arg = m.Groups[1].Value.Trim()
                });
            }

            if (Regex.IsMatch(response, @"\[DONE\]", RegexOptions.IgnoreCase))
                actions.Add(new DexAction { Type = DexActionType.Done, Arg = "" });

            return actions;
        }

        // ─────────────────────────────────────────────────────────────
        //  Action execution
        // ─────────────────────────────────────────────────────────────

        private async Task<string> ExecuteActionAsync(DexAction action)
        {
            switch (action.Type)
            {
                case DexActionType.ReadFile:
                    return ReadFileSafe(action.Arg);

                case DexActionType.WriteFile:
                    if (!await RequestPermissionAsync("Write file", action.Arg)) return "(declined)";
                    return WriteFile(action.Arg, action.Content);

                case DexActionType.EditFile:
                    if (!await RequestPermissionAsync("Edit file", action.Arg)) return "(declined)";
                    return EditFile(action.Arg, action.OldString, action.Content);

                case DexActionType.RunCmd:
                    if (!await RequestPermissionAsync("Run command", action.Arg)) return "(declined)";
                    return await RunCommandAsync(action.Arg);

                case DexActionType.Todo:
                    Report("[todo] " + action.Arg);
                    return "Todo list updated: " + action.Arg;

                case DexActionType.Done:
                    return "[done]";

                default:
                    return "";
            }
        }

        // ─────────────────────────────────────────────────────────────
        //  Permission layer
        // ─────────────────────────────────────────────────────────────

        private Task<bool> RequestPermissionAsync(string actionType, string detail)
        {
            if (PermissionMode == DexPermissionMode.Auto)
            {
                Report($"[auto-approved] {actionType}: {detail}");
                return Task.FromResult(true);
            }

            if (PermissionMode == DexPermissionMode.AcceptEdits && actionType != "Run command")
            {
                Report($"[accept-edits] {actionType}: {detail}");
                return Task.FromResult(true);
            }

            if (PermissionMode == DexPermissionMode.Plan)
            {
                Report($"[plan-blocked] {actionType}: {detail}");
                return Task.FromResult(false);
            }

            var result = System.Windows.MessageBox.Show(
                $"Aven Dex wants to {actionType.ToLower()}:\n\n{detail}\n\nAllow?",
                "Aven Dex — Permission",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Question);

            return Task.FromResult(result == System.Windows.MessageBoxResult.Yes);
        }

        // ─────────────────────────────────────────────────────────────
        //  File operations
        // ─────────────────────────────────────────────────────────────

        private string ReadFileSafe(string relativePath)
        {
            try
            {
                var full = Path.Combine(_projectRoot, relativePath);
                if (!File.Exists(full))
                {
                    Report($"[read ✗] {relativePath} — not found");
                    return $"(file not found: {relativePath})";
                }

                var text = File.ReadAllText(full);
                if (text.Length > 8000) text = text.Substring(0, 8000) + "\n... (truncated at 8000 chars)";
                Report($"[read ✓] {relativePath} ({text.Length} chars)");
                return text;
            }
            catch (Exception ex)
            {
                Report($"[read ✗] {relativePath} — {ex.Message}");
                return "(read error: " + ex.Message + ")";
            }
        }

        private string WriteFile(string relativePath, string content)
        {
            try
            {
                var full = Path.Combine(_projectRoot, relativePath);
                var dir = Path.GetDirectoryName(full);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                content = content.TrimStart('\r', '\n').TrimEnd();
                File.WriteAllText(full, content);

                Report($"[write ✓] {relativePath} ({content.Length} chars)");
                return $"Wrote {relativePath} ({content.Length} chars).";
            }
            catch (Exception ex)
            {
                Report($"[write ✗] {relativePath} — {ex.Message}");
                return "write error: " + ex.Message;
            }
        }

        private string EditFile(string relativePath, string oldString, string newString)
        {
            try
            {
                var full = Path.Combine(_projectRoot, relativePath);
                if (!File.Exists(full))
                {
                    Report($"[edit ✗] {relativePath} — file not found");
                    return "(file not found: " + relativePath + ")";
                }

                var content = File.ReadAllText(full);
                oldString = oldString.TrimStart('\r', '\n').TrimEnd();
                newString = newString.TrimStart('\r', '\n').TrimEnd();

                var firstIndex = content.IndexOf(oldString, StringComparison.Ordinal);
                if (firstIndex == -1)
                {
                    Report($"[edit ✗] {relativePath} — old string not found");
                    return $"(edit failed: the exact old text was not found in {relativePath}. Read the file again and check for whitespace differences.)";
                }

                var secondIndex = content.IndexOf(oldString, firstIndex + oldString.Length, StringComparison.Ordinal);
                if (secondIndex != -1)
                {
                    Report($"[edit ✗] {relativePath} — old string appears multiple times");
                    return $"(edit failed: the old text appears more than once in {relativePath}. Include more surrounding context to make it unique.)";
                }

                var updated = content.Substring(0, firstIndex) + newString + content.Substring(firstIndex + oldString.Length);
                File.WriteAllText(full, updated);

                Report($"[edit ✓] {relativePath} (replaced {oldString.Length} chars with {newString.Length})");
                return $"Edited {relativePath}: replaced {oldString.Length} chars with {newString.Length} chars.";
            }
            catch (Exception ex)
            {
                Report($"[edit ✗] {relativePath} — {ex.Message}");
                return "edit error: " + ex.Message;
            }
        }

        // ─────────────────────────────────────────────────────────────
        //  Command execution
        // ─────────────────────────────────────────────────────────────

        private async Task<string> RunCommandAsync(string command)
        {
            Report($"[cmd →] {command}");

            var output = await Task.Run(() =>
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = "/c " + command,
                        WorkingDirectory = _projectRoot,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    using (var proc = Process.Start(psi))
                    {
                        var stdout = proc.StandardOutput.ReadToEnd();
                        var stderr = proc.StandardError.ReadToEnd();
                        proc.WaitForExit(300000);

                        var combined = (stdout + "\n" + stderr).Trim();
                        if (combined.Length > 4000) combined = combined.Substring(0, 4000) + "\n... (truncated)";
                        return combined;
                    }
                }
                catch (Exception ex)
                {
                    return "(command error: " + ex.Message + ")";
                }
            });

            Report($"[cmd ✓] {Trim(output, 300)}");
            return output;
        }

        // ─────────────────────────────────────────────────────────────
        //  Context compaction
        // ─────────────────────────────────────────────────────────────

        private string CompactHistory(string history)
        {
            var lines = history.Split('\n');
            if (lines.Length < 20) return history;

            var keepCount = lines.Length * 2 / 5;
            var oldPart = string.Join("\n", lines.Take(lines.Length - keepCount));
            var recentPart = string.Join("\n", lines.Skip(lines.Length - keepCount));

            var summary = SummarizeOldPart(oldPart);
            return "[COMPACTED HISTORY SUMMARY]\n" + summary + "\n\n[RECENT STEPS]\n" + recentPart;
        }

        private string SummarizeOldPart(string oldPart)
        {
            var reads = Regex.Matches(oldPart, @"\[ReadFile: ([^\]]+)\]").Count;
            var writes = Regex.Matches(oldPart, @"\[WriteFile: ([^\]]+)\]").Count;
            var edits = Regex.Matches(oldPart, @"\[EditFile: ([^\]]+)\]").Count;
            var cmds = Regex.Matches(oldPart, @"\[RunCmd: ([^\]]+)\]").Count;

            return $"Earlier in this session: {reads} file reads, {writes} file writes, {edits} edits, {cmds} commands run. " +
                   "Detailed content of these steps has been compacted to save context. " +
                   "Refer to the file tree and recent steps for current state.";
        }

        // ─────────────────────────────────────────────────────────────
        //  Project context
        // ─────────────────────────────────────────────────────────────

        private string ReadContextFiles(string root)
        {
            var sb = new StringBuilder();
            var priorityNames = new[]
            {
                "README.md", "readme.md", "Readme.md",
                "package.json", "requirements.txt", "pyproject.toml",
                "Cargo.toml", "go.mod", "AVEIN.csproj"
            };

            foreach (var name in priorityNames)
            {
                var path = Path.Combine(root, name);
                if (File.Exists(path))
                {
                    var content = SafeRead(path, 3000);
                    sb.AppendLine($"── {name} ──");
                    sb.AppendLine(content);
                    sb.AppendLine();
                }
            }

            return sb.Length == 0 ? "(no README or config files found)" : sb.ToString();
        }

        private string BuildFileTree(string root, int maxFiles)
        {
            try
            {
                var sb = new StringBuilder();
                int count = 0;
                foreach (var file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
                {
                    var rel = Path.GetRelativePath(root, file);
                    if (rel.Contains("\\bin\\") || rel.Contains("\\obj\\") || rel.Contains("\\node_modules\\") || rel.Contains("\\.git\\")) continue;
                    if (count++ >= maxFiles) { sb.AppendLine("... (truncated)"); break; }
                    sb.AppendLine("- " + rel.Replace('\\', '/'));
                }
                return sb.ToString().Trim();
            }
            catch (Exception ex)
                
