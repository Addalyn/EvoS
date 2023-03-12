﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using CentralServer.LobbyServer;
using CentralServer.LobbyServer.Session;
using Discord;
using Discord.Webhook;
using EvoS.Framework;
using EvoS.Framework.Constants.Enums;
using EvoS.Framework.Misc;
using EvoS.Framework.Network.NetworkMessages;
using EvoS.Framework.Network.Static;
using EvoS.Framework.Network.Unity;
using log4net;
using WebSocketSharp;
using WebSocketSharp.Server;

namespace CentralServer.BridgeServer
{
    public class BridgeServerProtocol : WebSocketBehaviorBase
    {
        private static readonly ILog log = LogManager.GetLogger(typeof(BridgeServerProtocol));

        public string Address;
        public int Port;
        private LobbySessionInfo SessionInfo;
        public LobbyGameInfo GameInfo { private set; get; }
        private LobbyServerTeamInfo TeamInfo;
        public List<LobbyServerProtocol> clients = new List<LobbyServerProtocol>();
        public string URI => "ws://" + Address + ":" + Port;
        public GameStatus GameStatus { get; private set; } = GameStatus.Stopped;
        public string ProcessCode { get; } = "Artemis" + DateTime.Now.Ticks;
        public string Name => SessionInfo?.UserName ?? "ATLAS";
        public string BuildVersion => SessionInfo?.BuildVersion ?? "";
        public bool IsPrivate { get; private set; }
        public bool IsConnected { get; private set; } = true;

        public LobbyServerPlayerInfo GetServerPlayerInfo(long accountId)
        {
            return TeamInfo.TeamPlayerInfo.Find(p => p.AccountId == accountId);
        }

        public IEnumerable<long> GetPlayers(Team team)
        {
            return from p in TeamInfo.TeamInfo(team) select p.AccountId;
        }

        public IEnumerable<long> GetPlayers()
        {
            return from p in TeamInfo.TeamPlayerInfo select p.AccountId;
        }

        public static readonly List<Type> BridgeMessageTypes = new List<Type>
        {
            typeof(RegisterGameServerRequest),
            typeof(RegisterGameServerResponse),
            typeof(LaunchGameRequest),
            typeof(JoinGameServerRequest),
            null, // typeof(JoinGameAsObserverRequest),
            typeof(ShutdownGameRequest),
            null, // typeof(DisconnectPlayerRequest),
            null, // typeof(ReconnectPlayerRequest),
            null, // typeof(MonitorHeartbeatResponse),
            typeof(ServerGameSummaryNotification),
            typeof(PlayerDisconnectedNotification),
            typeof(ServerGameMetricsNotification),
            typeof(ServerGameStatusNotification),
            typeof(MonitorHeartbeatNotification),
            typeof(LaunchGameResponse),
            typeof(JoinGameServerResponse),
            null, // typeof(JoinGameAsObserverResponse)
        };
        
        protected List<Type> GetMessageTypes()
        {
            return BridgeMessageTypes;
        }

        protected override string GetConnContext()
        {
            return $"S {Address}:{Port}";
        }

        protected override async void HandleMessage(MessageEventArgs e)
        {
            NetworkReader networkReader = new NetworkReader(e.RawData);
            short messageType = networkReader.ReadInt16();
            int callbackId = networkReader.ReadInt32();
            List<Type> messageTypes = GetMessageTypes();
            if (messageType >= messageTypes.Count)
            {
                log.Error($"Unknown bridge message type {messageType}");
                return;
            }

            Type type = messageTypes[messageType];

            if (type == typeof(RegisterGameServerRequest))
            {
                RegisterGameServerRequest request = Deserialize<RegisterGameServerRequest>(networkReader);
                log.Debug($"< {request.GetType().Name} {DefaultJsonSerializer.Serialize(request)}");
                string data = request.SessionInfo.ConnectionAddress;
                Address = data.Split(":")[0];
                Port = Convert.ToInt32(data.Split(":")[1]);
                SessionInfo = request.SessionInfo;
                IsPrivate = request.isPrivate;
                ServerManager.AddServer(this);

                Send(new RegisterGameServerResponse
                    {
                        Success = true
                    },
                    callbackId);
            }
            else if (type == typeof(ServerGameSummaryNotification))
            {
                try 
                {
                    ServerGameSummaryNotification request = Deserialize<ServerGameSummaryNotification>(networkReader);

                    if (request.GameSummary == null)
                    {
                        GameInfo.GameResult = GameResult.TieGame;
                        request.GameSummary = new LobbyGameSummary();
                    }
                    else 
                    { 
                        GameInfo.GameResult = request.GameSummary.GameResult;
                    }

                    log.Debug($"< {request.GetType().Name} {DefaultJsonSerializer.Serialize(request)}");
                    log.Info($"Game {GameInfo?.Name} at {request.GameSummary?.GameServerAddress} finished " +
                                            $"({request.GameSummary?.NumOfTurns} turns), " +
                                            $"{request.GameSummary?.GameResult} {request.GameSummary?.TeamAPoints}-{request.GameSummary?.TeamBPoints}");

                    request.GameSummary.BadgeAndParticipantsInfo = new List<BadgeAndParticipantInfo>();

                    if (GameInfo.GameResult != GameResult.TieGame)
                    {
                        PlayerGameSummary highestHealingPlayer = request.GameSummary.PlayerGameSummaryList.OrderByDescending(p => p.GetTotalHealingFromAbility() + p.TotalPlayerAbsorb).FirstOrDefault();
                        PlayerGameSummary highestDamagePlayer = request.GameSummary.PlayerGameSummaryList.OrderByDescending(p => p.TotalPlayerDamage).FirstOrDefault();
                        PlayerGameSummary highestDamageRecievedPlayer = request.GameSummary.PlayerGameSummaryList.OrderByDescending(p => p.TotalPlayerDamageReceived).FirstOrDefault();
                        PlayerGameSummary highestDamagePerTurn = request.GameSummary.PlayerGameSummaryList.OrderByDescending(p => p.GetDamageDealtPerTurn()).FirstOrDefault();
                        PlayerGameSummary highestDamageTakenPerLife = request.GameSummary.PlayerGameSummaryList.OrderByDescending(p => p.GetDamageTakenPerLife()).FirstOrDefault();
                        PlayerGameSummary highestEnemiesSightedPerTurn = request.GameSummary.PlayerGameSummaryList.OrderByDescending(p => p.EnemiesSightedPerTurn).FirstOrDefault();
                        PlayerGameSummary highestMitigated = request.GameSummary.PlayerGameSummaryList.OrderByDescending(p => p.GetTeamMitigation()).FirstOrDefault();
                        PlayerGameSummary highestDamageEfficiency = request.GameSummary.PlayerGameSummaryList.OrderByDescending(p => p.DamageEfficiency).FirstOrDefault();
                        PlayerGameSummary highestDamageDonePerLife = request.GameSummary.PlayerGameSummaryList.OrderByDescending(p => p.GetDamageDonePerLife()).FirstOrDefault();
                        PlayerGameSummary highestDodge = request.GameSummary.PlayerGameSummaryList.OrderByDescending(p => p.DamageAvoidedByEvades).FirstOrDefault();
                        PlayerGameSummary highestCrowdControl = request.GameSummary.PlayerGameSummaryList.OrderByDescending(p => p.MovementDeniedByMe).FirstOrDefault();
                        PlayerGameSummary highestBoostTeamDamage = request.GameSummary.PlayerGameSummaryList.OrderByDescending(p => p.MyOutgoingExtraDamageFromEmpowered).FirstOrDefault();
                        PlayerGameSummary highestBoostTeamEnergize = request.GameSummary.PlayerGameSummaryList.OrderByDescending(p => p.TeamExtraEnergyByEnergizedFromMe).FirstOrDefault();
                        List<PlayerGameSummary> sortedPlayersEnemiesSightedPerTurn = request.GameSummary.PlayerGameSummaryList.OrderByDescending(p => p.EnemiesSightedPerTurn).ToList();
                        List<PlayerGameSummary> sortedPlayersFreelancerStats = request.GameSummary.PlayerGameSummaryList.OrderByDescending(p => p.FreelancerStats.OrderByDescending(p => p).FirstOrDefault()).ToList();
                        List<PlayerGameSummary> sortedPlayersDamageDealtPerTurn = request.GameSummary.PlayerGameSummaryList.OrderByDescending(p => p.GetDamageDealtPerTurn()).ToList();
                        List<PlayerGameSummary> sortedPlayersDamageEfficiency = request.GameSummary.PlayerGameSummaryList.OrderByDescending(p => p.DamageEfficiency).ToList();
                        List<PlayerGameSummary> sortedPlayersDamageDonePerLife = request.GameSummary.PlayerGameSummaryList.OrderByDescending(p => p.GetDamageDonePerLife()).ToList();
                        List<PlayerGameSummary> sortedPlayersDamageTakenPerLife = request.GameSummary.PlayerGameSummaryList.OrderByDescending(p => p.GetDamageTakenPerLife()).ToList();
                        List<PlayerGameSummary> sortedPlayersDodge = request.GameSummary.PlayerGameSummaryList.OrderByDescending(p => p.DamageAvoidedByEvades).ToList();
                        List<PlayerGameSummary> sortedPlayersCrowdControl = request.GameSummary.PlayerGameSummaryList.OrderByDescending(p => p.MovementDeniedByMe).ToList();
                        List<PlayerGameSummary> sortedPlayersHealedShielded = request.GameSummary.PlayerGameSummaryList.OrderByDescending(p => p.GetTotalHealingFromAbility() + p.TotalPlayerAbsorb).ToList();
                        List<PlayerGameSummary> sortedPlayersBoostTeamDamage = request.GameSummary.PlayerGameSummaryList.OrderByDescending(p => p.MyOutgoingExtraDamageFromEmpowered).ToList();
                        List<PlayerGameSummary> sortedPlayersBoostTeamEnergize = request.GameSummary.PlayerGameSummaryList.OrderByDescending(p => p.TeamExtraEnergyByEnergizedFromMe).ToList();

                        Dictionary<int, List<BadgeInfo>> badgeInfos = new Dictionary<int, List<BadgeInfo>>();

                        foreach (PlayerGameSummary player in request.GameSummary.PlayerGameSummaryList)
                        {
                            List<BadgeInfo> playerBadgeInfos = new List<BadgeInfo>();

                            if (player.NumAssists == 3) playerBadgeInfos.Add(new BadgeInfo() { BadgeId = 1 });
                            if (player.NumAssists == 4) playerBadgeInfos.Add(new BadgeInfo() { BadgeId = 2 });
                            if (player.NumAssists == 5) playerBadgeInfos.Add(new BadgeInfo() { BadgeId = 3 });

                            int playerIndexEnemiesSightedPerTurn = sortedPlayersEnemiesSightedPerTurn.FindIndex(p => p.PlayerId == player.PlayerId);

                            if (playerIndexEnemiesSightedPerTurn >= 0 && highestEnemiesSightedPerTurn != null && highestEnemiesSightedPerTurn.PlayerId == player.PlayerId)
                            {
                                int totalPlayers = sortedPlayersEnemiesSightedPerTurn.Count;
                                double percentile = (totalPlayers - playerIndexEnemiesSightedPerTurn - 1) * 100.0 / totalPlayers;

                                if (percentile > 50) playerBadgeInfos.Add(new BadgeInfo() { BadgeId = 4 });
                                if (percentile > 75) playerBadgeInfos.Add(new BadgeInfo() { BadgeId = 5 });
                                if (percentile > 80) playerBadgeInfos.Add(new BadgeInfo() { BadgeId = 6 });
                            }

                            if (player.GetDamageDealtPerTurn() >= 20 && player.GetSupportPerTurn() >= 20 && player.GetTankingPerLife() >= 200) playerBadgeInfos.Add(new BadgeInfo() { BadgeId = 9 });
                            else if (player.GetDamageDealtPerTurn() >= 15 && player.GetSupportPerTurn() >= 15 && player.GetTankingPerLife() >= 150) playerBadgeInfos.Add(new BadgeInfo() { BadgeId = 8 });
                            else if (player.GetDamageDealtPerTurn() >= 10 && player.GetSupportPerTurn() >= 10 && player.GetTankingPerLife() >= 100) playerBadgeInfos.Add(new BadgeInfo() { BadgeId = 7 });

                            int playerIndexFreelancerStats = sortedPlayersFreelancerStats.FindIndex(p => p.PlayerId == player.PlayerId);

                            if (playerIndexFreelancerStats >= 0)
                            {
                                int totalPlayers = sortedPlayersFreelancerStats.Count;
                                double percentile = (totalPlayers - playerIndexFreelancerStats - 1) * 100.0 / totalPlayers;

                                if (percentile > 50) playerBadgeInfos.Add(new BadgeInfo() { BadgeId = 10 });
                                if (percentile > 75) playerBadgeInfos.Add(new BadgeInfo() { BadgeId = 11 });
                                if (percentile > 80) playerBadgeInfos.Add(new BadgeInfo() { BadgeId = 12 });
                            }

                            if (highestDamagePerTurn != null && highestDamagePerTurn.PlayerId == player.PlayerId) playerBadgeInfos.Add(new BadgeInfo() { BadgeId = 13 });

                            int playerIndexDamageDealtPerTurn = sortedPlayersDamageDealtPerTurn.FindIndex(p => p.PlayerId == player.PlayerId);

                            if (playerIndexDamageDealtPerTurn >= 0 && highestDamagePerTurn != null && highestDamagePerTurn.PlayerId == player.PlayerId)
                            {
                                int totalPlayers = sortedPlayersDamageDealtPerTurn.Count;
                                double percentile = (totalPlayers - playerIndexDamageDealtPerTurn - 1) * 100.0 / totalPlayers;

                                if (percentile > 50) playerBadgeInfos.Add(new BadgeInfo() { BadgeId = 14 });
                                if (percentile > 75) playerBadgeInfos.Add(new BadgeInfo() { BadgeId = 15 });
                                if (percentile > 80) playerBadgeInfos.Add(new BadgeInfo() { BadgeId = 16 });
                            }

                            int playerIndexDamageEfficiency = sortedPlayersDamageEfficiency.FindIndex(p => p.PlayerId == player.PlayerId);

                            if (playerIndexDamageEfficiency >= 0 && highestDamageEfficiency != null && highestDamageEfficiency.PlayerId == player.PlayerId)
                            {
                                int totalPlayers = sortedPlayersDamageEfficiency.Count;
                                double percentile = (totalPlayers - playerIndexDamageEfficiency - 1) * 100.0 / totalPlayers;

                                if (percentile > 50) playerBadgeInfos.Add(new BadgeInfo() { BadgeId = 17 });
                                if (percentile > 75) playerBadgeInfos.Add(new BadgeInfo() { BadgeId = 18 });
                                if (percentile > 80) playerBadgeInfos.Add(new BadgeInfo() { BadgeId = 19 });
                            }

                            int playerIndexDamageDonePerLife = sortedPlayersDamageDonePerLife.FindIndex(p => p.PlayerId == player.PlayerId);

                            if (playerIndexDamageDonePerLife >= 0 && highestDamageDonePerLife != null && highestDamageDonePerLife.PlayerId == player.PlayerId)
                            {
                                int totalPlayers = sortedPlayersDamageDonePerLife.Count;
                                double percentile = (totalPlayers - playerIndexDamageDonePerLife - 1) * 100.0 / totalPlayers;

                                if (percentile > 50) playerBadgeInfos.Add(new BadgeInfo() { BadgeId = 21 });
                                if (percentile > 75) playerBadgeInfos.Add(new BadgeInfo() { BadgeId = 22 });
                                if (percentile > 80) playerBadgeInfos.Add(new BadgeInfo() { BadgeId = 23 });
                            }

                            int playerIndexDamageTakenPerLife = sortedPlayersDamageTakenPerLife.FindIndex(p => p.PlayerId == player.PlayerId);

                            if (playerIndexDamageTakenPerLife >= 0 && highestDamageTakenPerLife != null && highestDamageTakenPerLife.PlayerId == player.PlayerId)
                            {
                                if (highestDamageTakenPerLife != null && highestDamageTakenPerLife.PlayerId == player.PlayerId) playerBadgeInfos.Add(new BadgeInfo() { BadgeId = 20 });
                                int totalPlayers = sortedPlayersDamageTakenPerLife.Count;
                                double percentile = (totalPlayers - playerIndexDamageTakenPerLife - 1) * 100.0 / totalPlayers;

                                if (percentile > 50) playerBadgeInfos.Add(new BadgeInfo() { BadgeId = 24 });
                                if (percentile > 75) playerBadgeInfos.Add(new BadgeInfo() { BadgeId = 25 });
                                if (percentile > 80) playerBadgeInfos.Add(new BadgeInfo() { BadgeId = 26 });
                            }


                            int playerIndexDodge = sortedPlayersDodge.FindIndex(p => p.PlayerId == player.PlayerId);

                            if (playerIndexDodge >= 0 && highestDodge != null && highestDodge.PlayerId == player.PlayerId)
                            {
                                int totalPlayers = sortedPlayersDodge.Count;
                                double percentile = (totalPlayers - playerIndexDodge - 1) * 100.0 / totalPlayers;

                                if (percentile > 50) playerBadgeInfos.Add(new BadgeInfo() { BadgeId = 27 });
                                if (percentile > 75) playerBadgeInfos.Add(new BadgeInfo() { BadgeId = 28 });
                                if (percentile > 80) playerBadgeInfos.Add(new BadgeInfo() { BadgeId = 29 });
                            }

                            int playerIndexCrowdControl = sortedPlayersCrowdControl.FindIndex(p => p.PlayerId == player.PlayerId);

                            if (playerIndexCrowdControl >= 0 && highestCrowdControl != null && highestCrowdControl.PlayerId == player.PlayerId)
                            {
                                int totalPlayers = sortedPlayersCrowdControl.Count;
                                double percentile = (totalPlayers - playerIndexCrowdControl - 1) * 100.0 / totalPlayers;

                                if (percentile > 50) playerBadgeInfos.Add(new BadgeInfo() { BadgeId = 30 });
                                if (percentile > 75) playerBadgeInfos.Add(new BadgeInfo() { BadgeId = 31 });
                                if (percentile > 80) playerBadgeInfos.Add(new BadgeInfo() { BadgeId = 32 });
                            }

                            if (highestMitigated != null && highestMitigated.PlayerId == player.PlayerId) playerBadgeInfos.Add(new BadgeInfo() { BadgeId = 33 });

                            int playerIndexHealedShielded = sortedPlayersHealedShielded.FindIndex(p => p.PlayerId == player.PlayerId);

                            if (playerIndexHealedShielded >= 0 && highestHealingPlayer != null && highestHealingPlayer.PlayerId == player.PlayerId)
                            {
                                int totalPlayers = sortedPlayersHealedShielded.Count;
                                double percentile = (totalPlayers - playerIndexHealedShielded - 1) * 100.0 / totalPlayers;

                                if (percentile > 50) playerBadgeInfos.Add(new BadgeInfo() { BadgeId = 34 });
                                if (percentile > 75) playerBadgeInfos.Add(new BadgeInfo() { BadgeId = 35 });
                                if (percentile > 80) playerBadgeInfos.Add(new BadgeInfo() { BadgeId = 36 });
                            }

                            int playerIndexBoostTeamDamage = sortedPlayersBoostTeamDamage.FindIndex(p => p.PlayerId == player.PlayerId);

                            if (playerIndexBoostTeamDamage >= 0 && highestBoostTeamDamage != null && highestBoostTeamDamage.PlayerId == player.PlayerId)
                            {
                                int totalPlayers = sortedPlayersBoostTeamDamage.Count;
                                double percentile = (totalPlayers - playerIndexBoostTeamDamage - 1) * 100.0 / totalPlayers;

                                if (percentile > 50) playerBadgeInfos.Add(new BadgeInfo() { BadgeId = 37 });
                                if (percentile > 75) playerBadgeInfos.Add(new BadgeInfo() { BadgeId = 38 });
                                if (percentile > 80) playerBadgeInfos.Add(new BadgeInfo() { BadgeId = 39 });
                            }

                            int playerIndexBoostTeamEnergize = sortedPlayersBoostTeamEnergize.FindIndex(p => p.PlayerId == player.PlayerId);

                            if (playerIndexBoostTeamEnergize >= 0 && highestBoostTeamEnergize != null && highestBoostTeamEnergize.PlayerId == player.PlayerId)
                            {
                                int totalPlayers = sortedPlayersBoostTeamEnergize.Count;
                                double percentile = (totalPlayers - playerIndexBoostTeamEnergize - 1) * 100.0 / totalPlayers;

                                if (percentile > 50) playerBadgeInfos.Add(new BadgeInfo() { BadgeId = 40 });
                                if (percentile > 75) playerBadgeInfos.Add(new BadgeInfo() { BadgeId = 41 });
                                if (percentile > 80) playerBadgeInfos.Add(new BadgeInfo() { BadgeId = 42 });
                            }

                            badgeInfos[player.PlayerId] = playerBadgeInfos;
                        }

                        foreach (PlayerGameSummary player in request.GameSummary.PlayerGameSummaryList)
                        {
                            List<TopParticipantSlot> topParticipationEarned = new List<TopParticipantSlot>();

                            if (highestHealingPlayer != null && highestHealingPlayer.PlayerId == player.PlayerId)
                            {
                                topParticipationEarned.Add(TopParticipantSlot.Supportiest);
                            }
                            if (highestDamagePlayer != null && highestDamagePlayer.PlayerId == player.PlayerId)
                            {
                                topParticipationEarned.Add(TopParticipantSlot.Deadliest);
                            }
                            if (highestDamageRecievedPlayer != null && highestDamageRecievedPlayer.PlayerId == player.PlayerId)
                            {
                                topParticipationEarned.Add(TopParticipantSlot.Tankiest);
                            }

                            var playerBadgeCounts = badgeInfos
                                .GroupBy(b => b.Key)
                                .Select(g => new { PlayerId = g.Key, BadgeCount = g.Count() })
                                .OrderByDescending(x => x.BadgeCount);
                            int maxBadgeCount = playerBadgeCounts.FirstOrDefault()?.BadgeCount ?? 0;
                            int playerIdWithMostBadges = playerBadgeCounts
                                .Where(x => x.BadgeCount == maxBadgeCount)
                                .Select(x => x.PlayerId)
                                .FirstOrDefault();

                            if (playerIdWithMostBadges == player.PlayerId)
                            {
                                topParticipationEarned.Add(TopParticipantSlot.MostDecorated);
                            }


                            Team team = Team.TeamB;
                            if (player.IsInTeamA() && request.GameSummary.GameResult == GameResult.TeamAWon) team = Team.TeamA;
                            else if (player.IsInTeamB() && request.GameSummary.GameResult == GameResult.TeamBWon) team = Team.TeamA;

                            request.GameSummary.BadgeAndParticipantsInfo.Add(new BadgeAndParticipantInfo()
                            {
                                PlayerId = player.PlayerId,
                                TeamId = team,
                                TeamSlot = player.TeamSlot,
                                BadgesEarned = badgeInfos[player.PlayerId],
                                TopParticipationEarned = topParticipationEarned,
                                GlobalPercentiles = new Dictionary<StatDisplaySettings.StatType, PercentileInfo>(),
                                FreelancerSpecificPercentiles = new Dictionary<int, PercentileInfo>(),
                                FreelancerPlayed = player.CharacterPlayed
                            });
                        }
                    }

                    //Wait 5 seconds for gg Usages
                    await Task.Delay(5000);

                    foreach (LobbyServerProtocolBase client in clients)
                    {
                        MatchResultsNotification response = new MatchResultsNotification
                        {
                            BadgeAndParticipantsInfo = request.GameSummary.BadgeAndParticipantsInfo,
                            //Todo xp and stuff
                            BaseXpGained = 0,
                            CurrencyRewards = new List<MatchResultsNotification.CurrencyReward>()
                        };
                        client.Send(response);
                    }


                    UpdateGameInfoToPlayers();

                    if (LobbyConfiguration.GetChannelWebhook().MaybeUri())
                    {
                        try
                        {
                            if (GameInfo.GameResult != GameResult.TieGame)
                            {
                                DiscordWebhookClient discord = new DiscordWebhookClient(LobbyConfiguration.GetChannelWebhook());
                                string map = Maps.GetMapName[GameInfo.GameConfig.Map];
                                EmbedBuilder eb = new EmbedBuilder()
                                {
                                    Title = $"Game Result for {(map ?? GameInfo.GameConfig.Map)}",
                                    Description = $"{(request.GameSummary.GameResult.ToString() == "TeamAWon" ? "Team A Won" : "Team B Won")} {request.GameSummary.TeamAPoints}-{request.GameSummary.TeamBPoints} ({request.GameSummary.NumOfTurns} turns)",
                                    Color = request.GameSummary.GameResult.ToString() == "TeamAWon" ? Color.Green : Color.Red
                                };

                                eb.AddField("Team A", "\\_\\_\\_\\_\\_\\_\\_\\_\\_\\_\\_\\_\\_\\_\\_\\_\\_\\_\\_\\_\\_\\_\\_\\_\\_\\_\\_\\_\\_\\_", true);
                                eb.AddField("│", "│", true);
                                eb.AddField("Team B", "\\_\\_\\_\\_\\_\\_\\_\\_\\_\\_\\_\\_\\_\\_\\_\\_\\_\\_\\_\\_\\_\\_\\_\\_\\_\\_\\_\\_\\_\\_", true);

                                eb.AddField("**[ Takedowns : Deaths : Deathblows ] [ Damage : Healing : Damage Received ]**", "\\_\\_\\_\\_\\_\\_\\_\\_\\_\\_\\_\\_\\_\\_\\_\\_\\_\\_\\_\\_\\_\\_\\_\\_\\_\\_\\_\\_\\_\\_\\_\\_\\_\\_\\_\\_\\_\\_\\_\\_\\_\\_\\_\\_\\_\\_\\_\\_\\_\\_\\_\\_\\_\\_\\_\\_\\_\\_\\_\\_\\_\\_\\_\\_\\_\\_\\_", false);


                                List<PlayerGameSummary> teamA = new List<PlayerGameSummary>();
                                List<PlayerGameSummary> teamB = new List<PlayerGameSummary>();
                                int players = 0;

                                // Sort into seperate teams ignore spectators if ever
                                while (players < request.GameSummary.PlayerGameSummaryList.Count())
                                {
                                    PlayerGameSummary player = request.GameSummary.PlayerGameSummaryList[players];
                                    if (player.IsSpectator()) return;
                                    if (player.IsInTeamA()) teamA.Add(player);
                                    else teamB.Add(player);
                                    players++;
                                }

                                int teams = 0;
                                int highestCount = (teamA.Count() > teamB.Count() ? teamA.Count() : teamB.Count());
                                while (teams < highestCount)
                                {
                                    // try catch cause index can be out of bound if it happens (oneven teams) add a default field need to keep order of operation or fields are a jumbeld mess
                                    try
                                    {
                                        PlayerGameSummary playerA = teamA[teams];
                                        LobbyServerPlayerInfo playerInfoA = SessionManager.GetPlayerInfo(playerA.AccountId);
                                        eb.AddField($"{playerInfoA.Handle} ({playerA.CharacterName})", $"**[ {playerA.NumAssists} : {playerA.NumDeaths} : {playerA.NumKills} ] [ {playerA.TotalPlayerDamage} : {playerA.GetTotalHealingFromAbility() + playerA.TotalPlayerAbsorb} : {playerA.TotalPlayerDamageReceived} ]**", true);
                                    }
                                    catch
                                    {
                                        eb.AddField("-", "-", true);
                                    }

                                    eb.AddField("│", "│", true);

                                    try
                                    {
                                        PlayerGameSummary playerB = teamB[teams];
                                        LobbyServerPlayerInfo playerInfoB = SessionManager.GetPlayerInfo(playerB.AccountId);
                                        eb.AddField($"{playerInfoB.Handle} ({playerB.CharacterName})", $"**[ {playerB.NumAssists} : {playerB.NumDeaths} : {playerB.NumKills} ] [ {playerB.TotalPlayerDamage} : {playerB.GetTotalHealingFromAbility() + playerB.TotalPlayerAbsorb} : {playerB.TotalPlayerDamageReceived} ]**", true);
                                    }
                                    catch
                                    {
                                        eb.AddField("-", "-", true);
                                    }
                                    teams++;
                                }

                                EmbedFooterBuilder footer = new EmbedFooterBuilder
                                {
                                    Text = $"{Name} - {BuildVersion} - {new DateTime(GameInfo.CreateTimestamp):yyyy_MM_dd__HH_mm_ss}"
                                };
                                eb.Footer = footer;

                                Embed[] embedArray = new Embed[] { eb.Build() };
                                await discord.SendMessageAsync(null, false, embeds: embedArray, "Atlas Reactor", threadId: LobbyConfiguration.GetChannelThreadId());
                            }
                        }
                        catch (Exception exeption)
                        {
                            log.Info($"Failed to send report to discord webhook {exeption.Message}");
                        }
                    }
                } catch(NullReferenceException ex)
                {
                    log.Error(ex);
                }

                //Wait a bit so people can look at stuff but we do have to send it so server can restart
                await Task.Delay(60000);
                Send(new ShutdownGameRequest());
            }
            else if (type == typeof(PlayerDisconnectedNotification))
            {
                PlayerDisconnectedNotification request = Deserialize<PlayerDisconnectedNotification>(networkReader);
                log.Debug($"< {request.GetType().Name} {DefaultJsonSerializer.Serialize(request)}");
                log.Info($"Player {request.PlayerInfo.AccountId} left game {GameInfo?.GameServerProcessCode}");
                
                foreach (LobbyServerProtocol client in clients)
                {
                    if (client.AccountId == request.PlayerInfo.AccountId)
                    {
                        client.CurrentServer = null;
                        break;
                    }
                }
            }
            else if (type == typeof(ServerGameMetricsNotification))
            {
                ServerGameMetricsNotification request = Deserialize<ServerGameMetricsNotification>(networkReader);
                log.Debug($"< {request.GetType().Name} {DefaultJsonSerializer.Serialize(request)}");
                log.Info($"Game {GameInfo?.Name} Turn {request.GameMetrics?.CurrentTurn}, " +
                         $"{request.GameMetrics?.TeamAPoints}-{request.GameMetrics?.TeamBPoints}, " +
                         $"frame time: {request.GameMetrics?.AverageFrameTime}");
            }
            else if (type == typeof(ServerGameStatusNotification))
            {
                ServerGameStatusNotification request = Deserialize<ServerGameStatusNotification>(networkReader);
                log.Debug($"< {request.GetType().Name} {DefaultJsonSerializer.Serialize(request)}");
                log.Info($"Game {GameInfo?.Name} {request.GameStatus}");

                UpdateGameStatus(request.GameStatus, true);

                if (GameStatus == GameStatus.Stopped)
                {
                    foreach (LobbyServerProtocol client in clients)
                    {
                        client.CurrentServer = null;
                    }
                }
            }
            else if (type == typeof(MonitorHeartbeatNotification))
            {
                MonitorHeartbeatNotification request = Deserialize<MonitorHeartbeatNotification>(networkReader);
                log.Debug($"< {request.GetType().Name} heartbeat");
            }
            else if (type == typeof(LaunchGameResponse))
            {
                LaunchGameResponse response = Deserialize<LaunchGameResponse>(networkReader);
                log.Debug($"< {response.GetType().Name} {DefaultJsonSerializer.Serialize(response)}");
                log.Info($"Game {GameInfo?.Name} launched ({response.GameServerAddress}, {response.GameInfo?.GameStatus}) with {response.GameInfo?.ActiveHumanPlayers} players");
            }
            else if (type == typeof(JoinGameServerResponse))
            {
                JoinGameServerResponse response = Deserialize<JoinGameServerResponse>(networkReader);
                log.Debug($"< {response.GetType().Name} {DefaultJsonSerializer.Serialize(response)}");
                log.Info($"Player {response.PlayerInfo?.Handle} {response.PlayerInfo?.AccountId} {response.PlayerInfo?.CharacterType} " +
                         $"joined {GameInfo?.Name}  ({response.GameServerProcessCode})");
            }
            else
            {
                log.Warn($"Received unhandled bridge message type {(type != null ? type.Name : "id_" + messageType)}");
            }
        }

        private T Deserialize<T>(NetworkReader reader) where T : AllianceMessageBase
        {
            ConstructorInfo constructor = typeof(T).GetConstructor(Type.EmptyTypes);
            T o = (T)(AllianceMessageBase)constructor.Invoke(Array.Empty<object>());
            o.Deserialize(reader);
            return o;
        }

        protected override void HandleClose(CloseEventArgs e)
        {
            ServerManager.RemoveServer(ProcessCode);
            IsConnected = false;
        }

        public void UpdateGameStatus(GameStatus status, bool notify = false)
        {
            // Update GameInfo's GameStatus
            GameStatus = status;
            GameInfo.GameStatus = status;

            // If status is not None, notify players of the change
            if (status == GameStatus.None || !notify) return;
            GameStatusNotification notification = new GameStatusNotification() { GameStatus = status };

            foreach (long player in GetPlayers())
            {
                LobbyServerProtocol playerConnection = SessionManager.GetClientConnection(player);
                if (playerConnection != null)
                {
                    playerConnection.Send(notification);
                }
            }
        }

        public void UpdateGameInfoToPlayers()
        {
            foreach (long player in GetPlayers())
            {
                GameInfoNotification notification = new GameInfoNotification()
                {
                    GameInfo = GameInfo,
                    TeamInfo = LobbyTeamInfo.FromServer(TeamInfo, 0, new MatchmakingQueueConfig()),
                    PlayerInfo = LobbyPlayerInfo.FromServer(SessionManager.GetPlayerInfo(player), 0, new MatchmakingQueueConfig())
                };
                LobbyServerProtocol playerConnection = SessionManager.GetClientConnection(player);
                if (playerConnection != null)
                {
                    playerConnection.Send(notification);
                }
            }
        }

        public void OnPlayerUsedGGPack(long accountId)
        {
            int ggPackUsedAccountIDs = 0;
            GameInfo.ggPackUsedAccountIDs.TryGetValue(accountId, out ggPackUsedAccountIDs);
            GameInfo.ggPackUsedAccountIDs[accountId] = ggPackUsedAccountIDs + 1;

            UpdateGameInfoToPlayers();
        }

        public bool IsAvailable()
        {
            return GameStatus == GameStatus.Stopped && !IsPrivate && IsConnected;
        }

        public void ReserveForGame()
        {
            GameStatus = GameStatus.Assembling;
            // TODO release if game did not start?
        }

        public void StartGame(LobbyGameInfo gameInfo, LobbyServerTeamInfo teamInfo)
        {
            GameInfo = gameInfo;
            TeamInfo = teamInfo;
            GameStatus = GameStatus.Assembling;
            Dictionary<int, LobbySessionInfo> sessionInfos = teamInfo.TeamPlayerInfo
                .ToDictionary(
                    playerInfo => playerInfo.PlayerId,
                    playerInfo => SessionManager.GetSessionInfo(playerInfo.AccountId) ?? new LobbySessionInfo());  // fallback for bots TODO something smarter

            foreach (LobbyServerPlayerInfo playerInfo in teamInfo.TeamPlayerInfo)
            {
                LobbySessionInfo sessionInfo = sessionInfos[playerInfo.PlayerId];
                JoinGameServerRequest request = new JoinGameServerRequest
                {
                    OrigRequestId = 0,
                    GameServerProcessCode = GameInfo.GameServerProcessCode,
                    PlayerInfo = playerInfo,
                    SessionInfo = sessionInfo
                };
                Send(request);
            }
            
            Send(new LaunchGameRequest()
            {
                GameInfo = gameInfo,
                TeamInfo = teamInfo,
                SessionInfo = sessionInfos,
                GameplayOverrides = new LobbyGameplayOverrides()
            });
        }

        public bool Send(AllianceMessageBase msg, int originalCallbackId = 0)
        {
            short messageType = GetMessageType(msg);
            if (messageType >= 0)
            {
                Send(messageType, msg, originalCallbackId);
                log.Debug($"> {msg.GetType().Name} {DefaultJsonSerializer.Serialize(msg)}");
                return true;
            }
            log.Error($"No sender for {msg.GetType().Name}");
            log.Debug($">X {msg.GetType().Name} {DefaultJsonSerializer.Serialize(msg)}");

            return false;
        }

        private bool Send(short msgType, AllianceMessageBase msg, int originalCallbackId = 0)
        {
            NetworkWriter networkWriter = new NetworkWriter();
            networkWriter.Write(msgType);
            networkWriter.Write(originalCallbackId);
            msg.Serialize(networkWriter);
            Send(networkWriter.ToArray());
            return true;
        }

        public short GetMessageType(AllianceMessageBase msg)
        {
            short num = (short)GetMessageTypes().IndexOf(msg.GetType());
            if (num < 0)
            {
                log.Error($"Message type {msg.GetType().Name} is not in the MonitorGameServerInsightMessages MessageTypes list and doesnt have a type");
            }

            return num;
        }
    }
}