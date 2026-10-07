using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace AVEIN
{
    public sealed class ChatMessage
    {
        [JsonProperty("sender")]
        public string Sender { get; set; }

        [JsonProperty("text")]
        public string Text { get; set; }

        [JsonProperty("timestamp")]
        public string Timestamp { get; set; }
    }

    public sealed class ChatSession
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("createdAt")]
        public string CreatedAt { get; set; }

        [JsonProperty("title")]
        public string Title { get; set; }

        [JsonProperty("messages")]
        public List<ChatMessage> Messages { get; set; } = new List<ChatMessage>();
    }

    public static class HistoryStore
    {
        private static string SessionsPath =>
            Path.Combine(AppContext.BaseDirectory, "data", "sessions.json");

        public static List<ChatSession> LoadAllSessions()
        {
            try
            {
                if (File.Exists(SessionsPath))
                {
                    var json = File.ReadAllText(SessionsPath);
                    var loaded = JsonConvert.DeserializeObject<List<ChatSession>>(json);
                    if (loaded != null) return loaded;
                }
            }
            catch { }

            return new List<ChatSession>();
        }

        private static void SaveAll(List<ChatSession> sessions)
        {
            var dir = Path.GetDirectoryName(SessionsPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(SessionsPath, JsonConvert.SerializeObject(sessions, Formatting.Indented));
        }

        public static ChatSession CreateSession()
        {
            var sessions = LoadAllSessions();
            var session = new ChatSession
            {
                Id = Guid.NewGuid().ToString("N"),
                CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
                Title = "New conversation"
            };
            sessions.Insert(0, session);
            SaveAll(sessions);
            return session;
        }

        public static void AppendMessage(string sessionId, string sender, string text)
        {
            var sessions = LoadAllSessions();
            var session = sessions.FirstOrDefault(s => s.Id == sessionId);
            if (session == null) return;

            session.Messages.Add(new ChatMessage
            {
                Sender = sender,
                Text = text,
                Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm")
            });

            if (session.Title == "New conversation" && sender == "You")
            {
                session.Title = text.Length > 40 ? text.Substring(0, 40) + "..." : text;
            }

            SaveAll(sessions);
        }

        public static ChatSession GetSession(string sessionId)
        {
            return LoadAllSessions().FirstOrDefault(s => s.Id == sessionId);
        }

        public static void DeleteSession(string sessionId)
        {
            var sessions = LoadAllSessions();
            sessions.RemoveAll(s => s.Id == sessionId);
            SaveAll(sessions);
        }
    }
}
