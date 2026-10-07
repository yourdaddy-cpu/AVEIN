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
    public enum DexActionType
    {
        ReadFile, WriteFile, EditFile, RunCmd, Todo, Done,
        Glob, Grep, ListDir, DeleteFile, MoveFile, Download, Git, WebFetch
    }

    public enum DexPermissionMode { Default, AcceptEdits, Plan, Auto }

    public sealed class DexAction
    {
        public DexActionType Type { get; set; }
        public string Arg { get; set; } = "";
        public string Content { get; set; }
        public string OldString { get; set; }
    }

    public sealed class AvenDexModule
    {
        private readonly LocalAiModule _ai;
        private readonly string _root;
        public static event Action<string> ActivityLogged;
        public event Action<string> StepReport;
        public DexPermissionMode PermissionMode { get; set; } = DexPermissionMode.Default;

        private static readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        private readonly StringBuilder _history = new StringBuilder();
        private const int MaxIter = 25, MaxCtx = 24000, CompactAt = 20000;

        public AvenDexModule(LocalAiModule ai, string projectRoot) { _ai = ai; _root = projectRoot; }

        public async Task<string> ExecuteCodeTaskAsync(string userRequest)
        {
            if (!Directory.Exists(_root)) return "Project root not found: " + _root;
            _history.Clear();
            Report($"Project: {_root}  |  Mode: {PermissionMode}");

            var sys = BuildSystemPrompt();
            _history.AppendLine("TASK: " + userRequest + "\n");

            string last = "";
            for (int i = 1; i <= MaxIter; i++)
            {
                Report($"── Step {i}/{MaxIter} ──");
                var hist = _history.ToString();
                if (hist.Length > CompactAt) hist = "[earlier steps compacted]\n" + hist.Substring(hist.Length - CompactAt / 2);
                if (hist.Length > MaxCtx) hist = hist.Substring(hist.Length - MaxCtx);

                string resp;
                try { resp = await _ai.AskRawAsync(sys, hist + "\n\nNext action(s)? End with [DONE] when finished.\n", 1200, 0.15f); }
                catch (Exception ex) { return "(agent error: " + ex.Message + ")"; }
                last = resp;

                var said = Strip(resp);
                if (!string.IsNullOrWhiteSpace(said)) Report("AI: " + Trunc(said, 500));

                var actions = Parse(resp);
                if (actions.Count == 0) { Report("(no actions)"); break; }
                if (actions.Any(a => a.Type == DexActionType.Done)) { Report("[DONE]"); break; }

                _history.AppendLine($"--- Turn {i} ---\nAI: {Trunc(said, 400)}");
                foreach (var a in actions)
                {
                    var r = await Run(a);
                    _history.AppendLine($"[{a.Type}: {a.Arg}]");
                    if (!string.IsNullOrWhiteSpace(r)) _history.AppendLine("Result:\n" + Trunc(r, 1500));
                    _history.AppendLine();
                }
            }
            return "Task finished.\n\n" + Strip(last).Trim();
        }

        private string BuildSystemPrompt()
        {
            var tree = BuildTree(); var ctx = ReadContext();
            return
                "You are Aven Dex, an autonomous coding agent inside AVEIN.\n" +
                "One action at a time. Observe result. Decide next.\n\n" +
                "ROOT: " + _root + "\nMODE: " + PermissionMode + "\n\n" +
                "FILE TREE:\n" + tree + "\n\nKEY FILES:\n" + ctx + "\n\n" +
                "ACTIONS (use exactly one of these tag formats):\n" +
                "  [READ_FILE:path]\n" +
                "  [WRITE_FILE:path] <<<content>>> [END_WRITE]\n" +
                "  [EDIT_FILE:path] old:<<<text>>> new:<<<text>>> [END_EDIT]\n" +
                "  [RUN_CMD:command]\n" +
                "  [GLOB:pattern]        e.g. **/*.cs\n" +
                "  [GREP:text]           search file contents\n" +
                "  [LIST_DIR:path]\n" +
                "  [DELETE_FILE:path]\n" +
                "  [MOVE_FILE:src] [TO:dest]\n" +
                "  [DOWNLOAD:url] [TO:path]\n" +
                "  [WEB_FETCH:url]\n" +
                "  [GIT:subcommand]      status/diff/log/add/commit/push\n" +
                "  [TODO:item1 | item2]\n" +
                "  [DONE]\n\n" +
                "RULES:\n" +
                "1. Edit existing files with [EDIT_FILE]. Read first.\n" +
                "2. New files → [WRITE_FILE].\n" +
                "3. 3+ steps → start with [TODO:...].\n" +
                "4. Never guess file contents. Read or grep first.\n" +
                "5. Relative paths only. No markdown fences.\n";
        }

        private List<DexAction> Parse(string r)
        {
            var list = new List<DexAction>();
            void Add(DexActionType t, string a) { if (!string.IsNullOrWhiteSpace(a)) list.Add(new DexAction { Type = t, Arg = a.Trim() }); }

            foreach (Match m in Regex.Matches(r, @"\[READ_FILE:([^\]]+)\]")) Add(DexActionType.ReadFile, m.Groups[1].Value);
            foreach (Match m in Regex.Matches(r, @"\[RUN_CMD:([^\]]+)\]")) Add(DexActionType.RunCmd, m.Groups[1].Value);
            foreach (Match m in Regex.Matches(r, @"\[GLOB:([^\]]+)\]")) Add(DexActionType.Glob, m.Groups[1].Value);
            foreach (Match m in Regex.Matches(r, @"\[GREP:([^\]]+)\]")) Add(DexActionType.Grep, m.Groups[1].Value);
            foreach (Match m in Regex.Matches(r, @"\[LIST_DIR:([^\]]+)\]")) Add(DexActionType.ListDir, m.Groups[1].Value);
            foreach (Match m in Regex.Matches(r, @"\[DELETE_FILE:([^\]]+)\]")) Add(DexActionType.DeleteFile, m.Groups[1].Value);
            foreach (Match m in Regex.Matches(r, @"\[GIT:([^\]]+)\]")) Add(DexActionType.Git, m.Groups[1].Value);
            foreach (Match m in Regex.Matches(r, @"\[WEB_FETCH:([^\]]+)\]")) Add(DexActionType.WebFetch, m.Groups[1].Value);
            foreach (Match m in Regex.Matches(r, @"\[TODO:([^\]]+)\]")) Add(DexActionType.Todo, m.Groups[1].Value);

            foreach (Match m in Regex.Matches(r, @"\[WRITE_FILE:([^\]]+)\]\s*<<<([\s\S]*?)>>>\s*\[END_WRITE\]"))
                list.Add(new DexAction { Type = DexActionType.WriteFile, Arg = m.Groups[1].Value.Trim(), Content = m.Groups[2].Value });

            foreach (Match m in Regex.Matches(r, @"\[EDIT_FILE:([^\]]+)\]\s*old:<<<([\s\S]*?)>>>\s*new:<<<([\s\S]*?)>>>\s*\[END_EDIT\]"))
                list.Add(new DexAction { Type = DexActionType.EditFile, Arg = m.Groups[1].Value.Trim(), OldString = m.Groups[2].Value, Content = m.Groups[3].Value });

            foreach (Match m in Regex.Matches(r, @"\[MOVE_FILE:([^\]]+)\]\s*\[TO:([^\]]+)\]"))
                list.Add(new DexAction { Type = DexActionType.MoveFile, Arg = m.Groups[1].Value.Trim(), Content = m.Groups[2].Value.Trim() });

            foreach (Match m in Regex.Matches(r, @"\[DOWNLOAD:([^\]]+)\]\s*\[TO:([^\]]+)\]"))
                list.Add(new DexAction { Type = DexActionType.Download, Arg = m.Groups[1].Value.Trim(), Content = m.Groups[2].Value.Trim() });

            if (Regex.IsMatch(r, @"\[DONE\]", RegexOptions.IgnoreCase)) list.Add(new DexAction { Type = DexActionType.Done });
            return list;
        }

        private async Task<string> Run(DexAction a)
        {
            switch (a.Type)
            {
                case DexActionType.ReadFile:   return ReadFile(a.Arg);
                case DexActionType.Glob:       return Glob(a.Arg);
                case DexActionType.Grep:       return Grep(a.Arg);
                case DexActionType.ListDir:    return ListDir(a.Arg);
                case DexActionType.WebFetch:   return await WebFetch(a.Arg);
                case DexActionType.Todo:       Report("[todo] " + a.Arg); return "ok";
                case DexActionType.Done:       return "done";
                case DexActionType.WriteFile:  return await Ask("Write file", a.Arg) ? WriteFile(a.Arg, a.Content) : "(declined)";
                case DexActionType.EditFile:   return await Ask("Edit file", a.Arg) ? EditFile(a.Arg, a.OldString, a.Content) : "(declined)";
                case DexActionType.RunCmd:     return await Ask("Run command", a.Arg) ? await Cmd(a.Arg) : "(declined)";
                case DexActionType.Git:        return await Ask("Git", a.Arg) ? await Cmd("git " + a.Arg) : "(declined)";
                case DexActionType.DeleteFile: return await Ask("Delete file", a.Arg) ? DelFile(a.Arg) : "(declined)";
                case DexActionType.MoveFile:   return await Ask("Move file", a.Arg + " → " + a.Content) ? MoveFile(a.Arg, a.Content) : "(declined)";
                case DexActionType.Download:   return await Ask("Download", a.Arg + " → " + a.Content) ? await Download(a.Arg, a.Content) : "(declined)";
                default: return "";
            }
        }

        private Task<bool> Ask(string what, string detail)
        {
            if (PermissionMode == DexPermissionMode.Auto) { Report($"[auto] {what}"); return Task.FromResult(true); }
            if (PermissionMode == DexPermissionMode.AcceptEdits && what != "Run command" && what != "Git" && what != "Delete file") { Report($"[accept] {what}"); return Task.FromResult(true); }
            if (PermissionMode == DexPermissionMode.Plan) { Report($"[plan-block] {what}"); return Task.FromResult(false); }
            var r = System.Windows.MessageBox.Show($"Aven Dex wants to {what.ToLower()}:\n\n{detail}\n\nAllow?", "Aven Dex", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);
            return Task.FromResult(r == System.Windows.MessageBoxResult.Yes);
        }

        private string ReadFile(string p)
        {
            try { var f = Path.Combine(_root, p); if (!File.Exists(f)) { Report($"[read ✗] {p}"); return "(not found)"; }
                var t = File.ReadAllText(f); if (t.Length > 8000) t = t.Substring(0, 8000) + "\n...";
                Report($"[read ✓] {p} ({t.Length})"); return t; }
            catch (Exception e) { return "err: " + e.Message; }
        }

        private string WriteFile(string p, string c)
        {
            try { var f = Path.Combine(_root, p); var d = Path.GetDirectoryName(f); if (!string.IsNullOrEmpty(d)) Directory.CreateDirectory(d);
                c = (c ?? "").TrimStart('\r', '\n').TrimEnd(); File.WriteAllText(f, c);
                Report($"[write ✓] {p} ({c.Length})"); return $"Wrote {p}."; }
            catch (Exception e) { return "err: " + e.Message; }
        }

        private string EditFile(string p, string oldS, string newS)
        {
            try { var f = Path.Combine(_root, p); if (!File.Exists(f)) return "(not found)";
                var c = File.ReadAllText(f); oldS = (oldS ?? "").TrimStart('\r', '\n').TrimEnd(); newS = (newS ?? "").TrimStart('\r', '\n').TrimEnd();
                var i1 = c.IndexOf(oldS, StringComparison.Ordinal); if (i1 < 0) return "(old text not found)";
                var i2 = c.IndexOf(oldS, i1 + oldS.Length, StringComparison.Ordinal); if (i2 >= 0) return "(old text appears multiple times)";
                File.WriteAllText(f, c.Substring(0, i1) + newS + c.Substring(i1 + oldS.Length));
                Report($"[edit ✓] {p}"); return $"Edited {p}."; }
            catch (Exception e) { return "err: " + e.Message; }
        }

        private string Glob(string pattern)
        {
            try { var dir = _root; var fp = pattern;
                if (pattern.Contains("/") && !pattern.StartsWith("*")) { var s = pattern.IndexOf('/'); dir = Path.Combine(_root, pattern.Substring(0, s)); fp = pattern.Substring(s + 1); }
                fp = fp.Replace("**/", "").Replace("**", "*");
                if (!Directory.Exists(dir)) return "(no matches)";
                var m = Directory.GetFiles(dir, fp, SearchOption.AllDirectories)
                    .Select(f => Path.GetRelativePath(_root, f).Replace('\\', '/'))
                    .Where(f => !f.Contains("/bin/") && !f.Contains("/obj/") && !f.Contains("/node_modules/") && !f.Contains("/.git/"))
                    .Take(50).ToList();
                Report($"[glob ✓] {pattern} → {m.Count}");
                return m.Count == 0 ? "(no matches)" : string.Join("\n", m); }
            catch (Exception e) { return "err: " + e.Message; }
        }

        private string Grep(string text)
        {
            try { var hits = new List<string>(); int n = 0;
                foreach (var f in Directory.GetFiles(_root, "*.*", SearchOption.AllDirectories))
                {
                    var rel = Path.GetRelativePath(_root, f).Replace('\\', '/');
                    if (rel.Contains("/bin/") || rel.Contains("/obj/") || rel.Contains("/node_modules/") || rel.Contains("/.git/")) continue;
                    if (n++ > 300) break;
                    try { var lines = File.ReadAllText(f).Split('\n');
                        for (int i = 0; i < lines.Length; i++)
                            if (lines[i].IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0) { hits.Add($"{rel}:{i + 1}: {lines[i].Trim()}"); if (hits.Count >= 30) break; }
                    } catch { }
                    if (hits.Count >= 30) break;
                }
                Report($"[grep ✓] \"{text}\" → {hits.Count}");
                return hits.Count == 0 ? "(no matches)" : string.Join("\n", hits); }
            catch (Exception e) { return "err: " + e.Message; }
        }

        private string ListDir(string p)
        {
            try { var f = string.IsNullOrWhiteSpace(p) ? _root : Path.Combine(_root, p); if (!Directory.Exists(f)) return "(not found)";
                var sb = new StringBuilder();
                foreach (var d in Directory.GetDirectories(f)) sb.AppendLine("[dir] " + Path.GetFileName(d) + "/");
                foreach (var x in Directory.GetFiles(f)) sb.AppendLine("[file] " + Path.GetFileName(x) + " (" + new FileInfo(x).Length + "b)");
                Report($"[ls ✓] {p}"); return sb.Length == 0 ? "(empty)" : sb.ToString().Trim(); }
            catch (Exception e) { return "err: " + e.Message; }
        }

        private string DelFile(string p)
        {
            try { var f = Path.Combine(_root, p); if (!File.Exists(f)) return "(not found)"; File.Delete(f); Report($"[del ✓] {p}"); return "Deleted " + p; }
            catch (Exception e) { return "err: " + e.Message; }
        }

        private string MoveFile(string s, string d)
        {
            try { var fs = Path.Combine(_root, s); var fd = Path.Combine(_root, d); if (!File.Exists(fs)) return "(not found)";
                var dir = Path.GetDirectoryName(fd); if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                if (File.Exists(fd)) File.Delete(fd); File.Move(fs, fd); Report($"[mv ✓] {s} → {d}"); return "Moved."; }
            catch (Exception e) { return "err: " + e.Message; }
        }

        private async Task<string> Download(string url, string p)
        {
            try { var f = Path.Combine(_root, p); var d = Path.GetDirectoryName(f); if (!string.IsNullOrEmpty(d)) Directory.CreateDirectory(d);
                Report($"[dl →] {url}"); var b = await _http.GetByteArrayAsync(url); File.WriteAllBytes(f, b);
                Report($"[dl ✓] {p} ({b.Length}b)"); return $"Downloaded ({b.Length} bytes)."; }
            catch (Exception e) { return "err: " + e.Message; }
        }

        private async Task<string> WebFetch(string url)
        {
            try { Report($"[fetch →] {url}"); var h = await _http.GetStringAsync(url);
                h = Regex.Replace(h, @"<script[\s\S]*?</script>", "", RegexOptions.IgnoreCase);
                h = Regex.Replace(h, @"<style[\s\S]*?</style>", "", RegexOptions.IgnoreCase);
                h = Regex.Replace(h, @"<[^>]+>", " "); h = Regex.Replace(h, @"\s+", " ").Trim();
                if (h.Length > 4000) h = h.Substring(0, 4000) + "...";
                Report($"[fetch ✓] {h.Length}c"); return h; }
            catch (Exception e) { return "err: " + e.Message; }
        }

        private async Task<string> Cmd(string cmd)
        {
            Report($"[cmd →] {cmd}");
            return await Task.Run(() =>
            {
                try { var psi = new ProcessStartInfo { FileName = "cmd.exe", Arguments = "/c " + cmd, WorkingDirectory = _root,
                        RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
                    using (var pr = Process.Start(psi))
                    { var o = pr.StandardOutput.ReadToEnd() + "\n" + pr.StandardError.ReadToEnd();
                        pr.WaitForExit(300000); o = o.Trim(); if (o.Length > 4000) o = o.Substring(0, 4000) + "...";
                        Report($"[cmd ✓] {Trunc(o, 200)}"); return o; } }
                catch (Exception e) { return "err: " + e.Message; }
            });
        }

        private string BuildTree()
        {
            try { var sb = new StringBuilder(); int n = 0;
                foreach (var f in Directory.GetFiles(_root, "*", SearchOption.AllDirectories))
                { var r = Path.GetRelativePath(_root, f);
                    if (r.Contains("\\bin\\") || r.Contains("\\obj\\") || r.Contains("\\node_modules\\") || r.Contains("\\.git\\")) continue;
                    if (n++ >= 300) { sb.AppendLine("..."); break; }
                    sb.AppendLine("- " + r.Replace('\\', '/')); }
                return sb.ToString().Trim(); }
            catch (Exception e) { return "(err: " + e.Message + ")"; }
        }

        private string ReadContext()
        {
            var sb = new StringBuilder();
            var names = new[] { "README.md", "readme.md", "package.json", "requirements.txt", "pyproject.toml", "Cargo.toml", "go.mod", "AVEIN.csproj" };
            foreach (var n in names) { var p = Path.Combine(_root, n);
                if (File.Exists(p)) { sb.AppendLine("── " + n + " ──"); var c = SafeRead(p, 3000); sb.AppendLine(c); sb.AppendLine(); } }
            return sb.Length == 0 ? "(no config files)" : sb.ToString();
        }

        private string SafeRead(string p, int max)
        {
            try { var t = File.ReadAllText(p); if (t.Length > max) t = t.Substring(0, max) + "..."; return t; }
            catch (Exception e) { return "(err: " + e.Message + ")"; }
        }

        private string Strip(string r)
        {
            r = Regex.Replace(r, @"\[(READ_FILE|RUN_CMD|GLOB|GREP|LIST_DIR|DELETE_FILE|GIT|WEB_FETCH|TODO):[^\]]+\]", "");
            r = Regex.Replace(r, @"\[(WRITE_FILE|EDIT_FILE):[^\]]+\][\s\S]*?\[END_(WRITE|EDIT)\]", "");
            r = Regex.Replace(r, @"\[MOVE_FILE:[^\]]+\]\s*\[TO:[^\]]+\]", "");
            r = Regex.Replace(r, @"\[DOWNLOAD:[^\]]+\]\s*\[TO:[^\]]+\]", "");
            r = Regex.Replace(r, @"\[DONE\]", "", RegexOptions.IgnoreCase);
            return r.Trim();
        }

        private static string Trunc(string s, int n) => string.IsNullOrEmpty(s) ? s : (s.Length <= n ? s : s.Substring(0, n) + "...");
        private void Report(string m) => StepReport?.Invoke(m);
    }
}
