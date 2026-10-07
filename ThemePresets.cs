using System.Collections.Generic;

namespace AVEIN
{
    public static class ThemePresets
    {
        public static List<ThemePalette> All => new List<ThemePalette>
        {
            // ─── LIGHT ─────────────────────────────────────────────
            new ThemePalette { Name = "Clean Light", Background = "#F7F8FA", Surface = "#FFFFFF",
                PrimaryText = "#1B1D21", SecondaryText = "#6B7280", Accent = "#4F6EF7",
                Border = "#E5E7EB", SidebarBackground = "#FFFFFF" },

            new ThemePalette { Name = "Paper", Background = "#F5F1E8", Surface = "#FFFDF7",
                PrimaryText = "#2B2416", SecondaryText = "#7A6F5A", Accent = "#B58B3C",
                Border = "#E0D8C2", SidebarBackground = "#FBF6E9" },

            new ThemePalette { Name = "Mint", Background = "#EAF7F0", Surface = "#FFFFFF",
                PrimaryText = "#16362A", SecondaryText = "#5C8574", Accent = "#10B981",
                Border = "#C7E6D6", SidebarBackground = "#F1FAF5" },

            new ThemePalette { Name = "Sky", Background = "#EAF3FF", Surface = "#FFFFFF",
                PrimaryText = "#0F2544", SecondaryText = "#5A7796", Accent = "#0EA5E9",
                Border = "#C9DEF5", SidebarBackground = "#F2F8FF" },

            new ThemePalette { Name = "Peach", Background = "#FFF1EA", Surface = "#FFFFFF",
                PrimaryText = "#3A1A0F", SecondaryText = "#9B6A55", Accent = "#FB7185",
                Border = "#F5D2C2", SidebarBackground = "#FFF6F0" },

            new ThemePalette { Name = "Lavender Light", Background = "#F3EFFF", Surface = "#FFFFFF",
                PrimaryText = "#251A44", SecondaryText = "#736299", Accent = "#8B5CF6",
                Border = "#DFD3FF", SidebarBackground = "#F8F5FF" },

            // ─── DARK ──────────────────────────────────────────────
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

            new ThemePalette { Name = "Cyberpunk", Background = "#0A0A14", Surface = "#14141F",
                PrimaryText = "#F0F0FF", SecondaryText = "#8888AA", Accent = "#FF2E97",
                Border = "#2A2A3E", SidebarBackground = "#0E0E18" },

            new ThemePalette { Name = "Matrix", Background = "#050A05", Surface = "#0A150A",
                PrimaryText = "#C8FFC8", SecondaryText = "#5A905A", Accent = "#00FF41",
                Border = "#123A12", SidebarBackground = "#071007" },

            new ThemePalette { Name = "Blood Red", Background = "#170606", Surface = "#240A0A",
                PrimaryText = "#F5DCDC", SecondaryText = "#B07070", Accent = "#DC2626",
                Border = "#3E1414", SidebarBackground = "#1C0808" },

            new ThemePalette { Name = "Gold", Background = "#1A1405", Surface = "#2A200A",
                PrimaryText = "#FFF4D6", SecondaryText = "#C4A550", Accent = "#FACC15",
                Border = "#463514", SidebarBackground = "#201809" },

            new ThemePalette { Name = "Teal", Background = "#071619", Surface = "#0E2429",
                PrimaryText = "#DFF5F5", SecondaryText = "#6FA0A8", Accent = "#14B8A6",
                Border = "#1B3940", SidebarBackground = "#0A1C20" },

            new ThemePalette { Name = "Deep Navy", Background = "#050B18", Surface = "#0C1730",
                PrimaryText = "#DDE6F5", SecondaryText = "#7285A8", Accent = "#60A5FA",
                Border = "#16264A", SidebarBackground = "#081026" },

            new ThemePalette { Name = "Chocolate", Background = "#170F0A", Surface = "#261910",
                PrimaryText = "#F2E0D0", SecondaryText = "#A8836A", Accent = "#A16207",
                Border = "#3A2718", SidebarBackground = "#1D130B" },

            new ThemePalette { Name = "Slate", Background = "#0F1319", Surface = "#1A1F27",
                PrimaryText = "#E2E8F0", SecondaryText = "#7A8798", Accent = "#94A3B8",
                Border = "#2A313C", SidebarBackground = "#13171E" },

            // ─── VIBRANT ───────────────────────────────────────────
            new ThemePalette { Name = "Sunset", Background = "#2A0E1A", Surface = "#3D1428",
                PrimaryText = "#FFE5EE", SecondaryText = "#C4809A", Accent = "#FF6B6B",
                Border = "#5A1F35", SidebarBackground = "#321020" },

            new ThemePalette { Name = "Vaporwave", Background = "#1A0F2E", Surface = "#251846",
                PrimaryText = "#F0E5FF", SecondaryText = "#9B85C4", Accent = "#F472B6",
                Border = "#3D2A66", SidebarBackground = "#1F1238" },

            new ThemePalette { Name = "Aurora", Background = "#05101A", Surface = "#0A1C2E",
                PrimaryText = "#D6F5FF", SecondaryText = "#6AA5C4", Accent = "#00D4FF",
                Border = "#153A52", SidebarBackground = "#071523" },

            new ThemePalette { Name = "Toxic", Background = "#0A1205", Surface = "#141F0A",
                PrimaryText = "#E5FFC8", SecondaryText = "#7AA050", Accent = "#A3E635",
                Border = "#223314", SidebarBackground = "#0E1808" },

            new ThemePalette { Name = "Crimson Night", Background = "#12060A", Surface = "#1E0A12",
                PrimaryText = "#F5DCE5", SecondaryText = "#A87084", Accent = "#BE123C",
                Border = "#34141F", SidebarBackground = "#170810" },

            new ThemePalette { Name = "Ice", Background = "#0A1418", Surface = "#132229",
                PrimaryText = "#E0F5F8", SecondaryText = "#6FA0AA", Accent = "#67E8F9",
                Border = "#1D3540", SidebarBackground = "#0E1A20" },

            new ThemePalette { Name = "Amethyst", Background = "#150A22", Surface = "#221040",
                PrimaryText = "#EDE0FF", SecondaryText = "#9678C4", Accent = "#C084FC",
                Border = "#34215E", SidebarBackground = "#1A0E2E" },
        };
    }
}
