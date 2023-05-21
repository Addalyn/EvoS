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
    public class DiscordBotWrapper
    {
        private static readonly ILog log = LogManager.GetLogger(typeof(DiscordBotWrapper));

        private const string CMD_INFO = "info";
        private const string CMD_BROADCAST = "broadcast";
        
        private readonly DiscordSocketClient botClient;
        private static readonly DiscordSocketConfig discordConfig = new DiscordSocketConfig
        {
            GatewayIntents = GatewayIntents.AllUnprivileged | GatewayIntents.MessageContent
        };
        private readonly ulong? botChannelId;
        
        public DiscordBotWrapper(DiscordConfiguration conf)
        {
            log.Info("Discord bot is enabled");
            botClient = new DiscordSocketClient(discordConfig);
            if (!conf.BotChannelId.HasValue || conf.BotChannelId == 0)
            {
                botChannelId = null;
            }
            else
            {
                log.Info("Discord bot lobby channel is enabled");
                botChannelId = conf.BotChannelId;
            }
            botClient.Log += Log;
            botClient.Ready += Ready;
            botClient.SlashCommandExecuted += SlashCommandHandler;
            botClient.MessageReceived += ClientOnMessageReceived;
        }

        public async Task Login(DiscordConfiguration conf)
        {
            await botClient.LoginAsync(TokenType.Bot, conf.BotToken);
            await botClient.StartAsync();
            await botClient.SetGameAsync("Atlas Reactor");
        }

        public async Task Ready()
        {
            SlashCommandBuilder infoCommand = new SlashCommandBuilder();
            infoCommand.WithName(CMD_INFO);
            infoCommand.WithDescription("Print Atlas Reactor lobby status");

            SlashCommandBuilder broadcastCommand = new SlashCommandBuilder();
            broadcastCommand.WithName(CMD_BROADCAST);
            broadcastCommand.WithDescription("Send a broadcast to Atlas Reactor lobby");
            broadcastCommand.AddOption("message", ApplicationCommandOptionType.String, "Message to send", true);
            broadcastCommand.WithDefaultMemberPermissions(GuildPermission.ManageGuild);

            try
            {
                await botClient.CreateGlobalApplicationCommandAsync(infoCommand.Build());
                await botClient.CreateGlobalApplicationCommandAsync(broadcastCommand.Build());
            }
            catch (HttpException exception)
            {
                var json = JsonConvert.SerializeObject(exception.Errors, Formatting.Indented);
                log.Error(json);
            }
        }

        private async Task ClientOnMessageReceived(SocketMessage socketMessage)
        {
            // Check if Author is not a bot and allow only reading from the discord LobbyChannel
            if (botChannelId == null
                || socketMessage.Author.IsBot
                || socketMessage.Channel.Id != botChannelId
                || socketMessage.Author.IsWebhook)
            {
                return;
            }

            log.Info($"Discord message from {socketMessage.Author.Username}: {socketMessage.Content}");
            
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

        private async Task SlashCommandHandler(SocketSlashCommand command)
        {
            switch (command.Data.Name)
            {
                case CMD_INFO:
                {
                    DiscordLobbyUtils.Status status = DiscordLobbyUtils.GetStatus();
                    await command.RespondAsync(embed:
                        new EmbedBuilder
                        {
                            Title = DiscordLobbyUtils.BuildPlayerCountSummary(status),
                            Color = Color.Green
                        }.Build(), ephemeral: true);
                    break;
                }
                case CMD_BROADCAST:
                {
                    string msg = command.Data.Options.First().Value.ToString();
                    ChatNotification message = new ChatNotification
                    {
                        SenderHandle = command.User.Username,
                        ConsoleMessageType = ConsoleMessageType.BroadcastMessage,
                        Text = msg,
                    };
                    SessionManager.Broadcast(message);
                    await command.RespondAsync($"Broadcast: {msg}", ephemeral: true);
                    break;
                }
            }
        }

        private static Task Log(LogMessage msg)
        {
            return DiscordUtils.Log(log, msg);
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
            ulong? _channelId = channelIdOverride ?? botChannelId;
            if (_channelId.Value == 0) return null;
            IMessageChannel chnl = botClient.GetChannel(_channelId.Value) as IMessageChannel;
            return chnl.SendMessageAsync(text, isTTS, embed, options, allowedMentions, messageReference, components, stickers, embeds, flags);
        }
    }
}