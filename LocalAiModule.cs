using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using LLama;
using LLama.Common;
using LLama.Sampling;

namespace AVEIN
{
    public sealed class LocalAiModule
    {
        private LLamaWeights _model;
        private LLamaContext _context;
        private InteractiveExecutor _executor;
        private string _loadedModelName;

        private const string OwnerName = "Onyx";

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
                error = $"({modelName} model file not found in models/{modelName}/ - make sure it's copied there)";
                return false;
            }

            _context?.Dispose();
            _model?.Dispose();
            _executor = null;

            var parameters = new ModelParams(path)
            {
                ContextSize = 2048,
                Threads = 6,
                GpuLayerCount = 20
            };

            _model = LLamaWeights.LoadFromFile(parameters);
            _context = _model.CreateContext(parameters);
            _executor = new InteractiveExecutor(_context);
            _loadedModelName = modelName;
            return true;
        }

        private async Task<string> GenerateRawAsync(string modelName, string prompt, int maxTokens)
        {
            if (!EnsureLoaded(modelName, out var error)) return error;

            var inferenceParams = new InferenceParams
            {
                MaxTokens = maxTokens,
                AntiPrompts = new System.Collections.Generic.List<string> { "<|im_end|>", "<|im_start|>" },
                SamplingPipeline = new DefaultSamplingPipeline
                {
                    Temperature = 0.3f,
                    RepeatPenalty = 1.5f
                }
            };

            var result = "";
            await foreach (var text in _executor.InferAsync(prompt, inferenceParams))
            {
                result += text;
            }
            return CleanUp(result);
        }

        public async Task<string> AskAsync(string userMessage, string modelName = "Phi4-mini", bool deepThink = false)
        {
            if (SafetyModule.IsBlocked(userMessage))
                return SafetyModule.RefusalMessage();

            var lower = userMessage.ToLowerInvariant().Trim();

            // Open windows query
            if (lower.Contains("what") && (lower.Contains("open") || lower.Contains("running")) && (lower.Contains("window") || lower.Contains("app") || lower.Contains("screen")))
            {
                return "Here's what's currently open:\n" + FileToolModule.GetOpenWindowsList();
            }

            // ZIP creation
            if (lower.Contains("zip") || lower.Contains("compress") || lower.Contains("archive"))
            {
                if (lower.Contains("create") || lower.Contains("make") || lower.Contains("build"))
                {
                    var zipMatch = Regex.Match(userMessage, @"[\w\-]+\.zip", RegexOptions.IgnoreCase);
                    var zipName = zipMatch.Success ? zipMatch.Value : "archive.zip";
                    return FileToolModule.CreateZip(zipName);
                }
            }

            // Minecraft / mod / compiled program refusals
            if ((lower.Contains("minecraft") || lower.Contains(" mod ") || lower.Contains("compiled program")) &&
                (lower.Contains("create") || lower.Contains("make")))
            {
                return "I can create plain text-based files (.txt, .md, .json, .csv, .log, .html, .css, .xml, .js, .py, .cs) and .zip archives of them. I can't build compiled programs or Minecraft mods.";
            }

            // File creation with smart extension detection
            if ((lower.Contains("create") || lower.Contains("make") || lower.Contains("write")) && lower.Contains("file"))
            {
                var extMatch = Regex.Match(userMessage, @"[a-zA-Z0-9_\-]+\.[a-zA-Z]{1,5}");

                string fileName;
                if (extMatch.Success)
                {
                    fileName = extMatch.Value;
                }
                else if (lower.Contains("html"))
                    fileName = "note.html";
                else if (lower.Contains("json"))
                    fileName = "note.json";
                else if (lower.Contains("css"))
                    fileName = "note.css";
                else if (lower.Contains("javascript") || lower.Contains("js file"))
                    fileName = "note.js";
                else if (lower.Contains("python") || lower.Contains("py file"))
                    fileName = "note.py";
                else if (lower.Contains("markdown") || lower.Contains("md file"))
                    fileName = "note.md";
                else
                    fileName = "note.txt";

                var ext = Path.GetExtension(fileName).ToLowerInvariant();

                var contentPrompt = $"<|im_start|>system<|im_sep|>You are a file content generator. The user wants to create a file named '{fileName}' ({ext}). Write ONLY the raw content that should go inside this file. Output ONLY the raw file content with no explanations, no greetings, no markdown fences, no intro sentences, no outro. For HTML files: write complete valid HTML starting with <!DOCTYPE html>. For JSON: write only valid JSON. For code files: write only code. Use actual line breaks. Do not write the characters \\n.<|im_end|><|im_start|>user<|im_sep|>{userMessage}<|im_end|><|im_start|>assistant<|im_sep|>";

                var generatedContent = await GenerateRawAsync(modelName, contentPrompt, 800);

                if (string.IsNullOrWhiteSpace(generatedContent))
                    generatedContent = "(The AI could not generate content for this file.)";

                return FileToolModule.CreateFile(fileName, generatedContent);
            }

            // List files
            if (lower.Contains("what files") || lower.Contains("list files") || lower.Contains("list my files") || lower.Contains("show files"))
            {
                return "Files in AVEIN-Files:\n" + FileToolModule.ListSandboxFiles();
            }

            // Who made you
            if (lower.Contains("who made you") || lower.Contains("who created you") || lower.Contains("who built you") || lower.Contains("who is your creator"))
            {
                return $"I was created by {OwnerName}, as part of the AVEIN project.";
            }

            // Standard chat with system prompt
            var systemPrompt = $"You are {modelName}, a helpful assistant created by {OwnerName} as part of the AVEIN project. " +
                "STRICT RULES:\n" +
                "1. Always reply in English, be concise and factual.\n" +
                "2. Never make up facts, dates, numbers, names, or events. If you are not sure, say 'I am not sure'.\n" +
                "3. If search results are provided, use ONLY that information.\n" +
                "4. Never claim you searched the web unless search results are literally shown to you below.\n" +
                "5. Never repeat words or phrases.\n" +
                "6. Do not invent fake tables, fake weather data, or fake statistics.\n" +
                $"7. If asked who made you, say {OwnerName}.\n" +
                "8. When asked to create files, output ONLY raw content - no explanations, no markdown fences.";

            if (deepThink)
                systemPrompt += " Think through this step by step. Put reasoning inside <thinking></thinking> tags, then final answer inside <answer></answer> tags.";

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
                else
                {
                    systemPrompt += " No search result was found. Say so honestly.";
                }
            }

            var prompt = $"<|im_start|>system<|im_sep|>{systemPrompt}<|im_end|><|im_start|>user<|im_sep|>{userMessage}<|im_end|><|im_start|>assistant<|im_sep|>";
            var answer = await GenerateRawAsync(modelName, prompt, deepThink ? 500 : 300);

            if (App.Config.IsModuleEnabled("web-search"))
                answer += searchUsed ? "\n\n[used live web search]" : "\n\n[no web search result used - answered from own knowledge]";

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
