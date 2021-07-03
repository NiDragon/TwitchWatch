#define OAUTH2

using Discord;
using Discord.WebSocket;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TwitchWatch.Services
{
    public class TwitchWatchService
    {
        public class Stream
        {
            public string id { get; set; }
            public string user_id { get; set; }
            public string user_login { get; set; }
            public string user_name { get; set; }
            public string game_id { get; set; }
            public string game_name { get; set; }
            public string type { get; set; }
            public string title { get; set; }
            public int viewer_count { get; set; }
            public DateTime started_at { get; set; }
            public string language { get; set; }
            public string thumbnail_url { get; set; }
            public List<string> tag_ids { get; set; }
            public bool is_mature { get; set; }
        }

        public class Pagination
        {
            public string cursor { get; set; }
        }

        public class StreamsRequest
        {
            public List<Stream> data { get; set; }
            public Pagination pagination { get; set; }
        }

        public class OAuth2Request
        {
            public string access_token { get; set; }
            public string refresh_token { get; set; }
            public object expires_in { get; set; }
            public List<string> scope { get; set; }
            public string token_type { get; set; }
        }

        private readonly DiscordSocketClient _client;
        private readonly HttpClient _http;
        private List<Stream> m_activeStreams;
        private Dictionary<string, ulong> m_messageMap;

        // Default to 60 seconds
        private TimeSpan UpdateInterval = TimeSpan.FromSeconds(60);
        private ulong EchoChannel = 0;

        private bool _IsRunning = false;
        public bool IsRunning
        {
            get { return _IsRunning; }
            private set { _IsRunning = value; }
        }
        private bool _Started = false;
        private bool RunOnStart = false;

        public TwitchWatchService(DiscordSocketClient client, HttpClient http)
        {
            _client = client;
            _http = http;

            m_activeStreams = new List<Stream>();
            m_messageMap = new Dictionary<string, ulong>();

            EchoChannel = ulong.Parse(App.GetConfigValue("EchoChannel"));

            string intv = App.GetConfigValue("UpdateInterval");

            if (intv != string.Empty)
            {
                UpdateInterval = TimeSpan.FromSeconds(int.Parse(intv));

                // Cap this to a minute as to not abuse the API
                if (UpdateInterval.TotalMilliseconds < 60)
                {
                    UpdateInterval = TimeSpan.FromSeconds(60);
                }
            }

            RunOnStart = bool.Parse(App.GetConfigValue("RunOnStart"));

            if (RunOnStart)
            {
                _client.Ready += () =>
                {
                    ResetWatchService();

                    _Started = true;

                    Task.Factory.StartNew(
                    new Action(() =>
                    {
                        DoRunWhileAlive();
                    }));

                    return Task.CompletedTask;
                };
            }
        }

        #region Tasks

        /// <summary>
        /// Clear the watch list at startup
        /// </summary>
        public async void Start()
        {
            ResetWatchService();

            _Started = true;

            await Task.Factory.StartNew(
            new Action(() =>
            {
                DoRunWhileAlive();
            }));
        }

        public async void Stop()
        {
            _Started = false;

            await Task.Run(() =>
            {
                while (_IsRunning)
                    Thread.Sleep(10);
            });
        }

        private async void ResetWatchService()
        {
            m_activeStreams = new List<Stream>();
            m_messageMap = new Dictionary<string, ulong>();

            var socket = _client.GetChannel(EchoChannel);

            if (socket == null)
                return;

            SocketTextChannel output = socket as SocketTextChannel;

            var messages = await Discord.AsyncEnumerableExtensions.FlattenAsync(output.GetMessagesAsync(100));
            await output.DeleteMessagesAsync(messages.Where(p => p.IsPinned.Equals(false)));
        }

        private async void Announce(Stream strm)
        {
            var socket = _client.GetChannel(EchoChannel);

            if (socket == null)
            {
                Console.WriteLine("Failed to announce channel will try again later!");
                return;
            }

            SocketTextChannel output = socket as SocketTextChannel;

            // Add announced stream
            m_activeStreams.Add(strm);

            // Build Message
            EmbedBuilder builder = new EmbedBuilder();

            builder.WithTitle($"{strm.game_name} - {strm.user_name}");
            builder.WithThumbnailUrl(strm.thumbnail_url.Replace("{width}", "128").Replace("{height}", "128"));
            builder.WithDescription($"**{strm.title}**\nViewers: {strm.viewer_count}");
            builder.WithUrl($"https://twitch.tv/{strm.user_name}");
            builder.WithColor(Color.Purple);

            // Store Announcement
            try
            {
                Discord.Rest.RestUserMessage Rum = (await output.SendMessageAsync("", false, builder.Build()));

                if (Rum == null)
                {
                    Console.WriteLine("Failed To Send Announce Message!");
                    return;
                }

                m_messageMap.Add(strm.id.ToString(), Rum.Id);
            }
            catch (Exception)
            {
                Console.WriteLine("Failed To Send Announce Message!");
            }
        }

        private string[] _games = { "489642", "505991", "518276" };

        private async void DoRunWhileAlive()
        {
            bool init = true;
            Stopwatch lastUpdate = new Stopwatch();

            IsRunning = true;

            string AuthToken = string.Empty;

            string ClientID = App.GetConfigValue("ClientID");
            string Secret = App.GetConfigValue("ClientSecret");

            HttpResponseMessage response = null;

            using (var request = new HttpRequestMessage(new HttpMethod("POST"), $"https://id.twitch.tv/oauth2/token?client_id={ClientID}&client_secret={Secret}&grant_type=client_credentials"))
            {
                try
                {
                    response = await _http.SendAsync(request);
                    var content = response.Content;

                    var result = JsonConvert.DeserializeObject<OAuth2Request>(content.ReadAsStringAsync().Result);

                    AuthToken = result.access_token;
                }
                catch (Exception)
                {
                    IsRunning = false;
                    _Started = false;

                    // Error Catch Auth
                    Console.WriteLine($"Failed To Authenticate OAuth2 Reason:{response.ReasonPhrase}");

                    return;
                }
            }

            while (_Started)
            {
                var socket = _client.GetChannel(EchoChannel);

                if (socket == null)
                {
                    Console.WriteLine("Could Not Locate Channel Waiting 5 Seconds To Try Again!");
                    Thread.Sleep(5000);
                    continue;
                }

                SocketTextChannel output = socket as SocketTextChannel;

                bool BadData = false;

                if (lastUpdate.Elapsed >= UpdateInterval || init)
                {
                    // List of streams returned from Twitch API
                    List<Stream> strms = new List<Stream>();

                    for (int i = 0; i < _games.Count(); i++)
                    {
                        using (var request = new HttpRequestMessage(new HttpMethod("GET"), $"https://api.twitch.tv/helix/streams?game_id={_games[i]}"))
                        {
                            try
                            {
                                request.Headers.TryAddWithoutValidation("client-id", ClientID);
                                request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {AuthToken}");

                                response = await _http.SendAsync(request);
                                var content = response.Content;

                                strms.AddRange(JsonConvert.DeserializeObject<StreamsRequest>(content.ReadAsStringAsync().Result).data);
                            }
                            catch (Exception)
                            {
                                Console.WriteLine($"Failed To Fetch Stream Reason: {response?.ReasonPhrase}");
                                BadData = true;
                                break;
                            }
                        }
                    }

                    if (BadData)
                    {
                        lastUpdate.Restart();
                        init = false;
                        continue;
                    }

                    // Add new streams
                    foreach (Stream activeStream in strms)
                    {
                        if (m_activeStreams.FindIndex(p => p.id.ToString() == activeStream.id.ToString()) == -1)
                        {
                            if (activeStream.type.ToLower() != "live")
                                continue;

                            Announce(activeStream);
                        }
                    }

                    // Check for streams that went offline update viewer count otherwise
                    for (int i = m_activeStreams.Count - 1; i >= 0; i--)
                    {
                        Stream currentStream = m_activeStreams[i];
                        string _id = currentStream.id.ToString();

                        // Check if this stream is no longer active and remove it
                        if (strms.FindIndex(p => p.id.ToString() == _id) == -1)
                        {
                            try
                            {
                                // Remove from active streams
                                m_activeStreams.RemoveAt(i);

                                // Discord message delete
                                await output.DeleteMessageAsync(m_messageMap[currentStream.id.ToString()]);

                                // Remove the associated message
                                m_messageMap.Remove(currentStream.id.ToString());
                            }
                            catch (Exception removeMessageException)
                            {
                                Console.WriteLine($"Failed to remove offline stream reason: {removeMessageException}");
                            }
                        }
                        else
                        {
                            try
                            {
                                // If they are still streaming and we have a message listed update it
                                if (m_messageMap.ContainsKey(currentStream.id))
                                {
                                    m_activeStreams[i].viewer_count = strms.Find(p => p.id.ToString() == _id).viewer_count;
                                    currentStream = m_activeStreams[i];

                                    EmbedBuilder builder = new EmbedBuilder();

                                    builder.WithTitle($"{currentStream.game_name} - {currentStream.user_name}");
                                    builder.WithThumbnailUrl(currentStream.thumbnail_url.Replace("{width}", "128").Replace("{height}", "128"));
                                    builder.WithDescription($"**{currentStream.title}**\nViewers: {currentStream.viewer_count}");
                                    builder.WithUrl($"https://twitch.tv/{currentStream.user_name}");
                                    builder.WithColor(Color.Purple);

                                    await output.ModifyMessageAsync(m_messageMap[currentStream.id], m => { m.Embed = builder.Build(); });
                                }
                            }
                            catch (Exception updateMessageFail)
                            {
                                Console.WriteLine($"Failed to update stream viewer count reason: {updateMessageFail.Message}");
                            }
                        }
                    }

                    lastUpdate.Restart();
                    init = false;
                }
                else
                    Thread.Sleep(10);
            }

            IsRunning = false;
        }
        #endregion
    }
}
