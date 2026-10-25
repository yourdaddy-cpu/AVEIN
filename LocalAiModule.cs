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
using LLama.Sampling;

namespace AVEIN
{
    public class ChatTurn
    {
        public string Role { get; set; }
        public string Content { get; set; }
        public ChatTurn(string role, string content) { Role = role; Content = content; }
    }

    public class AiResult
    {
        public string Text { get; set; }
        public bool Failed { get; set; }
    }

    public sealed class LocalAiModule
    {
        private LLamaWeights _model;
        private LLamaContext _context;
        private InteractiveExecutor _executor;
        private string _loadedModelName;

        private const string OwnerName = "Onyx";

        public static string DefaultModel = "Phi4-mini";
        public static string LoadedModel => "Phi4-mini";
        public static int ContextTokens = 4096;

        public static int EstimateTokens(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            return text.Length / 4;
        }

        private static string ModelFolder(string modelName) =>
            Path.Combine(AppContext.BaseDirectory, "models", modelName);

        private static string ModelFilePath(string modelName)
        {
            var folder = ModelFolder(modelName);
            if (!Directory.Exists(folder)) return null;
            var files = Directory.GetFiles(folder, "*.gguf");
            return files.Length > 0 ? files[0] : null;
        }

        private bool EnsureLoaded(string modelName, out string error)
        {
            error = null;
            if (_loadedModelName == modelName && _executor != null) return true;

            var path = ModelFilePath(modelName);
            if (path == null)
            {
                error = $"({modelName} model file not found in models/{modelName}/)";
                return false;
            }

            try
            {
                _context?.Dispose();
                _model?.Dispose();
                _executor = null;

                var parameters = new ModelParams(path)
                {
                    ContextSize = ContextTokens,
                    Threads = 6,
                    GpuLayerCount = 0 // Set to 20 if you want GPU
                };

                _model = LLamaWeights.LoadFromFile(parameters);
                _context = _model.CreateContext(parameters);
                _executor = new InteractiveExecutor(_context);
                _loadedModelName = modelName;
                return true;
            }
            catch (Exception ex)
            {
                error = "(model load failed)\n  " + ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        public async Task<AiResult> ChatDetailedAsync(List<ChatTurn> turns, int maxTokens, float temp, object p1, CancellationToken ct, object p2, float repeatPenalty, string[] stops)
        {
            if (!EnsureLoaded(DefaultModel, out var error))
                return new AiResult { Text = error, Failed = true };

            var prompt = new StringBuilder();
            foreach (var turn in turns)
            {
                prompt.Append($"<|im_start|>{turn.Role}<|im_sep|>{turn.Content}<|im_end|>");
            }
            prompt.Append("<|im_start|>assistant<|im_sep|>");

            var inferenceParams = new InferenceParams
            {
                MaxTokens = maxTokens,
                AntiPrompts = new List<string>(stops) { "<|im_end|>", "<|im_start|>" },
                SamplingPipeline = new DefaultSamplingPipeline
                {
                    Temperature = temp,
                    RepeatPenalty = repeatPenalty
                }
            };

            var result = new StringBuilder();
            try
            {
                await foreach (var text in _executor.InferAsync(prompt.ToString(), inferenceParams, ct))
                {
                    result.Append(text);
                }
                return new AiResult { Text = CleanUp(result.ToString()), Failed = false };
            }
            catch (OperationCanceledException)
            {
                return new AiResult { Text = "(cancelled)", Failed = true };
            }
            catch (Exception ex)
            {
                return new AiResult { Text = "(error: " + ex.Message + ")", Failed = true };
            }
        }

        public async Task<string> AskRawAsync(string systemPrompt, string userMessage, int maxTokens = 900, float temperature = 0.2f)
        {
            var turns = new List<ChatTurn>
            {
                new ChatTurn("system", systemPrompt),
                new ChatTurn("user", userMessage)
            };
            var res = await ChatDetailedAsync(turns, maxTokens, temperature, null, CancellationToken.None, null, 1.05f, new string[0]);
            return res.Text;
        }

        public async Task<string> AskAsync(string userMessage, string modelName = "Phi4-mini", bool deepThink = false)
        {
            if (SafetyModule.IsBlocked(userMessage))
                return SafetyModule.RefusalMessage();

            var lower = userMessage.ToLowerInvariant().Trim();

            if (lower.Contains("what") && (lower.Contains("open") || lower.Contains("running")) && (lower.Contains("window") || lower.Contains("app") || lower.Contains("screen")))
                return "Here's what's currently open:\n" + FileToolModule.GetOpenWindowsList();

            if (lower.Contains("zip") || lower.Contains("compress") || lower.Contains("archive"))
            {
                if (lower.Contains("create") || lower.Contains("make") || lower.Contains("build"))
                {
                    var zipMatch = Regex.Match(userMessage, @"[\w\-]+\.zip", RegexOptions.IgnoreCase);
                    var zipName = zipMatch.Success ? zipMatch.Value : "archive.zip";
                    return FileToolModule.CreateZip(zipName);
                }
            }

            if ((lower.Contains("create") || lower.Contains("make") || lower.Contains("write")) && lower.Contains("file"))
            {
                var extMatch = Regex.Match(userMessage, @"[a-zA-Z0-9_\-]+\.[a-zA-Z]{1,5}");

                string fileName;
                if (extMatch.Success) fileName = extMatch.Value;
                else if (lower.Contains("html")) fileName = "note.html";
                else if (lower.Contains("json")) fileName = "note.json";
                else if (lower.Contains("css")) fileName = "note.css";
                else if (lower.Contains("javascript") || lower.Contains("js file")) fileName = "note.js";
                else if (lower.Contains("python") || lower.Contains("py file")) fileName = "note.py";
                else if (lower.Contains("markdown") || lower.Contains("md file")) fileName = "note.md";
                else fileName = "note.txt";

                var contentPrompt = $"<|im_start|>system<|im_sep|>You are a file content generator. The user wants to create a file named '{fileName}'. Write ONLY the raw content that should go inside this file. Output ONLY the raw file content with no explanations, no greetings, no markdown fences. Use actual line breaks.<|im_end|><|im_start|>user<|im_sep|>{userMessage}<|im_end|><|im_start|>assistant<|im_sep|>";

                var generatedContent = await AskRawAsync("You are a helpful assistant.", userMessage, 800, 0.3f);
                if (string.IsNullOrWhiteSpace(generatedContent)) generatedContent = "(The AI could not generate content for this file.)";

                return FileToolModule.CreateFile(fileName, generatedContent);
            }

            if (lower.Contains("what files") || lower.Contains("list files") || lower.Contains("list my files") || lower.Contains("show files"))
                return "Files in AVEIN-Files:\n" + FileToolModule.ListSandboxFiles();

            if (lower.Contains("who made you") || lower.Contains("who created you") || lower.Contains("who built you") || lower.Contains("who is your creator"))
                return $"I was created by {OwnerName}, as part of the AVEIN project.";

            var systemPrompt = $"You are {modelName}, a helpful assistant created by {OwnerName} as part of the AVEIN project. " +
                "STRICT RULES:\n" +
                "1. Always reply in English, be concise and factual.\n" +
                "2. Never make up facts. If you are not sure, say 'I am not sure'.\n" +
                "3. If search results are provided, use ONLY that information.\n" +
                "4. Never claim you searched the web unless search results are literally shown to you.\n" +
                "5. Never repeat words or phrases.\n" +
                $"6. If asked who made you, say {OwnerName}.";

            if (deepThink)
                systemPrompt += " Think step by step. Put reasoning inside <thinking></thinking> tags, then final answer inside <answer></answer> tags.";

            var searchUsed = false;
            if (App.Config.IsModuleEnabled("web-search"))
            {
                var hasInternet = await WebSearchModule.IsInternetAvailableAsync();
                if (!hasInternet)
                    return "Internet is off, so I can't search right now.";

                var searchResult = await WebSearchModule.SearchAsync(userMessage);
                if (!string.IsNullOrEmpty(searchResult))
                {
                    systemPrompt += " Real search result: " + searchResult;
                    searchUsed = true;
                }
                else systemPrompt += " No search result was found. Say so honestly.";
            }

            var prompt = $"<|im_start|>system<|im_sep|>{systemPrompt}<|im_end|><|im_start|>user<|im_sep|>{userMessage}<|im_end|><|im_start|>assistant<|im_sep|>";
            var answer = await AskRawAsync(systemPrompt, userMessage, deepThink ? 500 : 300, 0.3f);

            if (App.Config.IsModuleEnabled("web-search"))
                answer += searchUsed ? "\n\n[used live web search]" : "\n\n[no web search result used]";

            return answer;
        }

        private static string CleanUp(string text)
        {
            text = text.Replace("Ċ", "\n");
            text = text.Replace("<|im_end|>", "");
            text = text.Replace("<|im_start|>", "");
            text = text.Replace("<|im_sep|>", "");
            text = text.Trim();

            if (text.StartsWith("assistant", StringComparison.OrdinalIgnoreCase))
                text = text.Substring("assistant".Length).TrimStart();

            return text;
        }
    }
}
