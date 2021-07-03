using System;
using System.IO;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Discord;
using Discord.WebSocket;
using Discord.Commands;
using TwitchWatch.Services;
using System.Collections.Concurrent;
using System.Reflection;

namespace TwitchWatch
{
    // This is a minimal example of using Discord.Net's command
    // framework - by no means does it show everything the framework
    // is capable of.
    //
    // You can find samples of using the command framework:
    // - Here, under the 02_commands_framework sample
    // - https://github.com/foxbot/DiscordBotBase - a bare-bones bot template
    // - https://github.com/foxbot/patek - a more feature-filled bot, utilizing more aspects of the library
    class App
    {
        private static char[] trimChars = { ' ' };
        private static ConcurrentDictionary<string, string> m_config;

        /// <summary>
        /// Quick dirty config loader
        /// </summary>
        /// <returns></returns>
        public static bool LoadConfig()
        {
           App.m_config = new ConcurrentDictionary<string, string>();

            if (!File.Exists("./App.conf"))
                return false;

            using (TextReader tr = new StreamReader(File.Open("./App.conf", FileMode.Open)))
            {
                string raw = "";

                while ((raw = tr.ReadLine()) != null)
                {
                    // find any comment characters
                    int found = raw.IndexOf(';');

                    int comment = (found != -1) ? found : raw.Length;

                    string[] line = raw.Substring(0, comment).Split("=", StringSplitOptions.RemoveEmptyEntries);

                    if (line.Length > 1)
                    {
                        App.m_config.GetOrAdd(line[0].Trim(trimChars), line[1].Trim(trimChars));
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Get Accessor
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        public static string GetConfigValue(string key)
        {
            if(m_config.ContainsKey(key))
            {
                return m_config[key];
            }
            else
            {
                throw new Exception($"Missing or incorrect config key: {key}");
            }
        }

        // There is no need to implement IDisposable like before as we are
        // using dependency injection, which handles calling Dispose for us.
        static void Main(string[] args)
        {
            Console.Title = $"{Assembly.GetCallingAssembly().GetName().Name} - Version: {Assembly.GetCallingAssembly().GetName().Version.ToString()}";

            Console.BackgroundColor = ConsoleColor.DarkBlue;
            Console.Clear();
            Console.ForegroundColor = ConsoleColor.Gray;

            if (!LoadConfig())
            {
                Console.WriteLine("Failed to load config file: \"App.conf\"");

                while (true)
                    Thread.Sleep(10);
            }

            new App().MainAsync().GetAwaiter().GetResult();
        }

        public async Task MainAsync()
        {
            // You should dispose a service provider created using ASP.NET
            // when you are finished using it, at the end of your app's lifetime.
            // If you use another dependency injection framework, you should inspect
            // its documentation for the best way to do this.
            using (var services = ConfigureServices())
            {
                var client = services.GetRequiredService<DiscordSocketClient>();

                client.Log += LogAsync;
                services.GetRequiredService<CommandService>().Log += LogAsync;

                // Tokens should be considered secret data and never hard-coded.
                // We can read from the environment variable to avoid hardcoding.
                await client.LoginAsync(TokenType.Bot, App.GetConfigValue("Token"));
                await client.StartAsync();

                // Here we initialize the logic required to register our commands.
                await services.GetRequiredService<CommandHandlingService>().InitializeAsync();

                await Task.Delay(Timeout.Infinite);
            }
        }

        private Task LogAsync(LogMessage log)
        {
            Console.WriteLine(log.ToString());

            return Task.CompletedTask;
        }

        private ServiceProvider ConfigureServices()
        {
            return new ServiceCollection()
                .AddSingleton<DiscordSocketClient>()
                .AddSingleton<CommandService>()
                .AddSingleton<CommandHandlingService>()
                .AddSingleton<HttpClient>()
                .AddSingleton<TwitchWatchService>()
                .BuildServiceProvider();
        }
    }
}
