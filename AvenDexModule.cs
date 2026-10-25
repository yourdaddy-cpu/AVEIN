using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace AVEIN
{
    public enum DexActionType
    {
        ReadFile, WriteFile, EditFile, RunCmd, Todo, Done,
        Glob, Grep, ListDir, DeleteFile, MoveFile, Download, Git, WebFetch, WebSearch, SubTask
    }

    public enum DexPermissionMode { Default, AcceptEdits, Plan, Auto }

    public sealed class DexAction
    {
        public DexActionType Type { get; set; }
        public string Arg { get; set; } = "";
        public string Content { get; set; }
        public string OldString { get; set; }
        public bool ReplaceAll { get; set; }
    }

    public sealed class AvenDexModule
    {
        private sealed class TodoItem { public string Text; public char State; }
        private sealed class Checkpoint { public string FullPath; public bool Existed; public byte[] Data; }
        private sealed class SlashResult { public string Output, RunPrompt; }

        private readonly LocalAiModule _ai;
        private readonly string _root;
        private readonly List<ChatTurn> _turns = new List<ChatTurn>();
        private readonly List<TodoItem> _todos = new List<TodoItem>();
        private readonly List<Checkpoint> _checkpoints = new List<Checkpoint>();
        private readonly HashSet<string> _readFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        
        private CancellationTokenSource _cts;
        private volatile bool _running;
        private int _calls, _tokensIn, _tokensOut;

        public static event Action<string> ActivityLogged;
        public event Action<string> StepReport;
        public DexPermissionMode PermissionMode { get; set; } = DexPermissionMode.Default;
        public int MaxIterations { get; set; } = 30;

        private static readonly HttpClient Http = MakeHttp();
        private static HttpClient MakeHttp()
        {
            var c = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            c.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AVEIN-Dex/1.0");
            return c;
        }

        private static readonly string[] IgnoredDirs =
            { "bin", "obj", "node_modules", ".git", ".vs", ".idea", "packages", "dist", "__pycache__", ".avein", "venv", ".venv", "target" };
        private static readonly string[] BinaryExt =
            { ".dll", ".exe", ".pdb", ".png", ".jpg", ".jpeg", ".gif", ".ico", ".zip", ".gguf", ".bin", ".pdf", ".7z", ".rar", ".mp3", ".mp4", ".woff", ".woff2", ".ttf", ".nupkg", ".cache" };
        private static readonly string[] AgentStops = { "[RESULT", "[ERROR]" };

        private const string HelpText =
            "Aven Dex commands\n" +
            "  /help          this list\n" +
            "  /clear         reset conversation + todo list\n" +
            "  /todos         show the todo list\n" +
            "  /undo [n]      revert the last n file changes\n" +
            "  /mode <m>      default | accept | plan | auto\n" +
            "  /status        session info\n";

        public AvenDexModule(LocalAiModule ai, string projectRoot)
        {
            _ai = ai ?? new LocalAiModule();
            try { _root = string.IsNullOrWhiteSpace(projectRoot) ? Directory.GetCurrentDirectory() : Path.GetFullPath(projectRoot.Trim().Trim('"')); }
            catch { _root = projectRoot; }
        }

        public void Cancel() { try { _cts?.Cancel(); } catch { } }

        public async Task<string> ExecuteCodeTaskAsync(string userRequest)
        {
            if (string.IsNullOrWhiteSpace(userRequest)) return "(empty request)";
            if (!Directory.Exists(_root)) return "Project root not found: " + _root;
            if (_running) return "(Aven Dex is still working on the previous request - wait, or call Cancel)";

            _running = true;
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;
            try
            {
                var text = userRequest.Trim();
                if (text.StartsWith("#")) return AppendMemory(text.TrimStart('#').Trim());

                if (text.StartsWith("/"))
                {
                    var sr = await HandleSlashAsync(text, ct).ConfigureAwait(false);
                    if (sr.RunPrompt == null) return sr.Output;
                    text = sr.RunPrompt;
                }

                Report($"Project: {_root} | Mode: {PermissionMode}");
                _turns.Add(new ChatTurn("user", text));
                var result = await RunAgentAsync(_turns, MaxIterations, false, ct).ConfigureAwait(false);

                return result;
            }
            catch (OperationCanceledException) { return "(cancelled)"; }
            catch (Exception ex) { LogEx(ex); return "(agent error: " + ex.Message + ")"; }
            finally { _running = false; }
        }

        public string Undo(int steps = 1)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < steps && _checkpoints.Count > 0; i++)
            {
                var c = _checkpoints[_checkpoints.Count - 1];
                _checkpoints.RemoveAt(_checkpoints.Count - 1);
                try
                {
                    if (c.Existed)
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(c.FullPath));
                        File.WriteAllBytes(c.FullPath, c.Data);
                        sb.AppendLine("restored " + Rel(c.FullPath));
                    }
                    else if (File.Exists(c.FullPath)) { File.Delete(c.FullPath); sb.AppendLine("removed " + Rel(c.FullPath)); }
                }
                catch (Exception ex) { sb.AppendLine("could not undo " + Rel(c.FullPath) + ": " + ex.Message); }
            }
            return sb.Length == 0 ? "(nothing to undo)" : sb.ToString().TrimEnd();
        }

        private async Task<string> RunAgentAsync(List<ChatTurn> turns, int maxIter, bool sub, CancellationToken ct)
        {
            var repeats = new Dictionary<string, int>();
            string lastSaid = "";
            int nudges = 0;

            for (int step = 1; step <= maxIter; step++)
            {
                ct.ThrowIfCancellationRequested();
                Report(sub ? $"   > sub-step {step}/{maxIter}" : $"-- Step {step}/{maxIter} --");

                var system = BuildSystemPrompt(sub);
                await CompactIfNeededAsync(turns, system, ct).ConfigureAwait(false);
                AgeOldResults(turns);

                var msgs = new List<ChatTurn> { new ChatTurn("system", system) };
                msgs.AddRange(turns);
                
                var res = await _ai.ChatDetailedAsync(msgs, 1400, 0.15f, null, ct, null, 1.05f, AgentStops).ConfigureAwait(false);
                
                _calls++;
                _tokensIn += msgs.Sum(t => LocalAiModule.EstimateTokens(t.Content));
                _tokensOut += LocalAiModule.EstimateTokens(res.Text);
                if (res.Failed) { ct.ThrowIfCancellationRequested(); return res.Text; }

                var response = res.Text ?? "";
                var actions = Parse(response, out var errors, out var said);
                
                if (!string.IsNullOrWhiteSpace(said)) { Report("AI: " + Trunc(said, 700)); lastSaid = said; }
                turns.Add(new ChatTurn("assistant", string.IsNullOrWhiteSpace(response) ? "(no output)" : response));

                var todo = actions.Where(a => a.Type != DexActionType.Done).Take(4).ToList();
                if (todo.Count == 0 && errors.Count == 0)
                {
                    if (string.IsNullOrWhiteSpace(said) && nudges++ < 2)
                    {
                        turns.Add(new ChatTurn("user", "You returned nothing. Call a tool, or give your final answer without tags."));
                        continue;
                    }
                    return said;
                }

                var results = new StringBuilder();
                foreach (var err in errors) results.AppendLine("[ERROR] " + err);

                foreach (var a in todo)
                {
                    ct.ThrowIfCancellationRequested();
                    var sig = a.Type + "|" + a.Arg + "|" + (a.Content ?? "").GetHashCode() + "|" + (a.OldString ?? "").GetHashCode();
                    repeats.TryGetValue(sig, out var seen);
                    repeats[sig] = ++seen;
                    if (seen > 4) return "Stopped: the agent kept repeating the same action.\n" + lastSaid;

                    string output = seen > 2
                        ? "(you already did exactly this - change approach or finish)"
                        : await ExecuteActionAsync(a, sub, ct).ConfigureAwait(false);

                    results.AppendLine($"[RESULT {a.Type} {Regex.Replace(Trunc(a.Arg, 70) ?? "", @"\s+", " ")}]");
                    results.AppendLine(Trunc(output, a.Type == DexActionType.ReadFile ? 4600 : 3000));
                    results.AppendLine();
                }
                results.Append("Continue. If the task is complete, reply with a short summary and NO tags.");
                turns.Add(new ChatTurn("user", results.ToString()));
            }
            return $"(stopped after {maxIter} steps - reply 'continue' to keep going)\n{lastSaid}";
        }

        private string BuildSystemPrompt(bool sub)
        {
            var sb = new StringBuilder();
            sb.AppendLine("You are Aven Dex, a careful coding agent running locally inside AVEIN.");
            sb.AppendLine("Project root: " + _root);
            sb.AppendLine("Call tools by writing the tags below. After the tags STOP and wait - results arrive in the next message. Never invent results.");
            sb.AppendLine();
            sb.AppendLine("TOOLS");
            sb.AppendLine("[READ:path]");
            sb.AppendLine("[LS:dir]  [GLOB:**/*.cs]  [GREP:regex]");
            if (!sub)
            {
                sb.AppendLine("[EDIT:path]");
                sb.AppendLine("<<<<<<< SEARCH");
                sb.AppendLine("exact existing lines");
                sb.AppendLine("=======");
                sb.AppendLine("new lines");
                sb.AppendLine(">>>>>>> REPLACE");
                sb.AppendLine("[WRITE:path]");
                sb.AppendLine("entire new file");
                sb.AppendLine("[END_WRITE]");
                sb.AppendLine("[BASH:command]  [DELETE:path]  [MOVE:from>to]");
                sb.AppendLine("[TODO:[x] done | [~] doing | pending]");
                sb.AppendLine("[TASK:question for a read-only sub-agent]");
            }
            sb.AppendLine("[FETCH:url]  [SEARCH:query]");
            sb.AppendLine();
            sb.AppendLine("RULES");
            if (sub)
            {
                sb.AppendLine("You are a READ-ONLY research sub-agent. Investigate, then answer in under 150 words with file paths and line numbers, and NO tags.");
            }
            else
            {
                sb.AppendLine("1. READ a file before you EDIT it. Never guess paths or contents - use GLOB / GREP / LS.");
                sb.AppendLine("2. SEARCH text must match the file exactly (no line numbers). EDIT existing files; WRITE only new files.");
                sb.AppendLine("3. For work with 3+ steps start with [TODO:...] and keep it updated.");
                sb.AppendLine("4. Verify with BASH (build/test) when possible.");
                sb.AppendLine("5. Use 1-3 tools per turn. When the task is done, reply with a short summary and NO tags.");
                sb.AppendLine("MODE: " + PermissionMode);
                if (PermissionMode == DexPermissionMode.Plan)
                    sb.AppendLine("PLAN MODE: only read tools work. Investigate, then reply with a numbered plan and NO tags; the user will approve it.");
                if (_todos.Count > 0) sb.AppendLine("TODO LIST:\n" + TodoView());
            }
            sb.AppendLine("PROJECT FILES:\n" + BuildTree());
            var mem = ReadMemory();
            if (mem.Length > 0) sb.AppendLine("PROJECT MEMORY:\n" + mem);
            return sb.ToString();
        }

        private async Task CompactIfNeededAsync(List<ChatTurn> turns, string system, CancellationToken ct)
        {
            int limit = (int)(LocalAiModule.ContextTokens * 0.6);
            int used = LocalAiModule.EstimateTokens(system) + turns.Sum(t => LocalAiModule.EstimateTokens(t.Content));
            if (used > limit && turns.Count >= 4) await CompactAsync(turns, null, ct).ConfigureAwait(false);
        }

        private async Task<string> CompactAsync(List<ChatTurn> turns, string focus, CancellationToken ct)
        {
            if (turns.Count < 3) return "(nothing to compact)";
            int keep = 2;
            var old = turns.Take(turns.Count - keep).ToList();
            var transcript = new StringBuilder();
            foreach (var t in old) transcript.AppendLine((t.Role == "assistant" ? "AGENT: " : "USER/RESULT: ") + Trunc(t.Content, 700));
            var src = transcript.ToString();
            if (src.Length > 6000) src = src.Substring(src.Length - 6000);

            Report("[compacting context...]");
            var res = await _ai.ChatDetailedAsync(new List<ChatTurn>
            {
                new ChatTurn("system", "You compress coding-agent transcripts. Write a dense summary (max 180 words): the user's goal, what was done (files read/changed), key findings, remaining steps." +
                    (string.IsNullOrWhiteSpace(focus) ? "" : " Focus on: " + focus)),
                new ChatTurn("user", src)
            }, 350, 0.2f, null, ct, null, 1.05f).ConfigureAwait(false);

            var tail = turns.Skip(turns.Count - keep).ToList();
            turns.Clear();
            turns.Add(new ChatTurn("user", res.Failed ? "[Earlier conversation was dropped to free context.]" : "[Summary of the earlier conversation]\n" + res.Text));
            turns.AddRange(tail);
            return res.Failed ? "(compaction failed - old context dropped)" : "Compacted:\n" + res.Text;
        }

        private static void AgeOldResults(List<ChatTurn> turns)
        {
            for (int i = 0; i < turns.Count - 4; i++)
                if (turns[i].Role == "user" && turns[i].Content.Length > 500 &&
                    (turns[i].Content.StartsWith("[RESULT") || turns[i].Content.StartsWith("[ERROR]")))
                    turns[i].Content = turns[i].Content.Substring(0, 400) + "\n...(older result truncated)";
        }

        private static readonly Regex WriteRx = new Regex(@"\[(?:WRITE|WRITE_FILE):([^\]\r\n]+)\]([\s\S]*?)\[END_WRITE\]", RegexOptions.IgnoreCase);
        private static readonly Regex EditRx = new Regex(@"\[(EDIT|EDIT_ALL|EDIT_FILE):([^\]\r\n]+)\][ \t]*\r?\n?\s*<{3,}[ \t]*SEARCH[^\r\n]*\r?\n([\s\S]*?)\r?\n={3,}[ \t]*\r?\n([\s\S]*?)\r?\n?>{3,}[ \t]*REPLACE", RegexOptions.IgnoreCase);
        private static readonly Regex OpenerRx = new Regex(@"\[(WRITE|WRITE_FILE|EDIT|EDIT_ALL|EDIT_FILE):([^\]\r\n]+)\]", RegexOptions.IgnoreCase);
        private static readonly Regex TagOpenRx = new Regex(@"\[([A-Za-z_]+):");
        private static readonly Regex DoneRx = new Regex(@"\[DONE\]", RegexOptions.IgnoreCase);

        private static readonly Dictionary<string, DexActionType> TagMap = new Dictionary<string, DexActionType>(StringComparer.OrdinalIgnoreCase)
        {
            ["READ"] = DexActionType.ReadFile, ["READ_FILE"] = DexActionType.ReadFile,
            ["LS"] = DexActionType.ListDir, ["LIST_DIR"] = DexActionType.ListDir,
            ["GLOB"] = DexActionType.Glob, ["GREP"] = DexActionType.Grep,
            ["BASH"] = DexActionType.RunCmd, ["RUN_CMD"] = DexActionType.RunCmd,
            ["GIT"] = DexActionType.Git,
            ["DELETE"] = DexActionType.DeleteFile, ["DELETE_FILE"] = DexActionType.DeleteFile,
            ["MOVE"] = DexActionType.MoveFile, ["MOVE_FILE"] = DexActionType.MoveFile,
            ["FETCH"] = DexActionType.WebFetch, ["WEB_FETCH"] = DexActionType.WebFetch,
            ["SEARCH"] = DexActionType.WebSearch, ["WEB_SEARCH"] = DexActionType.WebSearch,
            ["TODO"] = DexActionType.Todo, ["TASK"] = DexActionType.SubTask
        };

        private static List<DexAction> Parse(string r, out List<string> errors, out string prose)
        {
            var actions = new List<DexAction>();
            errors = new List<string>();
            r = r ?? "";
            var work = r.ToCharArray();

            void Consume(int index, int length)
            {
                for (int i = index; i < index + length && i < work.Length; i++) work[i] = ' ';
            }

            foreach (Match m in WriteRx.Matches(r))
            {
                actions.Add(new DexAction { Type = DexActionType.WriteFile, Arg = m.Groups[1].Value.Trim(), Content = m.Groups[2].Value });
                Consume(m.Index, m.Length);
            }

            foreach (Match m in EditRx.Matches(r))
            {
                var isAll = m.Groups[1].Value.ToUpperInvariant() == "EDIT_ALL";
                actions.Add(new DexAction { Type = DexActionType.EditFile, Arg = m.Groups[2].Value.Trim(), OldString = m.Groups[3].Value, Content = m.Groups[4].Value, ReplaceAll = isAll });
                Consume(m.Index, m.Length);
            }

            foreach (Match m in OpenerRx.Matches(r))
            {
                if (work[m.Index] != ' ')
                    errors.Add("Tool tag was not closed: " + m.Value);
            }

            var leftover = new string(work);
            foreach (Match m in TagOpenRx.Matches(leftover))
            {
                var name = m.Groups[1].Value.ToUpperInvariant();
                if (!TagMap.ContainsKey(name)) continue;

                var close = leftover.IndexOf(']', m.Index);
                if (close < 0) { errors.Add("Tool tag missing ']': " + m.Value); continue; }

                var arg = leftover.Substring(m.Index + m.Length, close - (m.Index + m.Length)).Trim();
                actions.Add(new DexAction { Type = TagMap[name], Arg = arg });
                Consume(m.Index, close - m.Index + 1);
            }

            prose = new string(work).Trim();
            prose = Regex.Replace(prose, @"\s{2,}", " ").Trim();
            
            if (DoneRx.IsMatch(r)) actions.Add(new DexAction { Type = DexActionType.Done });

            return actions;
        }

        private async Task<string> ExecuteActionAsync(DexAction a, bool sub, CancellationToken ct)
        {
            try
            {
                if (sub && (a.Type == DexActionType.WriteFile || a.Type == DexActionType.EditFile || a.Type == DexActionType.DeleteFile || a.Type == DexActionType.MoveFile || a.Type == DexActionType.RunCmd))
                    return "[ERROR] Sub-agent tried to use a write tool. Sub-agents are read-only.";

                switch (a.Type)
                {
                    case DexActionType.ReadFile: return ReadFile(a.Arg);
                    case DexActionType.ListDir: return ListDir(a.Arg);
                    case DexActionType.Glob: return Glob(a.Arg);
                    case DexActionType.Grep: return Grep(a.Arg);
                    case DexActionType.Todo: return UpdateTodos(a.Arg);
                    case DexActionType.SubTask: return await RunAgentAsync(new List<ChatTurn> { new ChatTurn("user", a.Arg) }, 10, true, ct).ConfigureAwait(false);
                    case DexActionType.WebFetch: return await WebFetchAsync(a.Arg).ConfigureAwait(false);
                    case DexActionType.WebSearch: return await WebSearchAsync(a.Arg).ConfigureAwait(false);
                    case DexActionType.WriteFile: return WriteFile(a.Arg, a.Content);
                    case DexActionType.EditFile: return EditFile(a.Arg, a.OldString, a.Content, a.ReplaceAll
