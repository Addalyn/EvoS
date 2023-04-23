using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CentralServer.LobbyServer.Session;
using Discord;
using Discord.Net;
using Discord.WebSocket;
using EvoS.Framework.Constants.Enums;
using EvoS.Framework.Network.NetworkMessages;
using log4net;
using Newtonsoft.Json;

namespace CentralServer.LobbyServer.Discord
{
    public class DiscordClientWrapper
    {
        private static readonly ILog log = LogManager.GetLogger(typeof(DiscordClientWrapper));
        
        private readonly DiscordSocketClient client;
        private readonly ulong? channelId;
        private static bool isReady = false;
        private static readonly DiscordSocketConfig discordConfig = new DiscordSocketConfig
        {
            GatewayIntents = GatewayIntents.AllUnprivileged | GatewayIntents.MessageContent
        };

        public DiscordClientWrapper(DiscordConfiguration conf)
        {

            client = new DiscordSocketClient(discordConfig);
            channelId = conf.LobbyChannel;
            client.LoginAsync(TokenType.Bot, conf.BotToken);
            client.StartAsync();
            client.SetGameAsync("Atlas Reactor");
            client.Log += Log;
            client.Ready += Ready;
            client.SlashCommandExecuted += SlashCommandHandler;
            client.MessageReceived += ClientOnMessageReceived;
        }

        public async Task Ready()
        {
            SlashCommandBuilder infoCommand = new SlashCommandBuilder();
            infoCommand.WithName("info");
            infoCommand.WithDescription("Retrieve info from Atlas Reactor");

            SlashCommandBuilder broadcastCommand = new SlashCommandBuilder();
            broadcastCommand.WithName("broadcast");
            broadcastCommand.WithDescription("Send a broadcast to atlas reactor lobby");
            broadcastCommand.AddOption("message", ApplicationCommandOptionType.String, "Message to send", true);
            broadcastCommand.WithDefaultMemberPermissions(GuildPermission.ManageGuild);

            try
            {
                await client.CreateGlobalApplicationCommandAsync(infoCommand.Build());
                await client.CreateGlobalApplicationCommandAsync(broadcastCommand.Build());
            }
            catch (HttpException exception)
            {
                var json = JsonConvert.SerializeObject(exception.Errors, Formatting.Indented);
                log.Info(json);
            }
            isReady = true;
        }

        private async Task ClientOnMessageReceived(SocketMessage socketMessage)
        {
            await Task.Run(() =>
            {
                // Check if Author is not a bot and allow only reading from the discord LobbyChannel
                if (!socketMessage.Author.IsBot && socketMessage.Channel.Id == channelId)
                {
                    ChatNotification message = new ChatNotification
                    {
                        SenderHandle = $"(Discord) {socketMessage.Author.Username}",
                        ConsoleMessageType = ConsoleMessageType.GlobalChat,
                        Text = socketMessage.Content,
                    };
                    foreach (long playerAccountId in SessionManager.GetOnlinePlayers())
                    {
                        LobbyServerProtocol player = SessionManager.GetClientConnection(playerAccountId);
                        if (player != null && player.CurrentServer == null)
                        {
                            player.Send(message);
                        }
                    }
                }
            });
        }

        private async Task SlashCommandHandler(SocketSlashCommand command)
        {
            if (command.Data.Name == "info")
            {
                DiscordLobbyUtils.Status status = DiscordLobbyUtils.GetStatus();
                await command.RespondAsync(embed:
                                new EmbedBuilder
                                {
                                    Title = DiscordLobbyUtils.BuildPlayerCountSummary(status),
                                    Color = Color.Green
                                }.Build(), ephemeral: true);
            }
            if (command.Data.Name == "broadcast")
            {
                ChatNotification message = new ChatNotification
                {
                    SenderHandle = command.User.Username,
                    ConsoleMessageType = ConsoleMessageType.BroadcastMessage,
                    Text = command.Data.Options.First().Value.ToString(),
                };
                foreach (long playerAccountId in SessionManager.GetOnlinePlayers())
                {
                    LobbyServerProtocol player = SessionManager.GetClientConnection(playerAccountId);
                    if (player != null)
                    {
                        player.Send(message);
                    }
                }
                await command.RespondAsync("Broadcast send", ephemeral: true);
            }
        }

        private static Task Log(LogMessage msg)
        {
            return DiscordUtils.Log(log, msg);
        }

        public bool IsReady()
        {
            return isReady;
        }

        public Task<IUserMessage> SendMessageAsync(
            string text = null,
            bool isTTS = false,
            Embed embed = null,
            RequestOptions options = null,
            AllowedMentions allowedMentions = null,
            MessageReference messageReference = null,
            MessageComponent components = null,
            ISticker[] stickers = null,
            Embed[] embeds = null,
            MessageFlags flags = MessageFlags.None,
            ulong? channelIdOverride = null)
        {
            ulong? _channelId = channelIdOverride ?? channelId;
            if (_channelId.Value == 0) return null;
            IMessageChannel chnl = client.GetChannel(_channelId.Value) as IMessageChannel;
            return chnl.SendMessageAsync(text, isTTS, embed, options, allowedMentions, messageReference, components, stickers, embeds, flags);
        }
    }
}