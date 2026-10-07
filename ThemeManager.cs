using System;
using System.IO;
using Newtonsoft.Json;

namespace AVEIN
{
    public sealed class ThemePalette
    {
        [JsonProperty("name")] public string Name { get; set; } = "Clean Light";
        [JsonProperty("background")] public string Background { get; set; } = "#F7F8FA";
        [JsonProperty("surface")] public string Surface { get; set; } = "#FFFFFF";
        [JsonProperty("primaryText")] public string PrimaryText { get; set; } = "#1B1D21";
        [JsonProperty("secondaryText")] public string SecondaryText { get; set; } = "#6B7280";
        [JsonProperty("accent")] public string Accent { get; set; } = "#4F6EF7";
        [JsonProperty("border")] public string Border { get; set; } = "#E5E7EB";
        [JsonProperty("sidebarBackground")] public string SidebarBackground { get; set; } = "#FFFFFF";
    }

    public static class ThemeManager
    {
        private static string ThemePath =>
            Path.Combine(AppContext.BaseDirectory, "config", "theme.json");

        public static ThemePalette Load()
        {
            try
            {
                if (File.Exists(ThemePath))
                {
                    var json = File.ReadAllText(ThemePath);
                    var loaded = JsonConvert.DeserializeObject<ThemePalette>(json);
                    if (loaded != null) return loaded;
                }
            }
            catch { }

            var defaults = new ThemePalette();
            Save(defaults);
            return defaults;
        }

        public static void Save(ThemePalette palette)
        {
            var dir = Path.GetDirectoryName(ThemePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var json = JsonConvert.SerializeObject(palette, Formatting.Indented);
            File.WriteAllText(ThemePath, json);
        }
    }
}
