using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace AVEIN
{
    public sealed class AppConfig
    {
        [JsonProperty("theme")]
        public string Theme { get; set; } = "default";

        [JsonProperty("modules")]
        public Dictionary<string, bool> Modules { get; set; } = new Dictionary<string, bool>
        {
            { "chat", true },
            { "settings", true },
            { "plugins", true },
            { "microphone", false },
            { "local-ai", true },
            { "api", false },
            { "memory", true },
            { "vision", false },
            { "automation", false },
            { "web-search", false },
        };

        [JsonProperty("window")]
        public WindowSettings Window { get; set; } = new WindowSettings();

        public bool IsModuleEnabled(string moduleId)
        {
            return Modules.TryGetValue(moduleId, out var enabled) && enabled;
        }

        public void SetModuleEnabled(string moduleId, bool enabled)
        {
            Modules[moduleId] = enabled;
            Save();
        }

        private static string ConfigPath =>
            Path.Combine(AppContext.BaseDirectory, "config", "app.json");

        public static AppConfig Load()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    var json = File.ReadAllText(ConfigPath);
                    var loaded = JsonConvert.DeserializeObject<AppConfig>(json);
                    if (loaded != null) return loaded;
                }
            }
            catch { }

            var defaults = new AppConfig();
            defaults.Save();
            return defaults;
        }

        public void Save()
        {
            var dir = Path.GetDirectoryName(ConfigPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var json = JsonConvert.SerializeObject(this, Formatting.Indented);
            File.WriteAllText(ConfigPath, json);
        }
    }

    public sealed class WindowSettings
    {
        [JsonProperty("width")]
        public double Width { get; set; } = 1000;

        [JsonProperty("height")]
        public double Height { get; set; } = 650;

        [JsonProperty("maximized")]
        public bool Maximized { get; set; } = false;
    }
}
