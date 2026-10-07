using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace AVEIN
{
    public static class FileToolModule
    {
        public static event Action<string> ActivityLogged;

        private static readonly string[] AllowedExtensions =
            { ".txt", ".md", ".json", ".csv", ".log", ".html", ".css", ".xml", ".js", ".py", ".cs", ".xaml" };

        private static string SandboxFolder =>
            Path.Combine(AppContext.BaseDirectory, "AVEIN-Files");

        public static string GetOpenWindowsList()
        {
            try
            {
                var sb = new StringBuilder();
                foreach (var proc in Process.GetProcesses())
                {
                    if (!string.IsNullOrWhiteSpace(proc.MainWindowTitle))
                        sb.AppendLine($"- {proc.ProcessName}: {proc.MainWindowTitle}");
                }
                var result = sb.ToString().Trim();
                Log("Read open windows list");
                return string.IsNullOrEmpty(result) ? "(no visible windows found)" : result;
            }
            catch (Exception ex)
            {
                Log("Failed to read windows: " + ex.Message);
                return "(couldn't read open windows)";
            }
        }

        public static string CreateFolder(string folderName)
        {
            try
            {
                var safeFolder = SanitizePath(folderName);
                var fullPath = Path.Combine(SandboxFolder, safeFolder);

                if (!Directory.Exists(fullPath))
                {
                    Directory.CreateDirectory(fullPath);
                    Log("Created folder: " + safeFolder);
                    return $"Created folder '{safeFolder}' inside AVEIN-Files.";
                }
                return $"Folder '{safeFolder}' already exists.";
            }
            catch (Exception ex)
            {
                Log("Folder creation failed: " + ex.Message);
                return "Couldn't create that folder: " + ex.Message;
            }
        }

        public static string CreateFile(string fileName, string content)
        {
            if (SafetyModule.IsBlocked(fileName) || SafetyModule.IsBlocked(content))
            {
                Log("Blocked file creation request: " + fileName);
                return SafetyModule.RefusalMessage();
            }

            var safeName = Path.GetFileName(fileName);
            var safeFolder = Path.GetDirectoryName(fileName);
            var ext = Path.GetExtension(safeName).ToLowerInvariant();

            if (!AllowedExtensions.Contains(ext))
            {
                Log("Blocked disallowed file type: " + ext);
                return $"I can only create these file types: {string.Join(", ", AllowedExtensions)}. Not {ext}.";
            }

            try
            {
                if (!Directory.Exists(SandboxFolder))
                    Directory.CreateDirectory(SandboxFolder);

                var targetDir = string.IsNullOrEmpty(safeFolder)
                    ? SandboxFolder
                    : Path.Combine(SandboxFolder, SanitizePath(safeFolder));

                if (!Directory.Exists(targetDir))
                    Directory.CreateDirectory(targetDir);

                var fullPath = Path.Combine(targetDir, safeName);
                var formattedContent = FormatContent(content);

                File.WriteAllText(fullPath, formattedContent);
                Log("Created file: " + safeName);
                return $"Created {safeName} inside the AVEIN-Files folder. Open it from: {fullPath}";
            }
            catch (Exception ex)
            {
                Log("File creation failed: " + ex.Message);
                return "Couldn't create that file: " + ex.Message;
            }
        }

        public static string CreateZip(string zipFileName, string sourceFolder = null)
        {
            try
            {
                var safeName = Path.GetFileName(zipFileName);
                if (!safeName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    safeName += ".zip";

                var sourcePath = string.IsNullOrEmpty(sourceFolder)
                    ? SandboxFolder
                    : Path.Combine(SandboxFolder, SanitizePath(sourceFolder));

                if (!Directory.Exists(sourcePath))
                {
                    Log("Zip source folder not found: " + sourcePath);
                    return $"Source folder not found. Create some files first, then zip them.";
                }

                if (!Directory.Exists(SandboxFolder))
                    Directory.CreateDirectory(SandboxFolder);

                var destPath = Path.Combine(SandboxFolder, safeName);

                if (File.Exists(destPath))
                    File.Delete(destPath);

                ZipFile.CreateFromDirectory(sourcePath, destPath, CompressionLevel.Fastest, false);

                var info = new FileInfo(destPath);
                Log($"Created zip file: {safeName} ({info.Length} bytes)");
                return $"Successfully created '{safeName}' inside AVEIN-Files ({info.Length} bytes). Location: {destPath}";
            }
            catch (Exception ex)
            {
                Log("Zip creation failed: " + ex.Message);
                return "Couldn't create the zip file: " + ex.Message;
            }
        }

        public static string ListSandboxFiles()
        {
            try
            {
                if (!Directory.Exists(SandboxFolder))
                    return "(AVEIN-Files folder is empty or doesn't exist yet)";

                var files = Directory.GetFiles(SandboxFolder, "*", SearchOption.AllDirectories)
                    .Select(f => f.Replace(SandboxFolder + "\\", ""));
                var list = string.Join("\n", files);
                Log("Listed sandbox files");
                return string.IsNullOrEmpty(list) ? "(no files yet)" : list;
            }
            catch (Exception ex)
            {
                return "Couldn't list files: " + ex.Message;
            }
        }

        private static string SanitizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return "";
            var invalidChars = Path.GetInvalidPathChars();
            var clean = new string(path.Where(c => !invalidChars.Contains(c)).ToArray());
            return clean.Replace("..", "").Trim('\\', '/');
        }

        private static string FormatContent(string content)
        {
            if (string.IsNullOrEmpty(content)) return content;

            content = content.Replace("\\n", Environment.NewLine);

            content = Regex.Replace(content, @"^```[a-zA-Z]*\r?\n?", "", RegexOptions.Multiline);
            content = Regex.Replace(content, @"\r?\n?```$", "", RegexOptions.Multiline);

            return content.Trim();
        }

        private static void Log(string message)
        {
            ActivityLogged?.Invoke(DateTime.Now.ToString("HH:mm:ss") + " - " + message);
        }
    }
}
