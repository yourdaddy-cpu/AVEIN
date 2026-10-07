using System.Collections.Generic;

namespace AVEIN
{
    public static class ThemePresets
    {
        public static List<ThemePalette> All => new List<ThemePalette>
        {
            new ThemePalette { Name = "Clean Light", Background = "#F7F8FA", Surface = "#FFFFFF",
                PrimaryText = "#1B1D21", SecondaryText = "#6B7280", Accent = "#4F6EF7",
                Border = "#E5E7EB", SidebarBackground = "#FFFFFF" },

            new ThemePalette { Name = "Ocean Blue", Background = "#0F1729", Surface = "#1A2540",
                PrimaryText = "#E8EDF7", SecondaryText = "#8B9CC2", Accent = "#3B82F6",
                Border = "#2A3A5C", SidebarBackground = "#141D33" },

            new ThemePalette { Name = "Midnight Purple", Background = "#150E22", Surface = "#211636",
                PrimaryText = "#EEE8F9", SecondaryText = "#9C8FBB", Accent = "#A855F7",
                Border = "#332448", SidebarBackground = "#1B1230" },

            new ThemePalette { Name = "Forest Green", Background = "#0E1A14", Surface = "#16261D",
                PrimaryText = "#E4F3EA", SecondaryText = "#8FB39C", Accent = "#22C55E",
                Border = "#233A2C", SidebarBackground = "#122019" },

            new ThemePalette { Name = "Ember Orange", Background = "#1C130C", Surface = "#2A1D12",
                PrimaryText = "#F8EDE3", SecondaryText = "#C4A488", Accent = "#F97316",
                Border = "#3D2A18", SidebarBackground = "#221709" },

            new ThemePalette { Name = "Rose", Background = "#1E1015", Surface = "#2B1922",
                PrimaryText = "#F8E9EE", SecondaryText = "#C494A8", Accent = "#EC4899",
                Border = "#3D2530", SidebarBackground = "#24141B" },
        };
    }
}
