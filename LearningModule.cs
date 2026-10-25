using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace AVEIN
{
    public sealed class Skill
    {
        [JsonProperty("name")]  public string Name { get; set; }
        [JsonProperty("when")]  public string When { get; set; }
        [JsonProperty("steps")] public List<string> Steps { get; set; } = new List<string>();
        [JsonProperty("uses")]  public int Uses { get; set; }
        [JsonProperty("fixes")] public int Fixes { get; set; }
    }

    /// <summary>
    /// Hermes-style learning loop for a small local model:
    /// 1) memory.json  - durable facts about the user/project
    /// 2) past-chat recall - keyword search over HistoryStore sessions
    /// 3) skills.json  - reusable how-tos the model writes after a task, refined when you correct it
    /// </summary>
    public static class LearningModule
    {
        // LocalAiModule shares one executor, so user turns and learning turns must not overlap.
        public static readonly SemaphoreSlim Gate = new SemaphoreSlim(1, 1);

        private const int MaxFacts = 60;
        private const int MaxSkills = 40;
        private static Skill _lastUsedSkill;

        private static string DataDir => Path.Combine(AppContext.BaseDirectory, "data");
        private static string FactsPath => Path.Combine(DataDir, "memory.json");
        private static string SkillsPath => Path.Combine(DataDir, "skills.json");

        private static readonly HashSet<string> Stop = new HashSet<string>
        {
            "the","and","that","with","this","from","have","what","please","can","you","for",
            "are","was","not","but","how","your","about","into","then","just","like","want","need"
        };

        // ---------- storage ----------
        private static T Load<T>(string path) where T : new()
        {
            try { if (File.Exists(path)) return JsonConvert.DeserializeObject<T>(File.ReadAllText(path)) ?? new T(); }
            catch { }
            return new T();
        }

        private static void Save(string path, object data)
        {
            Directory.CreateDirectory(DataDir);
            File.WriteAllText(path, JsonConvert.SerializeObject(data, Formatting.Indented));
        }

        // ---------- keyword scoring ----------
        private static HashSet<string> Tokens(string text)
        {
            var set = new HashSet<string>();
            foreach (Match m in Regex.Matches((text ?? "").ToLowerInvariant(), @"[a-z0-9_\.]{3,}"))
                if (!Stop.Contains(m.Value)) set.Add(m.Value);
            return set;
        }

        private static int Score(string text, HashSet<string> query) => Tokens(text).Count(query.Contains);

        // ---------- 1. build context to inject into the system prompt ----------
        public static string BuildContext(string userMessage, int maxChars = 1400)
        {
            var q = Tokens(userMessage);
            var sb = new StringBuilder();

            // facts: most relevant first, else newest
            var facts = Load<List<string>>(FactsPath);
            var pickedFacts = facts.OrderByDescending(f => Score(f, q)).ThenByDescending(f => facts.IndexOf(f)).Take(5).ToList();
            if (pickedFacts.Count > 0)
                sb.Append(" Known about the user: " + string.Join("; ", pickedFacts) + ".");

            // best matching skill
            _lastUsedSkill = null;
            var skills = Load<List<Skill>>(SkillsPath);
            var best = skills.Select(s => new { s, sc = Score(s.Name + " " + s.When, q) })
                             .OrderByDescending(x => x.sc).FirstOrDefault();
            if (best != null && best.sc >= 2)
            {
                _lastUsedSkill = best.s;
                sb.Append($" Relevant skill '{best.s.Name}': " + string.Join(" -> ", best.s.Steps) + ".");
            }

            // past conversations
            var recalled = 0;
            foreach (var session in HistoryStore.LoadAllSessions().Take(50))
            {
                for (int i = 0; i < session.Messages.Count - 1 && recalled < 2; i++)
                {
                    var m = session.Messages[i];
                    if (m.Sender != "You" || m.Text == userMessage) continue;
                    if (Score(m.Text, q) >= 3)
                    {
                        var reply = session.Messages[i + 1].Text ?? "";
                        sb.Append($" Earlier you asked '{Trim(m.Text, 80)}' and the answer was '{Trim(reply, 120)}'.");
                        recalled++;
                    }
                }
                if (recalled >= 2) break;
            }

            var result = sb.ToString();
            return result.Length > maxChars ? result.Substring(0, maxChars) : result;
        }

        private static string Trim(string s, int n) =>
            string.IsNullOrEmpty(s) ? "" : (s.Length > n ? s.Substring(0, n) + "..." : s).Replace("\n", " ");

        // ---------- 2. learn after each turn (call fire-and-forget) ----------
        public static async Task LearnAsync(LocalAiModule ai, string userMessage, string answer)
        {
            await Gate.WaitAsync();
            try
            {
                if (string.IsNullOrWhiteSpace(userMessage) || string.IsNullOrWhiteSpace(answer)) return;
                bool failed = answer.StartsWith("(") || answer.StartsWith("Internet is off");

                // user correcting the last skill -> refine it
                if (_lastUsedSkill != null && Regex.IsMatch(userMessage.Trim(), @"^(no\b|wrong|that'?s wrong|incorrect|not what i|that is not)", RegexOptions.IgnoreCase))
                {
                    await RefineSkillAsync(ai, _lastUsedSkill, userMessage);
                    return;
                }
                if (failed) return;

                await ExtractFactsAsync(ai, userMessage);

                bool taskLike = Regex.IsMatch(userMessage, @"\b(create|make|write|build|generate|zip|convert|fix|set ?up)\b", RegexOptions.IgnoreCase);
                if (taskLike) await CreateSkillAsync(ai, userMessage, answer);
            }
            catch { /* learning must never crash the app */ }
            finally { Gate.Release(); }
        }

        private static async Task ExtractFactsAsync(LocalAiModule ai, string userMessage)
        {
            var raw = await ai.AskRawAsync(
                "Extract durable facts about the user (name, preferences, projects, tools, habits) from their message. " +
                "Output ONLY a JSON array of short strings. If there are none, output [].",
                userMessage, 150, 0.1f);

            var found = ParseJson<List<string>>(raw, '[', ']');
            if (found == null || found.Count == 0) return;

            var facts = Load<List<string>>(FactsPath);
            foreach (var f in found.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => Trim(x.Trim(), 120)))
                if (!facts.Any(e => e.Equals(f, StringComparison.OrdinalIgnoreCase))) facts.Add(f);

            if (facts.Count > MaxFacts) facts = facts.Skip(facts.Count - MaxFacts).ToList();
            Save(FactsPath, facts);
        }

        private static async Task CreateSkillAsync(LocalAiModule ai, string userMessage, string answer)
        {
            var raw = await ai.AskRawAsync(
                "A task was just completed successfully. Write a short reusable skill for doing it again. " +
                "Output ONLY JSON like {\"name\":\"short title\",\"when\":\"when to use it\",\"steps\":[\"step 1\",\"step 2\"]} " +
                "with at most 5 steps.",
                $"Task: {userMessage}\nResult: {Trim(answer, 300)}", 250, 0.1f);

            var skill = ParseJson<Skill>(raw, '{', '}');
            if (skill == null || string.IsNullOrWhiteSpace(skill.Name) || skill.Steps == null || skill.Steps.Count == 0) return;

            var skills = Load<List<Skill>>(SkillsPath);
            var existing = skills.FirstOrDefault(s => s.Name.Equals(skill.Name, StringComparison.OrdinalIgnoreCase));
            if (existing != null) { existing.Uses++; Save(SkillsPath, skills); return; }

            skill.Uses = 1;
            skills.Add(skill);
            if (skills.Count > MaxSkills) skills = skills.OrderByDescending(s => s.Uses + s.Fixes).Take(MaxSkills).ToList();
            Save(SkillsPath, skills);
        }

        private static async Task RefineSkillAsync(LocalAiModule ai, Skill skill, string correction)
        {
            var raw = await ai.AskRawAsync(
                "Improve this skill using the user's correction. Output ONLY JSON like " +
                "{\"name\":\"...\",\"when\":\"...\",\"steps\":[\"...\"]} with at most 5 steps.",
                $"Skill: {JsonConvert.SerializeObject(skill)}\nCorrection: {correction}", 250, 0.1f);

            var improved = ParseJson<Skill>(raw, '{', '}');
            if (improved == null || improved.Steps == null || improved.Steps.Count == 0) return;

            var skills = Load<List<Skill>>(SkillsPath);
            var target = skills.FirstOrDefault(s => s.Name.Equals(skill.Name, StringComparison.OrdinalIgnoreCase));
            if (target == null) return;
            target.Steps = improved.Steps;
            if (!string.IsNullOrWhiteSpace(improved.When)) target.When = improved.When;
            target.Fixes++;
            Save(SkillsPath, skills);
        }

        private static T ParseJson<T>(string raw, char open, char close) where T : class
        {
            try
            {
                int a = raw.IndexOf(open), b = raw.LastIndexOf(close);
                if (a < 0 || b <= a) return null;
                return JsonConvert.DeserializeObject<T>(raw.Substring(a, b - a + 1));
            }
            catch { return null; }
        }
    }
}
