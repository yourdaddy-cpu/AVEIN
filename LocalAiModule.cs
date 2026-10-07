using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using LLama;
using LLama.Common;
using LLama.Native;

namespace AVEIN
{
    public sealed class LocalAiModule
    {
        private LLamaWeights _model;
        private LLamaContext _context;
        private InteractiveExecutor _executor;
        private string _loadedModelName;

        private const string OwnerName = "Onyx";
        private static bool _backendConfigured = false;

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

            // Force CUDA backend and skip the compatibility check that triggers the bug
            if (!_backendConfigured)
            {
                try
                {
                    NativeLibraryConfig.All.WithCuda(true);
                    NativeLibraryConfig.All.SkipCheck(true);
                }
                catch { }
                _backendConfigured = true;
            }

            if (_loadedModelName == modelName && _executor != null) return true;

            var path = ModelFilePath(modelName);
            if (path == null)
            {
                var tried = ModelFolder(modelName);
                error = $"(model not found)\n  looked in: {tried}\n  put your .gguf file in that folder and restart.";
                return false;
            }

            try
            {
                _context?.Dispose();
                _model?.Dispose();
                _executor = null;

                var parameters = new ModelParams(path)
                {
                    ContextSize = 4096,
                    Threads = 6,
                    GpuLayerCount = 20
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
                if (ex.InnerException != null)
                    error += "\n  → " + ex.InnerException.GetType().Name + ": " + ex.InnerException.Message;
                return false;
            }
        }

        private async Task<string> GenerateRawAsync(string modelName, string prompt, int maxTokens, float temperature)
        {
            if (!EnsureLoaded(modelName, out var error)) return error;

            var inferenceParams = new InferenceParams
            {
                MaxTokens = maxTokens,
                Temperature = temperature,
                RepeatPenalty = 1.5f,
                AntiPrompts = new System.Collections.Generic.List<string> { "<|im_end|>", "<|im_start|>" }
            };

            var result = "";
            await foreach (var text in _executor.InferAsync(prompt, inferenceParams))
                result += text;

            return CleanUp(result);
        }

        public async Task<string> AskRawAsync(string systemPrompt, string userMessage, int maxTokens = 900, float temperature = 0.2f)
        {
            var prompt = $"<|im_start|>system<|im_sep|>{systemPrompt}<|im_end|><|im_start|>user<|im_sep|>{userMessage}<|im_end|><|im_start|>assistant<|im_sep|>";
            return await GenerateRawAsync("Phi4-mini", prompt, maxTokens, temperature);
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

            if ((lower.Contains("minecraft") || lower.Contains(" mod ") || lower.Contains("compiled program")) &&
                (lower.Contains("create") || lower.Contains("make")))
            {
                return "I can create plain text-based files (.txt, .md, .json, .csv, .log, .html, .css, .xml, .js, .py, .cs) and .zip archives of them.";
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

                var ext = Path.GetExtension(fileName).ToLowerInvariant();

                var contentPrompt = $"<|im_start|>system<|im_sep|>You are a file content generator. The user wants to create a file named '{fileName}' ({ext}). Write ONLY the raw content that should go inside this file. Output ONLY the raw file content with no explanations, no greetings, no markdown fences. For HTML files: write complete valid HTML starting with <!DOCTYPE html>. Use actual line breaks.<|im_end|><|im_start|>user<|im_sep|>{userMessage}<|im_end|><|im_start|>assistant<|im_sep|>";

                var generatedContent = await GenerateRawAsync(modelName, contentPrompt, 800, 0.3f);
                if (string.IsNullOrWhiteSpace(generatedContent))
                    generatedContent = "(The AI could not generate content for this file.)";

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
            var answer = await GenerateRawAsync(modelName, prompt, deepThink ? 500 : 300, 0.3f);

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
