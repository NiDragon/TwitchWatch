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
using Microsoft.Extensions.Configuration;

namespace TwitchWatch.Services
{
    public partial class TwitchWatchService
    {
        private readonly DiscordSocketClient _client;
        private readonly HttpClient _http;
        private readonly IConfiguration _configuration;
        private List<Stream> m_activeStreams;
        private Dictionary<string, ulong> m_messageMap;

        /// <summary>
        /// Default to 60 seconds
        /// </summary>
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

        //TODO: Possibly move to IHostedService for easier docker monitoring
        /// <summary>
        /// On any request that might flood the server we request that this fails and throws an exception where acceptable.
        /// </summary>
        private static readonly RequestOptions RequestFailure = new RequestOptions { RetryMode = RetryMode.AlwaysFail };

        public TwitchWatchService(DiscordSocketClient client, HttpClient http,IConfiguration configuration)
        {
            _client = client;
            _http = http;
            _configuration = configuration;

            m_activeStreams = new List<Stream>();
            m_messageMap = new Dictionary<string, ulong>();

            EchoChannel = ulong.Parse(_configuration["EchoChannel"]);

            string intv = _configuration["UpdateInterval"];

            if (intv != string.Empty)
            {
                UpdateInterval = TimeSpan.FromSeconds(int.Parse(intv));

                // Cap this to a minute as to not abuse the API
                if (UpdateInterval.TotalMilliseconds < 60)
                {
                    UpdateInterval = TimeSpan.FromSeconds(60);
                }
            }

            RunOnStart = bool.Parse(_configuration["RunOnStart"]);

            // Handle a disconnect kill the logic loop wait for exit
            _client.Disconnected += (evt) =>
            {
                Console.WriteLine("Connection To Discord Lost Shutting Down.");
                Console.WriteLine($"Reason: {evt.Message}");

                _Started = false;

                while (_IsRunning)
                    Thread.Sleep(10);

                // Try to reconnect if this was started manually otherwise Ready will do it for us
                if (!RunOnStart)
                {
                    DateTime reconTime = DateTime.Now;

                    while (_client.ConnectionState != ConnectionState.Connected)
                    { 
                        Thread.Sleep(10);

                        if(DateTime.Now - reconTime >= TimeSpan.FromSeconds(30))
                        {
                            Console.WriteLine("Failed To Restart Timeout Reached!");
                            break;
                        }
                    }

                    if (_client.ConnectionState == ConnectionState.Connected)
                        Start();
                }

                return Task.CompletedTask;
            };

            // Only if RunOnStart is set true in the config
            if (RunOnStart)
            {
                _client.Ready += () =>
                {
                    while (_IsRunning)
                        Thread.Sleep(10);

                    ResetWatchService();

                    _Started = true;

                    // Start a new task for the loop keep a reference to this?
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
        /// Start a new task and clear the old data if any exists
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

        /// <summary>
        /// Await the stopping of any existing work
        /// </summary>
        public async void Stop()
        {
            _Started = false;

            await Task.Run(() =>
            {
                while (_IsRunning)
                    Thread.Sleep(10);
            });
        }

        /// <summary>
        /// This clears the twitch watch service data
        ///
        /// if this fails just throw an error and stop
        /// </summary>
        private async void ResetWatchService()
        {
            m_activeStreams = new List<Stream>();
            m_messageMap = new Dictionary<string, ulong>();

            var socket = _client.GetChannel(EchoChannel);

            if (socket == null)
                return;

            SocketTextChannel output = socket as SocketTextChannel;

            var messages = await Discord.AsyncEnumerableExtensions.FlattenAsync(output.GetMessagesAsync(100));

            try
            {
                await output.DeleteMessagesAsync(messages.Where(p => p.IsPinned.Equals(false)), RequestFailure);
            }
            catch (Exception e)
            {
                Console.WriteLine("Attempt to reset TwitchWatch service encountered an error and thrown an exception.");
                Console.WriteLine($"Reason: {e.Message}");
            }
        }

        /// <summary>
        /// Announce a new stream throw an exception if this fails
        /// </summary>
        /// <param name="strm"></param>
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
                Discord.Rest.RestUserMessage Rum = (await output.SendMessageAsync("", false, builder.Build(), RequestFailure));

                if (Rum == null)
                {
                    Console.WriteLine("Failed To Send Announce Message!");
                    return;
                }

                m_messageMap.Add(strm.id.ToString(), Rum.Id);
            }
            catch (Exception e)
            {
                Console.WriteLine("Failed To Send Announce Message!");
                Console.WriteLine($"Reason: {e.Message}");
            }
        }

        private string[] _games = { "489642", "505991", "518276" };

        private async void DoRunWhileAlive()
        {
            bool init = true;
            Stopwatch lastUpdate = new Stopwatch();

            IsRunning = true;

            string AuthToken = string.Empty;

            string ClientID = _configuration["Twitch:ClientID"];
            string Secret = _configuration["Twitch:ClientSecret"];

            HttpResponseMessage response = null;

            // Setup Twitch Api Access
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

            // Begin Update Loop
            while (_Started)
            {
                // Check if we have a valid text channel
                var socket = _client.GetChannel(EchoChannel);

                if (socket == null)
                {
                    Console.WriteLine("Could Not Locate Channel Waiting 5 Seconds To Try Again!");
                    Thread.Sleep(5000);
                    continue;
                }

                SocketTextChannel output = socket as SocketTextChannel;

                bool BadData = false;

                // Should we update if this is initial yes or if we passed the update interval
                if (lastUpdate.Elapsed >= UpdateInterval || init)
                {
                    // List of streams returned from Twitch API
                    List<Stream> strms = new List<Stream>();

                    // Loop over all games in the game list this should not be hard coded?
                    for (int i = 0; i < _games.Count(); i++)
                    {
                        // Collect a twitch response
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

                    // If the stream data was invalid dont do anything and wait till next time
                    if (BadData)
                    {
                        lastUpdate.Restart();
                        init = false;
                        continue;
                    }

                    // Add new streams (Should we wait between new posts?)
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
                                // This section has some considerations to make...
                                // In the event DeleteMessageAsync throws an exception and fails
                                // we may be stuck with a orphaned message in the message map.

                                // Remove from active streams
                                m_activeStreams.RemoveAt(i);

                                // Discord message delete
                                await output.DeleteMessageAsync(m_messageMap[currentStream.id.ToString()]);

                                // Remove the associated message
                                m_messageMap.Remove(currentStream.id.ToString());
                            }
                            catch (Exception removeMessageException)
                            {
                                Console.WriteLine($"Failed to remove offline stream reason: {removeMessageException.Message}");
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

                                    await output.ModifyMessageAsync(m_messageMap[currentStream.id], m => { m.Embed = builder.Build(); }, RequestFailure);
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
