using Rocket.API;

namespace Gaty.DiscordLinking
{
    public class DiscordLinkingConfiguration : IRocketPluginConfiguration
    {
        public string ApiUrl { get; set; }
        public int ListenPort { get; set; }

        public void LoadDefaults()
        {
            ApiUrl = "http://localhost:3000";
            ListenPort = 3000;
        }
    }
}