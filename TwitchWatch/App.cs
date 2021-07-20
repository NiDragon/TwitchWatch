using System;
using System.IO;
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
using Microsoft.Extensions.Configuration;

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
        private static IConfiguration configuration;

        /// <summary>
        /// Quick dirty config loader
        /// </summary>
        /// <returns></returns>
        private static void LoadConfig()
        { 
            configuration= new ConfigurationBuilder()
                .AddJsonFile("config.json")
                .AddUserSecrets<App>()
                .AddEnvironmentVariables().Build();
        }

        // There is no need to implement IDisposable like before as we are
        // using dependency injection, which handles calling Dispose for us.
        static void Main(string[] args)
        {
            Console.Title = $"{Assembly.GetCallingAssembly().GetName().Name} - Version: {Assembly.GetCallingAssembly().GetName().Version.ToString()}";
            LoadConfig();
            if (args.Length > 0 && args[0] == "dumpconfig")
            {
                DumpConfig();
                return;
            }
            if (string.IsNullOrWhiteSpace(configuration["Discord:Token"]))
            {
                Console.WriteLine("Failed to load config file: \"App.conf\"");
                Environment.ExitCode = 1; // Non 0 signifies an error.
                return;
            }

            new App().MainAsync().GetAwaiter().GetResult();
        }

        private static void DumpConfig()
        {
            foreach (var configValue in configuration.AsEnumerable())
            {
                Console.WriteLine($"{configValue.Key}={configValue.Value}");
            }
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
                await client.LoginAsync(TokenType.Bot, configuration["Discord:Token"]);
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
                .AddSingleton(configuration)
                .AddSingleton<DiscordSocketClient>()
                .AddSingleton<CommandService>()
                .AddSingleton<CommandHandlingService>()
                .AddSingleton<HttpClient>()
                .AddSingleton<TwitchWatchService>()
                .BuildServiceProvider();
        }
    }
}
