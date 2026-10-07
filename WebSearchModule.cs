using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AVEIN
{
    public static class WebSearchModule
    {
        public static event Action<string> ActivityLogged;

        public static string ApiKey { get; set; } = "";

        private const string Endpoint = "https://api.langsearch.com/v1/web-search";
        private static readonly HttpClient _client;

        static WebSearchModule()
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            _client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        }

        public static async Task<bool> IsInternetAvailableAsync()
        {
            try
            {
                var response = await _client.GetAsync("https://www.google.com/generate_204");
                return response.IsSuccessStatusCode || (int)response.StatusCode == 204;
            }
            catch { return false; }
        }

        public static async Task<string> SearchAsync(string query)
        {
            Log("Searching: " + query);

            if (string.IsNullOrWhiteSpace(query)) return null;

            var apiKey = ResolveApiKey();
            if (string.IsNullOrEmpty(apiKey))
            {
                Log("No LangSearch API key found.");
                return null;
            }

            if (!await IsInternetAvailableAsync())
            {
                Log("Internet is off - search skipped");
                return null;
            }

            try
            {
                var body = new JObject
                {
                    ["query"] = query,
                    ["count"] = 5,
                    ["contents"] = new JObject { ["text"] = true },
                    ["freshness"] = "noLimit"
                };

                var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
                {
                    Content = new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json")
                };
                request.Headers.Add("Authorization", "Bearer " + apiKey);

                var response = await _client.SendAsync(request);
                var jsonText = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    Log("Search API error: HTTP " + (int)response.StatusCode);
                    return null;
                }

                var json = JObject.Parse(jsonText);
                var results = json["data"]?["webPages"]?["value"] as JArray;
                if (results == null || results.Count == 0)
                {
                    Log("No usable result for this query");
                    return null;
                }

                var sb = new StringBuilder();
                int taken = 0;
                foreach (var item in results)
                {
                    if (taken >= 3) break;

                    var title = item["name"]?.ToString();
                    var url = item["url"]?.ToString();
                    var text = item["text"]?.ToString() ?? item["snippet"]?.ToString();
                    var cleaned = Sanitize(text);
                    if (string.IsNullOrEmpty(cleaned)) continue;

                    sb.AppendLine("Source: " + (string.IsNullOrEmpty(title) ? "(untitled)" : title));
                    if (!string.IsNullOrEmpty(url)) sb.AppendLine("URL: " + url);
                    sb.AppendLine(cleaned);
                    sb.AppendLine();
                    taken++;
                }

                var final = sb.ToString().Trim();
                if (string.IsNullOrEmpty(final))
                {
                    Log("No usable result for this query");
                    return null;
                }

                Log("Found " + taken + " result(s)");
                return final;
            }
            catch (Exception ex)
            {
                Log("Search failed: " + ex.Message);
                return null;
            }
        }

        private static string ResolveApiKey()
        {
            if (!string.IsNullOrWhiteSpace(ApiKey)) return ApiKey.Trim();

            try
            {
                var keyFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "langsearch.key");
                if (File.Exists(keyFile))
                {
                    var text = File.ReadAllText(keyFile).Trim();
                    if (!string.IsNullOrEmpty(text)) return text;
                }
            }
            catch { }

            return null;
        }

        private static string Sanitize(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            if (text.Contains("<?php") || text.Contains("<html") || text.Contains("<script") || text.Contains("function "))
                return null;

            text = Regex.Replace(text, "<.*?>", "");
            if (text.Length > 300) text = text.Substring(0, 300);
            return text.Trim();
        }

        private static void Log(string message)
        {
            ActivityLogged?.Invoke(DateTime.Now.ToString("HH:mm:ss") + " - " + message);
        }
    }
}
