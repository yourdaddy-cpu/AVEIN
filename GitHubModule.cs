using System;
using System.Threading.Tasks;
using Octokit;

namespace AVEIN
{
    public sealed class GitHubModule
    {
        private GitHubClient _client;
        private string _currentUser;

        public static event Action<string> ActivityLogged;

        public bool IsConnected => _client != null && _client.Credentials != null;

        public GitHubModule(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
                return;

            _client = new GitHubClient(new ProductHeaderValue("AVEIN-AI"));
            _client.Credentials = new Credentials(token);
        }

        public async Task<bool> TestConnectionAsync()
        {
            try
            {
                var user = await _client.User.Current();
                _currentUser = user.Login;
                Log("Connected as: " + _currentUser);
                return true;
            }
            catch (Exception ex)
            {
                Log("GitHub connection failed: " + ex.Message);
                return false;
            }
        }

        public async Task<string> ListMyRepositoriesAsync()
        {
            try
            {
                var repos = await _client.Repository.GetAllForCurrent();
                var sb = new System.Text.StringBuilder();
                foreach (var repo in repos)
                    sb.AppendLine($"- {repo.FullName} ({(repo.Private ? "private" : "public")}) - default: {repo.DefaultBranch}");

                var result = sb.ToString().Trim();
                Log("Listed " + repos.Count + " repos");
                return string.IsNullOrEmpty(result) ? "(no repositories found)" : result;
            }
            catch (Exception ex)
            {
                Log("List repos failed: " + ex.Message);
                return "Error listing repos: " + ex.Message;
            }
        }

        public async Task<string> ListFilesAsync(string owner, string repo, string path = "")
        {
            try
            {
                var contents = await _client.Repository.Content.GetAllContents(owner, repo, path);
                var sb = new System.Text.StringBuilder();
                foreach (var item in contents)
                    sb.AppendLine($"- {item.Name} ({(item.Type == ContentType.Dir ? "folder" : "file")})");
                return sb.ToString().Trim();
            }
            catch (Exception ex)
            {
                Log("List files failed: " + ex.Message);
                return "Error listing files: " + ex.Message;
            }
        }

        public async Task<string> ReadFileAsync(string owner, string repo, string path)
        {
            try
            {
                var contents = await _client.Repository.Content.GetAllContents(owner, repo, path);
                Log("Read file: " + owner + "/" + repo + "/" + path);
                return contents[0].Content;
            }
            catch (Exception ex)
            {
                Log("Read file failed: " + ex.Message);
                return "Error reading file: " + ex.Message;
            }
        }

        public async Task<string> WriteFileAsync(string owner, string repo, string path, string content, string commitMessage)
        {
            try
            {
                try
                {
                    var existing = await _client.Repository.Content.GetAllContents(owner, repo, path);
                    var updateRequest = new UpdateFileRequest(commitMessage, content, existing[0].Sha);
                    await _client.Repository.Content.UpdateFile(owner, repo, path, updateRequest);
                    Log("Updated file: " + path);
                    return $"Updated {path} in {owner}/{repo}.";
                }
                catch (NotFoundException)
                {
                    var createRequest = new CreateFileRequest(commitMessage, content);
                    await _client.Repository.Content.CreateFile(owner, repo, path, createRequest);
                    Log("Created file: " + path);
                    return $"Created {path} in {owner}/{repo}.";
                }
            }
            catch (Exception ex)
            {
                Log("Write file failed: " + ex.Message);
                return "Error writing file: " + ex.Message;
            }
        }

        public async Task<string> SearchRepositoriesAsync(string query)
        {
            try
            {
                var request = new SearchRepositoriesRequest(query) { PerPage = 10 };
                var result = await _client.Search.SearchRepo(request);
                var sb = new System.Text.StringBuilder();
                foreach (var repo in result.Items)
                    sb.AppendLine($"- {repo.FullName} - {repo.Description}");
                return sb.ToString().Trim();
            }
            catch (Exception ex)
            {
                Log("Search failed: " + ex.Message);
                return "Error searching: " + ex.Message;
            }
        }

        private static void Log(string message)
        {
            ActivityLogged?.Invoke(DateTime.Now.ToString("HH:mm:ss") + " - [GitHub] " + message);
        }
    }
}
