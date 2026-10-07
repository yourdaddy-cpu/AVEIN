using System;
using System.Text.RegularExpressions;

namespace AVEIN
{
    public static class SafetyModule
    {
        private static readonly string[] BlockedTerms =
        {
            "format c:", "rm -rf /", "del /f /s /q c:\\",
            "shutdown /s", "shutdown -s",
            "dd if=", "mkfs.", ":(){:|:&};:"
        };

        private static readonly Regex[] BlockedPatterns =
        {
            new Regex(@"rm\s+-rf\s+/", RegexOptions.IgnoreCase),
            new Regex(@"format\s+[a-z]:", RegexOptions.IgnoreCase),
            new Regex(@"del\s+/[fsq]", RegexOptions.IgnoreCase),
            new Regex(@"shutdown\s+/[sr]", RegexOptions.IgnoreCase)
        };

        public static bool IsBlocked(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;

            var lower = text.ToLowerInvariant();
            foreach (var term in BlockedTerms)
                if (lower.Contains(term)) return true;

            foreach (var pattern in BlockedPatterns)
                if (pattern.IsMatch(text)) return true;

            return false;
        }

        public static string RefusalMessage()
        {
            return "I can't do that. It looks like a destructive command that could damage your system, so I'm refusing for your safety.";
        }
    }
}
