using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
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

        private static readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

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
                "You work on a project folder. Take ONE action at a time, observe the result, then decide the next step.\n\n" +
                "PROJECT ROOT: " + _projectRoot + "\n" +
                "PERMISSION MODE: " + PermissionMode + "\n\n" +
                "FILE TREE:\n" + fileTree + "\n\n" +
                "KEY FILE CONTENTS:\n" + contextFiles + "\n\n" +
                "AVAILABLE ACTIONS:\n" +
                "  [READ_FILE:path]                        — Read a file. ALWAYS read before editing.\n" +
                "  [EDIT_FILE:path]                        — Replace an exact string in a file.\n" +
                "     old:<<<old text>>>\n" +
                "     new:<<<new text>>>\n" +
                "     [END_EDIT]\n" +
                "  [WRITE_FILE:path]                       — Create or overwrite a file.\n" +
                "     <<<full content>>>\n" +
                "     [END_WRITE]\n" +
                "  [RUN_CMD:command]                       — Run any shell command in the project folder.\n" +
                "  [GLOB:pattern]                          — Find files by name (e.g. **/*.cs, src/*.ts).\n" +
                "  [GREP:search text]                      — Search file contents for a string.\n" +
                "  [LIST_DIR:path]                         — List files and folders in a directory.\n" +
                "  [DELETE_FILE:path]                      — Delete a file (asks user first).\n" +
                "  [MOVE_FILE:src] [TO:dest]               — Move or rename a file.\n" +
                "  [DOWNLOAD:url] [TO:path]                — Download a URL to a file.\n" +
                "  [WEB_FETCH:url]                         — Fetch a webpage as plain text.\n" +
                "  [GIT:subcommand]                        — Run a git command (status/diff/log/commit/add/push).\n" +
                "  [TODO:item1 | item2 | item3]            — Track a multi-step task.\n" +
                "  [DONE]                                  — Task complete.\n\n" +
                "RULES:\n" +
                "1. For existing files, PREFER [EDIT_FILE] over [WRITE_FILE]. Read first.\n" +
                "2. For new files, use [WRITE_FILE].\n" +
                "3. If the task has 3+ steps, output [TODO:...] first to plan.\n" +
                "4. Never guess file contents. [READ_FILE] or [GREP] first.\n" +
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
                actions.Add(new DexAction { Type = DexActionType.WriteFile, Arg = m.Groups[1].Value.Trim(), Content = m.Groups[2].Value });

            foreach (Match m in Regex.Matches(response, @"\[EDIT_FILE:([^\]]+)\]\s*old:<<<([\s\S]*?)>>>\s*new:<<<([\s\S]*?)>>>\s*\[END_EDIT\]"))
                actions.Add(new DexAction { Type = DexActionType.EditFile, Arg = m.Groups[1].Value.Trim(), OldString = m.Groups[2].Value, Content = m.Groups[3].Value });

            foreach (Match m in Regex.Matches(response, @"\[TODO:([^\]]+)\]"))
                actions.Add(new DexAction { Type = DexActionType.Todo, Arg = m.Groups[1].Value.Trim() });

            foreach (Match m in Regex.Matches(response, @"\[GLOB:([^\]]+)\]"))
                actions.Add(new DexAction { Type = DexActionType.Glob, Arg = m.Groups[1].Value.Trim() });

            foreach (Match m in Regex.Matches(response, @"\[GREP:([^\]]+)\]"))
                actions.Add(new DexAction { Type = DexActionType.Grep, Arg = m.Groups[1].Value.Trim() });

            foreach (Match m in Regex.Matches(response, @"\[LIST_DIR:([^\]]+)\]"))
                actions.Add(new DexAction { Type = DexActionType.ListDir, Arg = m.Groups[1].Value.Trim() });

            foreach (Match m in Regex.Matches(response, @"\[DELETE_FILE:([^\]]+)\]"))
                actions.Add(new DexAction { Type = DexActionType.DeleteFile, Arg = m.Groups[1].Value.Trim() });

            foreach (Match m in Regex.Matches(response, @"\[MOVE_FILE:([^\]]+)\]\s*\[TO:([^\]]+)\]"))
                actions.Add(new DexAction { Type = DexActionType.MoveFile, Arg = m.Groups[1].Value.Trim(), Content = m.Groups[2].Value.Trim() });

            foreach (Match m in Regex.Matches(response, @"\[DOWNLOAD:([^\]]+)\]\s*\[TO:([^\]]+)\]"))
                actions.Add(new DexAction { Type = DexActionType.Download, Arg = m.Groups[1].Value.Trim(), Content = m.Groups[2].Value.Trim() });

            foreach (Match m in Regex.Matches(response, @"\[GIT:([^\]]+)\]"))
                actions.Add(new DexAction { Type = DexActionType.Git, Arg = m.Groups[1].Value.Trim() });

            foreach (Match m in Regex.Matches(response, @"\[WEB_FETCH:([^\]]+)\]"))
                actions.Add(new DexAction { Type = DexActionType.WebFetch, Arg = m.Groups[1].Value.Trim() });

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
                case DexActionType.ReadFile:   return ReadFileSafe(action.Arg);
                case DexActionType.WriteFile:  return await RequestPermissionAsync("Write file", action.Arg) ? WriteFile(action.Arg, action.Content) : "(declined)";
                case DexActionType.EditFile:   return await RequestPermissionAsync("Edit file", action.Arg) ? EditFile(action.Arg, action.OldString, action.Content) : "(declined)";
                case DexActionType.RunCmd:     return await RequestPermissionAsync("Run command", action.Arg) ? await RunCommandAsync(action.Arg, _projectRoot) : "(declined)";
                case DexActionType.Todo:       Report("[todo] " + action.Arg); return "Todo updated: " + action.Arg;
                case DexActionType.Done:       return "[done]";
                case DexActionType.Glob:       return Glob(action.Arg);
                case DexActionType.Grep:       return Grep(action.Arg);
                case DexActionType.ListDir:    return ListDir(action.Arg);
                case DexActionType.DeleteFile: return await RequestPermissionAsync("Delete file", action.Arg) ? DeleteFile(action.Arg) : "(declined)";
                case DexActionType.MoveFile:   return await RequestPermissionAsync("Move file", action.Arg + " → " + action.Content) ? MoveFile(action.Arg, action.Content) : "(declined)";
                case DexActionType.Download:   return await RequestPermissionAsync("Download", action.Arg) ? await DownloadAsync(action.Arg, action.Content) : "(declined)";
                case DexActionType.Git:        return await RequestPermissionAsync("Git", action.Arg) ? await RunCommandAsync("git " + action.Arg, _projectRoot) : "(declined)";
                case DexActionType.WebFetch:   return await WebFetchAsync(action.Arg);
                default: return "";
            }
        }

        // ─────────────────────────────────────────────────────────────
        //  Permission layer
        // ─────────────────────────────────────────────────────────────

        private Task<bool> RequestPermissionAsync(string actionType, string detail)
        {
            if (PermissionMode == DexPermissionMode.Auto)
            {
                Report($"[auto] {actionType}: {detail}");
                return Task.FromResult(true);
            }

            if (PermissionMode == DexPermissionMode.AcceptEdits && actionType != "Run command" && actionType != "Git" && actionType != "Delete file")
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
                if (!File.Exists(full)) { Report($"[read ✗] {relativePath}"); return $"(file not found: {relativePath})"; }
                var text = File.ReadAllText(full);
                if (text.Length > 8000) text = text.Substring(0, 8000) + "\n... (truncated)";
                Report($"[read ✓] {relativePath} ({text.Length} chars)");
                return text;
            }
            catch (Exception ex) { Report($"[read ✗] {relativePath}"); return "(read error: " + ex.Message + ")"; }
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
                return $"Wrote {relativePath}.";
            }
            catch (Exception ex) { Report($"[write ✗] {relativePath}"); return "write error: " + ex.Message; }
        }

        private string EditFile(string relativePath, string oldString, string newString)
        {
            try
            {
                var full = Path.Combine(_projectRoot, relativePath);
                if (!File.Exists(full)) return "(file not found: " + relativePath + ")";

                var content = File.ReadAllText(full);
                oldString = oldString.TrimStart('\r', '\n').TrimEnd();
                newString = newString.TrimStart('\r', '\n').TrimEnd();

                var first = content.IndexOf(oldString, StringComparison.Ordinal);
                if (first == -1) { Report($"[edit ✗] old text not found"); return $"(edit failed: old text not found in {relativePath})"; }

                var second = content.IndexOf(oldString, first + oldString.Length, StringComparison.Ordinal);
                if (second != -1) { Report($"[edit ✗] old text not unique"); return $"(edit failed: old text appears multiple times)"; }

                var updated = content.Substring(0, first) + newString + content.Substring(first + oldString.Length);
                File.WriteAllText(full, updated);
                Report($"[edit ✓] {relativePath}");
                return $"Edited {relativePath}.";
            }
            catch (Exception ex) { return "edit error: " + ex.Message; }
        }

        private string Glob(string pattern)
        {
            try
            {
                // pattern like **/*.cs or src/*.ts — convert to filesystem search
                var searchRoot = _projectRoot;
                var filePattern = pattern;

                if (pattern.Contains("/"))
                {
                    var firstSlash = pattern.IndexOf('/');
                    if (firstSlash > 0 && !pattern.StartsWith("*"))
                    {
                        searchRoot = Path.Combine(_projectRoot, pattern.Substring(0, firstSlash));
                        filePattern = pattern.Substring(firstSlash + 1);
                    }
                }
                filePattern = filePattern.Replace("**/", "").Replace("**", "*");

                if (!Directory.Exists(searchRoot)) return "(no matches: directory not found)";

                var matches = Directory.GetFiles(searchRoot, filePattern, SearchOption.AllDirectories)
                    .Select(f => Path.GetRelativePath(_projectRoot, f).Replace('\\', '/'))
                    .Where(f => !f.Contains("/bin/") && !f.Contains("/obj/") && !f.Contains("/node_modules/") && !f.Contains("/.git/"))
                    .Take(50)
                    .ToList();

                Report($"[glob ✓] {pattern} → {matches.Count} matches");
                return matches.Count == 0 ? "(no matches)" : string.Join("\n", matches);
            }
            catch (Exception ex) { return "glob error: " + ex.Message; }
        }

        private string Grep(string searchText)
        {
            try
            {
                var matches = new List<string>();
                int filesScanned = 0;

                foreach (var file in Directory.GetFiles(_projectRoot, "*.*", SearchOption.AllDirectories))
                {
                    var rel = Path.GetRelativePath(_projectRoot, file).Replace('\\', '/');
                    if (rel.Contains("/bin/") || rel.Contains("/obj/") || rel.Contains("/node_modules/") || rel.Contains("/.git/")) continue;
                    if (filesScanned++ > 300) break;

                    try
                    {
                        var text = File.ReadAllText(file);
                        var lines = text.Split('\n');
                        for (int i = 0; i < lines.Length; i++)
                        {
                            if (lines[i].IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                matches.Add($"{rel}:{i + 1}: {lines[i].Trim()}");
                                if (matches.Count >= 30) break;
                            }
                        }
                    }
                    catch { }

                    if (matches.Count >= 30) break;
                }

                Report($"[grep ✓] \"{searchText}\" → {matches.Count} hits");
                return matches.Count == 0 ? "(no matches)" : string.Join("\n", matches);
            }
            catch (Exception ex) { return "grep error: " + ex.Message; }
        }

        private string ListDir(string relativePath)
        {
            try
            {
                var full = string.IsNullOrWhiteSpace(relativePath) ? _projectRoot : Path.Combine(_projectRoot, relativePath);
                if (!Directory.Exists(full)) return "(directory not found)";

                var sb = new StringBuilder();
                foreach (var dir in Directory.GetDirectories(full))
                    sb.AppendLine("[dir]  " + Path.GetFileName(dir) + "/");
                foreach (var file in Directory.GetFiles(full))
                    sb.AppendLine("[file] " + Path.GetFileName(file) + " (" + new FileInfo(file).Length + " bytes)");

                Report($"[ls ✓] {relativePath}");
                return sb.Length == 0 ? "(empty)" : sb.ToString().Trim();
            }
            catch (Exception ex) { return "ls error: " + ex.Message; }
        }

        private string DeleteFile(string relativePath)
        {
            try
            {
                var full = Path.Combine(_projectRoot, relativePath);
                if (!File.Exists(full)) return "(file not found)";
                File.Delete(full);
                Report($"[delete ✓] {relativePath}");
                return $"Deleted {relativePath}.";
            }
            catch (Exception ex) { return "delete error: " + ex.Message; }
        }

        private string MoveFile(string src, string dest)
        {
            try
            {
                var fullSrc = Path.Combine(_projectRoot, src);
                var fullDest = Path.Combine(_projectRoot, dest);
                if (!File.Exists(fullSrc)) return "(source not found)";
                var dir = Path.GetDirectoryName(fullDest);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                if (File.Exists(fullDest)) File.Delete(fullDest);
                File.Move(fullSrc, fullDest);
                Report($"[move ✓] {src} → {dest}");
                return $"Moved {src} to {dest}.";
            }
            catch (Exception ex) { return "move error: " + ex.Message; }
        }

        // ─────────────────────────────────────────────────────────────
        //  Network operations
        // ─────────────────────────────────────────────────────────────

        private async Task<string> DownloadAsync(string url, string relativePath)
        {
            try
            {
                var full = Path.Combine(_projectRoot, relativePath);
                var dir = Path.GetDirectoryName(full);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                Report($"[download →] {url}");
                var bytes = await _http.GetByteArrayAsync(url);
                File.WriteAllBytes(full, bytes);
                Report($"[download ✓] {relativePath} ({bytes.Length} bytes)");
                return $"Downloaded {url} to {relativePath} ({bytes.Length} bytes).";
            }
            catch (Exception ex) { Report("[download ✗]"); return "download error: " + ex.Message; }
        }

        private async Task<string> WebFetchAsync(string url)
        {
            try
            {
                Report($"[webfetch →] {url}");
                var html = await _http.GetStringAsync(url);

                // Strip scripts, styles, and tags
                html = Regex.Replace(html, @"<script[\s\S]*?</script>", "", RegexOptions.IgnoreCase);
                html = Regex.Replace(html, @"<style[\s\S]*?</style>", "", RegexOptions.IgnoreCase);
                html = Regex.Replace(html, @"<[^>]+>", " ");
                html = Regex.Replace(html, @"\s+", " ").Trim();

                if (html.Length > 4000) html = html.Substring(0, 4000) + "... (truncated)";
                Report($"[webfetch ✓] {html.Length} chars");
                return html;
            }
            catch (Exception ex) { return "webfetch error: " + ex.Message; }
        }

        // ─────────────────────────────────────────────────────────────
        //  Command execution
        // ─────────────────────────────────────────────────────────────

        private async Task<string> RunCommandAsync(string command, string workingDir)
        {
            Report($"[cmd →] {command}");

            return await Task.Run(() =>
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = "/c " + command,
                        WorkingDirectory = workingDir,
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
                        Report($"[cmd ✓] {Trim(combined, 200)}");
                        return combined;
                    }
                }
                catch (Exception ex) { return "(command error: " + ex.Message + ")"; }
            });
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

            return "[COMPACTED SUMMARY]\n" + SummarizeOldPart(oldPart) + "\n\n[RECENT STEPS]\n" + recentPart;
        }

        private string SummarizeOldPart(string oldPart)
        {
            var reads = Regex.Matches(oldPart, @"\[ReadFile: ([^\]]+)\]").Count;
            var writes = Regex.Matches(oldPart, @"\[WriteFile: ([^\]]+)\]").Count;
            var edits = Regex.Matches(oldPart, @"\[EditFile: ([^\]]+)\]").Count;
            var cmds = Regex.Matches(oldPart, @"\[RunCmd: ([^\]]+)\]").Count;
            return $"{reads} reads, {writes} writes, {edits} edits, {cmds} commands. Details compacted.";
        }

        // ─────────────────────────────────────────────────────────────
        //  Project context
        // ─────────────────────────────────────────────────────────────

        private string ReadContextFiles(string root)
        {
            var sb = new StringBuilder();
            var priorityNames = new[]
            {
                "README.md", "readme.md", "package.json", "requirements.txt",
                "pyproject.toml", "Cargo.toml", "go.mod", "AVEIN.csproj"
            };

            foreach (var name in priorityNames)
            {
                var path = Path.Combine(root, name);
                if (File.Exists(path))
                {
                    sb.AppendLine($"── {name} ──");
                    sb.AppendLine(SafeRead(path, 3000));
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
            catch (Exception ex) { return "(error listing files: " + ex.Message + ")"; }
        }

        private string SafeRead(string path, int maxChars)
        {
            try
            {
                var text = File.ReadAllText(path);
                if (text.Length > maxChars) text = text.Substring(0, maxChars) + "\n... (truncated)";
                return text;
            }
            catch (Exception ex) { return "(could not read: " + ex.Message + ")"; }
        }

        private string StripActionBlocks(string response)
        {
            response = Regex.Replace(response, @"\[READ_FILE:[^\]]+\]", "");
            response = Regex.Replace(response, @"\[RUN_CMD:[^\]]+\]", "");
            response = Regex.Replace(response, @"\[WRITE_FILE:[^\]]+\][\s\S]*?\[END_WRITE\]", "");
            response = Regex.Replace(response, @"\[EDIT_FILE:[^\]]+\][\s\S]*?\[END_EDIT\]", "");
            response = Regex.Replace(response, @"\[TODO:[^\]]+\]", "");
            response = Regex.Replace(response, @"\[GLOB:[^\]]+\]", "");
            response = Regex.Replace(response, @"\[GREP:[^\]]+\]", "");
            response = Regex.Replace(response, @"\[LIST_DIR:[^\]]+\]", "");
            response = Regex.Replace(response, @"\[DELETE_FILE:[^\]]+\]", "");
            response = Regex.Replace(response, @"\[MOVE_FILE:[^\]]+\]\s*\[TO:[^\]]+\]", "");
            response = Regex.Replace(response, @"\[DOWNLOAD:[^\]]+\]\s*\[TO:[^\]]+\]", "");
            response = Regex.Replace(response, @"\[GIT:[^\]]+\]", "");
            response = Regex.Replace(response, @"\[WEB_FETCH:[^\]]+\]", "");
            response = Regex.Replace(response, @"\[DONE\]", "", RegexOptions.IgnoreCase);
            return response.Trim();
        }

        private static string Trim(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return s.Length <= max ? s : s.Substring(0, max) + "...";
        }

        private void Report(string msg) => StepReport?.Invoke(msg);
        private static void Log(string msg) => ActivityLogged?.Invoke(DateTime.Now.ToString("HH:mm:ss") + " - [AvenDex] " + msg);
    }
}
