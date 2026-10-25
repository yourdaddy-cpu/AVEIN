using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using LLama;
using LLama.Common;
using LLama.Native;
using LLama.Sampling;
using Newtonsoft.Json;

namespace AVEIN
{
    public sealed class ChatTurn
    {
        public string Role { get; set; }      // system | user | assistant
        public string Content { get; set; }
        public ChatTurn() { }
        public ChatTurn(string role, string content) { Role = role; Content = content; }
    }

    public sealed class AiResult
    {
        public string Text { get; set; }
        public bool Failed { get; set; }
    }

    internal sealed class AiSettings
    {
        [JsonProperty("contextSize")] public int ContextSize { get; set; } = 8192;
        [JsonProperty("gpuLayers")] public int GpuLayers { get; set; } = 20;

        private static string FilePath => Path.Combine(AppContext.BaseDirectory, "config", "ai.json");

        public static AiSettings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var s = JsonConvert.DeserializeObject<AiSettings>(File.ReadAllText(FilePath));
                    if (s != null)
                    {
                        s.ContextSize = Math.Max(2048, Math.Min(s.ContextSize, 131072));
                        return s;
                    }
                }
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                    File.WriteAllText(FilePath, JsonConvert.SerializeObject(new AiSettings(), Formatting.Indented));
                }
            }
            catch { }
            return new AiSettings();
        }
    }

    public sealed class LocalAiModule
    {
        public const string DefaultModel = "Phi4-mini";
        private const string OwnerName = "Onyx";

        private enum PromptStyle { Phi3, Phi4, ChatML, Llama3, Gemma }

        // Everything below is shared by ALL instances: one model, one lock.
        private static readonly SemaphoreSlim Gate = new SemaphoreSlim(1, 1);
        private static readonly object ConfigLock = new object();
        private static readonly List<ChatTurn> Chat = new List<ChatTurn>();
        private static readonly string[] BaseStops =
        {
            "<|end|>", "<|user|>", "<|assistant|>", "<|system|>", "<|im_end|>", "<|im_start|>",
            "<|eot_id|>", "<end_of_turn>", "<|endoftext|>"
        };

        private static LLamaWeights _model;
        private static StatelessExecutor _executor;
        private static string _loadedModelName;
        private static PromptStyle _style = PromptStyle.Phi3;
        private static int _ctxSize;
        private static bool _backendConfigured;
        private static string _backendFailure;

        public static bool LearningEnabled { get; set; } = true;
        public static string LastError { get; private set; }
        public static string LoadedModel => _loadedModelName;
        public static int ContextTokens => _ctxSize > 0 ? _ctxSize : AiSettings.Load().ContextSize;
        public static int EstimateTokens(string s) => string.IsNullOrEmpty(s) ? 0 : (int)Math.Ceiling(s.Length / 3.0);
        public static void ResetChat() { lock (Chat) Chat.Clear(); }

        // ───────────────────────── model loading ─────────────────────────
        private static string ModelFolder(string modelName) => Path.Combine(AppContext.BaseDirectory, "models", modelName);

        private static string ModelFilePath(string modelName)
        {
            var folder = ModelFolder(modelName);
            if (!Directory.Exists(folder)) return null;
            return Directory.GetFiles(folder, "*.gguf")
                .Where(f => f.IndexOf("mmproj", StringComparison.OrdinalIgnoreCase) < 0)
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }

        private static void ConfigureBackendOnce()
        {
            lock (ConfigLock)
            {
                if (_backendConfigured) return;
                _backendConfigured = true;
                try { NativeLibraryConfig.All.WithCuda(true).WithAutoFallback(true); }
                catch { }
            }
        }

        private static bool IsBackendFailure(Exception ex)
        {
            for (var e = ex; e != null; e = e.InnerException)
            {
                if (e is TypeInitializationException || e is DllNotFoundException ||
                    e is BadImageFormatException || e is EntryPointNotFoundException) return true;
                var m = e.Message ?? "";
                if (m.IndexOf("native library", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    m.IndexOf("Unable to load DLL", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }

        private static string Root(Exception e)
        {
            if (e == null) return "unknown error";
            while (e.InnerException != null) e = e.InnerException;
            return e.GetType().Name + ": " + e.Message;
        }

        private static string BackendHelp(Exception ex) =>
            "(LLamaSharp native backend failed to load)\n  " + Root(ex) + "\n\n" +
            "Fix:\n 1. AVEIN.csproj needs BOTH LLamaSharp.Backend.Cpu and LLamaSharp.Backend.Cuda12 (same version as LLamaSharp).\n" +
            " 2. Do NOT publish as single-file. The 'runtimes' folder must sit next to AVEIN.exe.\n" +
            " 3. For GPU install the NVIDIA driver + CUDA Toolkit 12.x. Without it the app falls back to CPU.\n" +
            " 4. Install the latest Microsoft Visual C++ Redistributable (x64).\n" +
            "Details: data\\ai-error.log";

        private static void LogError(string what, Exception ex)
        {
            try
            {
                var dir = Path.Combine(AppContext.BaseDirectory, "data");
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "ai-error.log"), $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {what}\n{ex}\n\n");
            }
            catch { }
        }

        private static PromptStyle DetectStyle(LLamaWeights w, string modelName)
        {
            string tpl = null;
            try { w.Metadata.TryGetValue("tokenizer.chat_template", out tpl); } catch { }
            tpl = tpl ?? "";
            if (tpl.Contains("<|im_sep|>")) return PromptStyle.Phi4;
            if (tpl.Contains("<|start_header_id|>")) return PromptStyle.Llama3;
            if (tpl.Contains("<start_of_turn>")) return PromptStyle.Gemma;
            if (tpl.Contains("<|im_start|>")) return PromptStyle.ChatML;
            if (tpl.Contains("<|end|>") && tpl.Contains("<|assistant|>")) return PromptStyle.Phi3;
            return modelName.IndexOf("phi", StringComparison.OrdinalIgnoreCase) >= 0 ? PromptStyle.Phi3 : PromptStyle.ChatML;
        }

        private static void DisposeModel()
        {
            try { _model?.Dispose(); } catch { }
            _model = null; _executor = null; _loadedModelName = null; _ctxSize = 0;
        }

        // Must be called while holding Gate.
        private static bool EnsureLoaded(string modelName, out string error)
        {
            error = null;
            if (_backendFailure != null) { error = _backendFailure; return false; }
            if (_model != null && _executor != null &&
                string.Equals(_loadedModelName, modelName, StringComparison.OrdinalIgnoreCase)) return true;

            var path = ModelFilePath(modelName);
            if (path == null)
            {
                error = $"(model not found)\n looked in: {ModelFolder(modelName)}\n put your .gguf file in that folder and restart.";
                return false;
            }

            ConfigureBackendOnce();
            DisposeModel();

            var cfg = AiSettings.Load();
            var attempts = new List<(int ctx, int gpu)> { (cfg.ContextSize, cfg.GpuLayers) };
            if (cfg.GpuLayers != 0) attempts.Add((cfg.ContextSize, 0));
            if (cfg.ContextSize > 4096) attempts.Add((4096, 0));

            Exception last = null;
            foreach (var a in attempts)
            {
                LLamaWeights weights = null;
                try
                {
                    var p = new ModelParams(path) { ContextSize = (uint)a.ctx, GpuLayerCount = a.gpu };
                    weights = LLamaWeights.LoadFromFile(p);
                    var exec = new StatelessExecutor(weights, p);   // also proves a context can be allocated
                    var style = DetectStyle(weights, modelName);
                    _model = weights; _executor = exec; _style = style;
                    _ctxSize = a.ctx; _loadedModelName = modelName;
                    return true;
                }
                catch (Exception ex)
                {
                    last = ex;
                    LogError($"load (ctx {a.ctx}, gpu layers {a.gpu})", ex);
                    try { weights?.Dispose(); } catch { }
                    if (IsBackendFailure(ex)) { _backendFailure = BackendHelp(ex); error = _backendFailure; return false; }
                }
            }
            error = "(model load failed)\n  " + Root(last) +
                    "\n  Tip: lower contextSize / gpuLayers in config\\ai.json or use a smaller .gguf.\n  Details: data\\ai-error.log";
            return false;
        }

        public static void Shutdown()
        {
            try { Gate.Wait(3000); } catch { }
            DisposeModel();
        }

        // ───────────────────────── prompt building ─────────────────────────
        private static string Neutralize(string s) =>
            (s ?? "").Replace("<|", "<\u200B|").Replace("<start_of_turn>", "<\u200Bstart_of_turn>").Replace("<end_of_turn>", "<\u200Bend_of_turn>");

        private static string Rl(ChatTurn t) => t.Role == "assistant" || t.Role == "system" ? t.Role : "user";

        private static string BuildPrompt(IList<ChatTurn> turns)
        {
            var sb = new StringBuilder();
            switch (_style)
            {
                case PromptStyle.Phi4:
                    foreach (var t in turns) sb.Append("<|im_start|>").Append(Rl(t)).Append("<|im_sep|>").Append(t.Content).Append("<|im_end|>");
                    sb.Append("<|im_start|>assistant<|im_sep|>");
                    break;
                case PromptStyle.ChatML:
                    foreach (var t in turns) sb.Append("<|im_start|>").Append(Rl(t)).Append('\n').Append(t.Content).Append("<|im_end|>\n");
                    sb.Append("<|im_start|>assistant\n");
                    break;
                case PromptStyle.Llama3:
                    foreach (var t in turns) sb.Append("<|start_header_id|>").Append(Rl(t)).Append("<|end_header_id|>\n\n").Append(t.Content).Append("<|eot_id|>");
                    sb.Append("<|start_header_id|>assistant<|end_header_id|>\n\n");
                    break;
                case PromptStyle.Gemma:
                    string sys = null;
                    foreach (var t in turns)
                    {
                        if (t.Role == "system") { sys = sys == null ? t.Content : sys + "\n\n" + t.Content; continue; }
                        var c = t.Content;
                        if (Rl(t) == "user" && sys != null) { c = sys + "\n\n" + c; sys = null; }
                        sb.Append("<start_of_turn>").Append(Rl(t) == "assistant" ? "model" : "user").Append('\n').Append(c).Append("<end_of_turn>\n");
                    }
                    sb.Append("<start_of_turn>model\n");
                    break;
                default: // Phi-4-mini / Phi-3
                    foreach (var t in turns) sb.Append("<|").Append(Rl(t)).Append("|>").Append(t.Content).Append("<|end|>");
                    sb.Append("<|assistant|>");
                    break;
            }
            return sb.ToString();
        }

        // Drops/shrinks the oldest content until the prompt fits the context window.
        private static List<ChatTurn> Fit(IList<ChatTurn> input, int budget)
        {
            var turns = input.Select(t => new ChatTurn(t.Role, Neutralize(t.Content))).ToList();
            for (int guard = 0; guard < 300 && EstimateTokens(BuildPrompt(turns)) > budget; guard++)
            {
                if (turns.Count(t => t.Role != "system") > 1)
                {
                    turns.RemoveAt(turns.FindIndex(t => t.Role != "system"));
                    continue;
                }
                var big = turns.OrderByDescending(t => t.Content.Length).First();
                if (big.Content.Length < 300) break;
                int keep = (int)(big.Content.Length * 0.75);
                big.Content = big.Role == "system"
                    ? big.Content.Substring(0, keep) + "\n[...trimmed...]"
                    : "[...trimmed...]\n" + big.Content.Substring(big.Content.Length - ke
