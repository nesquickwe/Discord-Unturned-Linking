using Newtonsoft.Json;
using Rocket.API;
using Rocket.Core.Plugins;
using Rocket.Unturned;
using Rocket.Unturned.Chat;
using Rocket.Unturned.Player;
using SDG.Unturned;
using Steamworks;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Xml.Serialization;
using UnityEngine;

namespace DiscordLinking
{
    // Configuration classes
    public class DiscordLinkingConfiguration : IRocketPluginConfiguration
    {
        public string ApiUrl;
        public int ListenPort;

        [XmlArrayItem("RoleMapping")]
        public List<RoleMapping> RoleMappings;

        public void LoadDefaults()
        {
            ApiUrl = "http://localhost:3000";
            ListenPort = 3001;

            RoleMappings = new List<RoleMapping>
            {
                new RoleMapping
                {
                    DiscordRoleId = "1450063138215952415",
                    PermissionGroup = "admin",
                    Priority = 100
                },
                new RoleMapping
                {
                    DiscordRoleId = "1450056803676327958",
                    PermissionGroup = "vip",
                    Priority = 50
                }
            };
        }
    }

    public class RoleMapping
    {
        [XmlAttribute]
        public string DiscordRoleId;

        [XmlAttribute]
        public string PermissionGroup;

        [XmlAttribute]
        public int Priority;
    }

    // Main Plugin
    public class DiscordLinkingPlugin : RocketPlugin<DiscordLinkingConfiguration>
    {
        public static DiscordLinkingPlugin Instance;
        private HttpClient httpClient;
        private HttpListener httpListener;
        private Thread listenerThread;

        protected override void Load()
        {
            Instance = this;
            httpClient = new HttpClient();

            // Start HTTP listener for incoming requests
            StartHttpListener();

            Rocket.Core.Logging.Logger.Log("Discord Linking Plugin Loaded!");
            Rocket.Core.Logging.Logger.Log($"API URL: {Configuration.Instance.ApiUrl}");
            Rocket.Core.Logging.Logger.Log($"Listening on port {Configuration.Instance.ListenPort}");
        }

        protected override void Unload()
        {
            StopHttpListener();
            httpClient?.Dispose();
            Rocket.Core.Logging.Logger.Log("Discord Linking Plugin Unloaded!");
        }

        private void StartHttpListener()
        {
            try
            {
                httpListener = new HttpListener();
                httpListener.Prefixes.Add($"http://localhost:{Configuration.Instance.ListenPort}/");
                httpListener.Start();

                listenerThread = new Thread(ListenForRequests);
                listenerThread.Start();

                Rocket.Core.Logging.Logger.Log($"HTTP Listener started on port {Configuration.Instance.ListenPort}");
            }
            catch (Exception ex)
            {
                Rocket.Core.Logging.Logger.LogError($"Failed to start HTTP listener: {ex.Message}");
            }
        }

        private void StopHttpListener()
        {
            try
            {
                if (httpListener != null && httpListener.IsListening)
                {
                    httpListener.Stop();
                    httpListener.Close();
                }

                if (listenerThread != null && listenerThread.IsAlive)
                {
                    listenerThread.Abort();
                }
            }
            catch (Exception ex)
            {
                Rocket.Core.Logging.Logger.LogError($"Error stopping HTTP listener: {ex.Message}");
            }
        }

        private void ListenForRequests()
        {
            while (httpListener.IsListening)
            {
                try
                {
                    var context = httpListener.GetContext();
                    var request = context.Request;
                    var response = context.Response;

                    if (request.HttpMethod == "POST" && request.Url.AbsolutePath == "/api/sync-permissions")
                    {
                        using (var reader = new System.IO.StreamReader(request.InputStream, request.ContentEncoding))
                        {
                            string json = reader.ReadToEnd();
                            var syncRequest = JsonConvert.DeserializeObject<PermissionSyncRequest>(json);

                            // Queue the permission sync on the main thread
                            ThreadPool.QueueUserWorkItem(state => SyncPlayerPermissions(syncRequest));

                            // Send response
                            string responseJson = JsonConvert.SerializeObject(new { success = true, message = "Permission sync queued" });
                            byte[] buffer = Encoding.UTF8.GetBytes(responseJson);
                            response.ContentType = "application/json";
                            response.ContentLength64 = buffer.Length;
                            response.OutputStream.Write(buffer, 0, buffer.Length);
                        }
                    }

                    response.Close();
                }
                catch (Exception ex)
                {
                    if (httpListener.IsListening)
                    {
                        Rocket.Core.Logging.Logger.LogError($"Error handling request: {ex.Message}");
                    }
                }
            }
        }

        private void SyncPlayerPermissions(PermissionSyncRequest request)
        {
            try
            {
                CSteamID steamId = new CSteamID(ulong.Parse(request.steamId));
                UnturnedPlayer player = UnturnedPlayer.FromCSteamID(steamId);

                if (player == null)
                {
                    Rocket.Core.Logging.Logger.LogWarning($"Player with Steam ID {request.steamId} is not online");
                    return;
                }

                // Find matching role mapping in configuration
                RoleMapping mapping = null;
                foreach (var roleMap in Configuration.Instance.RoleMappings)
                {
                    if (request.discordRoles.Contains(roleMap.DiscordRoleId))
                    {
                        if (mapping == null || roleMap.Priority > mapping.Priority)
                        {
                            mapping = roleMap;
                        }
                    }
                }

                if (mapping != null)
                {
                    // Add player to the permission group
                    var permissionProvider = Rocket.Core.R.Permissions;
                    var rocketPlayer = (Rocket.API.IRocketPlayer)player;

                    permissionProvider.AddPlayerToGroup(mapping.PermissionGroup, rocketPlayer);

                    UnturnedChat.Say(player, $"✅ Your permissions have been synced! You are now in the {mapping.PermissionGroup} group.", Color.green);
                    Rocket.Core.Logging.Logger.Log($"Synced permissions for {player.DisplayName} - Added to group: {mapping.PermissionGroup}");
                }
                else
                {
                    Rocket.Core.Logging.Logger.Log($"No permission group mapping found for {player.DisplayName}'s Discord roles");
                }
            }
            catch (Exception ex)
            {
                Rocket.Core.Logging.Logger.LogError($"Error syncing permissions: {ex.Message}");
            }
        }

        public async void LinkAccount(UnturnedPlayer player, string code)
        {
            try
            {
                var linkData = new
                {
                    code = code,
                    steamId = player.CSteamID.ToString(),
                    steamName = player.DisplayName
                };

                string json = JsonConvert.SerializeObject(linkData);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await httpClient.PostAsync($"{Configuration.Instance.ApiUrl}/api/link", content);
                var responseString = await response.Content.ReadAsStringAsync();
                var result = JsonConvert.DeserializeObject<ApiResponse>(responseString);

                if (result.success)
                {
                    UnturnedChat.Say(player, "✅ Your account has been successfully linked to Discord!", Color.green);
                    Rocket.Core.Logging.Logger.Log($"Successfully linked {player.DisplayName} ({player.CSteamID}) with code {code}");

                    // Request permission sync after successful link
                    RequestPermissionSync(player.CSteamID.ToString());
                }
                else
                {
                    UnturnedChat.Say(player, $"❌ Failed to link account: {result.message}", Color.red);
                    Rocket.Core.Logging.Logger.LogWarning($"Failed to link {player.DisplayName}: {result.message}");
                }
            }
            catch (Exception ex)
            {
                UnturnedChat.Say(player, "❌ An error occurred while linking your account. Please try again later.", Color.red);
                Rocket.Core.Logging.Logger.LogError($"Error linking account for {player.DisplayName}: {ex.Message}");
            }
        }

        private async void RequestPermissionSync(string steamId)
        {
            try
            {
                var syncData = new { steamId = steamId };
                string json = JsonConvert.SerializeObject(syncData);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                await httpClient.PostAsync($"{Configuration.Instance.ApiUrl}/api/request-sync", content);
            }
            catch (Exception ex)
            {
                Rocket.Core.Logging.Logger.LogError($"Error requesting permission sync: {ex.Message}");
            }
        }

        public async void CheckLink(UnturnedPlayer player)
        {
            try
            {
                var response = await httpClient.GetAsync($"{Configuration.Instance.ApiUrl}/api/check/{player.CSteamID}");
                var responseString = await response.Content.ReadAsStringAsync();
                var result = JsonConvert.DeserializeObject<CheckResponse>(responseString);

                if (result.linked)
                {
                    UnturnedChat.Say(player, $"✅ Your account is linked to Discord ID: {result.discordId}", Color.green);
                }
                else
                {
                    UnturnedChat.Say(player, "❌ Your account is not linked. Use the Discord bot to get a linking code!", Color.yellow);
                }
            }
            catch (Exception ex)
            {
                UnturnedChat.Say(player, "❌ An error occurred while checking your link status.", Color.red);
                Rocket.Core.Logging.Logger.LogError($"Error checking link for {player.DisplayName}: {ex.Message}");
            }
        }

        public async void SyncRoles(UnturnedPlayer player)
        {
            try
            {
                UnturnedChat.Say(player, "🔄 Syncing your Discord roles with in-game permissions...", Color.yellow);

                var response = await httpClient.GetAsync($"{Configuration.Instance.ApiUrl}/api/check/{player.CSteamID}");
                var responseString = await response.Content.ReadAsStringAsync();
                var result = JsonConvert.DeserializeObject<CheckResponse>(responseString);

                if (!result.linked)
                {
                    UnturnedChat.Say(player, "❌ Your account is not linked. Use the Discord bot to get a linking code first!", Color.red);
                    return;
                }

                // Request permission sync
                var syncData = new { steamId = player.CSteamID.ToString() };
                string json = JsonConvert.SerializeObject(syncData);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var syncResponse = await httpClient.PostAsync($"{Configuration.Instance.ApiUrl}/api/request-sync", content);
                var syncResponseString = await syncResponse.Content.ReadAsStringAsync();
                var syncResult = JsonConvert.DeserializeObject<ApiResponse>(syncResponseString);

                if (syncResult.success)
                {
                    UnturnedChat.Say(player, "✅ Role sync completed! Your permissions have been updated.", Color.green);
                    Rocket.Core.Logging.Logger.Log($"Role sync completed for {player.DisplayName} ({player.CSteamID})");
                }
                else
                {
                    UnturnedChat.Say(player, "❌ Failed to sync roles. Please try again later.", Color.red);
                }
            }
            catch (Exception ex)
            {
                UnturnedChat.Say(player, "❌ An error occurred while syncing your roles.", Color.red);
                Rocket.Core.Logging.Logger.LogError($"Error syncing roles for {player.DisplayName}: {ex.Message}");
            }
        }
    }

    // Data Models
    [Serializable]
    public class PermissionSyncRequest
    {
        public string steamId;
        public List<string> discordRoles;
    }

    [Serializable]
    public class ApiResponse
    {
        public bool success;
        public string message;
    }

    [Serializable]
    public class CheckResponse
    {
        public bool linked;
        public string discordId;
        public string steamName;
    }

    // Commands
    public class LinkCommand : IRocketCommand
    {
        public AllowedCaller AllowedCaller => AllowedCaller.Player;
        public string Name => "link";
        public string Help => "Link your Steam account to Discord";
        public string Syntax => "/link <code>";
        public List<string> Aliases => new List<string>();
        public List<string> Permissions => new List<string>();

        public void Execute(IRocketPlayer caller, string[] command)
        {
            UnturnedPlayer player = (UnturnedPlayer)caller;

            if (command.Length == 0)
            {
                UnturnedChat.Say(player, "❌ Usage: /link <code>", Color.red);
                UnturnedChat.Say(player, "Get your linking code from the Discord bot!", Color.yellow);
                return;
            }

            string code = command[0];

            if (code.Length != 10)
            {
                UnturnedChat.Say(player, "❌ Invalid code format. The code should be 10 characters long.", Color.red);
                return;
            }

            UnturnedChat.Say(player, "🔄 Linking your account...", Color.yellow);
            DiscordLinkingPlugin.Instance.LinkAccount(player, code);
        }
    }

    public class CheckLinkCommand : IRocketCommand
    {
        public AllowedCaller AllowedCaller => AllowedCaller.Player;
        public string Name => "checklink";
        public string Help => "Check if your account is linked to Discord";
        public string Syntax => "/checklink";
        public List<string> Aliases => new List<string> { "linked" };
        public List<string> Permissions => new List<string>();

        public void Execute(IRocketPlayer caller, string[] command)
        {
            UnturnedPlayer player = (UnturnedPlayer)caller;
            DiscordLinkingPlugin.Instance.CheckLink(player);
        }
    }

    public class RoleSyncCommand : IRocketCommand
    {
        public AllowedCaller AllowedCaller => AllowedCaller.Player;
        public string Name => "rolesync";
        public string Help => "Sync your Discord roles with in-game permissions";
        public string Syntax => "/rolesync";
        public List<string> Aliases => new List<string> { "syncperms", "syncroles" };
        public List<string> Permissions => new List<string>();

        public void Execute(IRocketPlayer caller, string[] command)
        {
            UnturnedPlayer player = (UnturnedPlayer)caller;
            DiscordLinkingPlugin.Instance.SyncRoles(player);
        }
    }
}