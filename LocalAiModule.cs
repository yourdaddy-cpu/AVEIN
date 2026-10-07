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
        private static bool _backendConfigured = false;

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

            // Configure the native backend once on first use
            if (!_backendConfigured)
            {
                NativeLibraryConfig.All.WithCuda(true);
                _backendConfigured = true;
            }

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
                Temperature = 0.3f,
                RepeatPenalty = 1.5f,
                AntiPrompts = new System.Collections.Generic.List<string> { "<|im_end|>", "<|im_start|>" }
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

            if (lower.Contains("what") && (lower.Contains("open") || lower.Contains("running")) && (lower.Contains("window") || lower.Contains("app") || lower.Contains("screen")))
            {
                return "Here's what's currently open:\n" + FileToolModule.GetOpenWindowsList();
            }

            if (lower.Contains("zip") || lower.Contains(" mod") || lower.Contains("compress") || lower.Contains("minecraft"))
            {
                if (lower.Contains("create") || lower.Contains("make") || lower.Contains("convert"))
                {
                    return "I can only create a single plain text-type file (like .txt, .md, .json, .csv, .log) inside the AVEIN-Files folder - I can't build zip archives, Minecraft mods, or compiled programs. Want me to create a text file instead?";
                }
            }

            if ((lower.Contains("create") || lower.Contains("make")) && lower.Contains("file"))
            {
                var extMatch = Regex.Match(userMessage, @"[a-zA-Z0-9_\-]+\.[a-zA-Z]{1,5}");
                var fileName = extMatch.Success ? extMatch.Value : "note.txt";

                var contentPrompt = $"<|im_start|>system<|im_sep|>You are a file content generator. The user wants to create a file named '{fileName}'. Write ONLY the content that should go inside this file. Format the text beautifully with paragraphs, bullet points, and proper spacing. Use actual line breaks, never write the characters \\n. Do not write any explanations, greetings, or markdown fences. Do not mention the file name. Just the raw content.<|im_end|><|im_start|>user<|im_sep|>{userMessage}<|im_end|><|im_start|>assistant<|im_sep|>";

                var generatedContent = await GenerateRawAsync(modelName, contentPrompt, 500);

                if (string.IsNullOrWhiteSpace(generatedContent))
                    generatedContent = "(The AI could not generate content for this file.)";

                return FileToolModule.CreateFile(fileName, generatedContent);
            }

            if (lower.Contains("what files") || lower.Contains("list files") || lower.Contains("list my files"))
            {
                return "Files in AVEIN-Files:\n" + FileToolModule.ListSandboxFiles();
            }

            if (lower.Contains("who made you") || lower.Contains("who created you") || lower.Contains("who built you") || lower.Contains("who is your creator"))
            {
                return $"I was created by {OwnerName}, as part of the AVEIN project.";
            }

            var systemPrompt = $"You are {modelName}, a helpful assistant created by {OwnerName} as part of the AVEIN project. " +
                "STRICT RULES:\n" +
                "1. Always reply in English, be concise and factual.\n" +
                "2. Never make up facts, dates, numbers, names, or events. If you are not sure, say 'I am not sure' or 'I do not know'.\n" +
                "3. If search results are provided, use ONLY that information. Do not add extra facts from your own knowledge.\n" +
                "4. Never claim you searched the web unless search results are literally shown to you below.\n" +
                "5. Never repeat words or phrases.\n" +
                "6. Do not invent fake tables, fake weather data, or fake statistics.\n" +
                $"7. If asked who made you, say {OwnerName}.\n" +
                "8. You can only create plain text files (.txt, .md, .json, .csv, .log, .html, .css, .xml).";

            if (deepThink)
            {
                systemPrompt += " Think through this step by step before answering. Put your reasoning inside <thinking></thinking> tags, then your final answer inside <answer></answer> tags. Keep the reasoning brief.";
            }

            var searchUsed = false;
            if (App.Config.IsModuleEnabled("web-search"))
            {
                var hasInternet = await WebSearchModule.IsInternetAvailableAsync();
                if (!hasInternet)
                {
                    return "Internet is off, so I can't search right now. Turn on Web Search access in Settings, or ask me something I might already know.";
                }

                var searchResult = await WebSearchModule.SearchAsync(userMessage);
                if (!string.IsNullOrEmpty(searchResult))
                {
                    systemPrompt += " Real search result: " + searchResult;
                    searchUsed = true;
                }
                else
                {
                    systemPrompt += " No search result was found. Say so honestly, then answer from your own knowledge if you can.";
                }
            }

            var prompt = $"<|im_start|>system<|im_sep|>{systemPrompt}<|im_end|><|im_start|>user<|im_sep|>{userMessage}<|im_end|><|im_start|>assistant<|im_sep|>";
            var answer = await GenerateRawAsync(modelName, prompt, deepThink ? 500 : 300);

            if (App.Config.IsModuleEnabled("web-search"))
            {
                answer += searchUsed ? "\n\n[used live web search]" : "\n\n[no web search result used - answered from own knowledge]";
            }

            return answer;
        }

        private static string CleanUp(string text)
        {
            text = text.Replace("Ċ", "\n");
            text = text.Replace("<|im_end|>", "");
            text = text.Replace("<|im_start|>", "");
            text = text.Replace("<|im_sep|>", "");
            text = text.Replace("assistant", "");
            return text.Trim();
        }
    }
}
