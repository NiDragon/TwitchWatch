using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using TwitchWatch.Services;

namespace TwitchWatch.Modules
{
    // Modules must be public and inherit from an IModuleBase
    public class PublicModule : ModuleBase<SocketCommandContext>
    {
        // Dependency Injection will fill this value in for us
        public TwitchWatchService TwitchUpdateService { get; set; }

        [Command("start")]
        [RequireContext(ContextType.Guild)]
        [RequireUserPermission(ChannelPermission.ManageChannels)]
        [RequireUserPermission(ChannelPermission.ManageMessages)]
        [RequireBotPermission(ChannelPermission.SendMessages)]
        [RequireBotPermission(ChannelPermission.ManageMessages)]
        public async Task StartTwitchWatch()
        {
            if (!TwitchUpdateService.IsRunning)
            {
                TwitchUpdateService.Start();
            }
            else
            {
                await Context.Channel.SendMessageAsync("Twitch Watch Service Already Running...");
                return;
            }

            await Context.Channel.SendMessageAsync("Twitch Watch Service **Started!**");
        }

        [Command("stop")]
        [RequireContext(ContextType.Guild)]
        [RequireUserPermission(ChannelPermission.ManageChannels)]
        [RequireUserPermission(ChannelPermission.ManageMessages)]
        [RequireBotPermission(ChannelPermission.SendMessages)]
        [RequireBotPermission(ChannelPermission.ManageMessages)]
        public async Task StopTwitchWatch()
        {
            if (!TwitchUpdateService.IsRunning)
            {
                await Context.Channel.SendMessageAsync("Twitch Watch Service Not Running...");
                return;
            }
            else
            {
                TwitchUpdateService.Stop();
            }

            await Context.Channel.SendMessageAsync("Twitch Watch Service **Stopped!**");
        }
    }
}
