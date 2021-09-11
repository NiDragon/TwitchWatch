using System.Collections.Generic;

namespace TwitchWatch.Services
{
    public partial class TwitchWatchService
    {
        public class OAuth2Request
        {
            public string access_token { get; set; }
            public string refresh_token { get; set; }
            public object expires_in { get; set; }
            public List<string> scope { get; set; }
            public string token_type { get; set; }
        }
    }
}