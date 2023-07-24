using System.Collections.Generic;
using System.Linq;
using CentralServer.LobbyServer.Matchmaking;
using EvoS.Framework.Constants.Enums;
using log4net;

namespace CentralServer.BridgeServer
{
    public static class ServerManager
    {
        private static readonly ILog log = LogManager.GetLogger(typeof(ServerManager));

        private static readonly Dictionary<string, BridgeServerProtocol> ServerPool = new Dictionary<string, BridgeServerProtocol>();

        public static void AddServer(BridgeServerProtocol gameServer)
        {
            lock (ServerPool)
            {
                bool newServer = ServerPool.TryAdd(gameServer.ProcessCode, gameServer);

                log.Info($"{(newServer ? "New game server connected" : "A server reconnected")} with address {gameServer.Address}:{gameServer.Port}");
                MatchmakingManager.Update();
            }
        }

        public static void RemoveServer(string processCode)
        {
            if (processCode == null)
            {
                return;
            }
            lock (ServerPool)
            {
                ServerPool.Remove(processCode);
                log.Info($"Game server disconnected");
            }
        }

        public static BridgeServerProtocol GetServer()
        {
            lock (ServerPool)
            {
                foreach (BridgeServerProtocol server in ServerPool.Values)
                {
                    if (server.IsAvailable())
                    {
                        server.ReserveForGame();
                        return server;
                    }
                }
            }

            return null;
        }

        public static BridgeServerProtocol GetServerWithPlayer(long accountId)
        {
            lock (ServerPool)
            {
                foreach (BridgeServerProtocol server in ServerPool.Values)
                {
                    if (server.ServerGameStatus is >= GameStatus.Launched and < GameStatus.Stopped)
                    {
                        foreach (long player in server.GetPlayers())
                        {
                            if (player.Equals(accountId))
                            {
                                return server;
                            }
                        }
                    }
                }

                return null;
            }
        }

        public static bool IsAnyServerAvailable()
        {
            lock (ServerPool)
            {
                foreach (BridgeServerProtocol server in ServerPool.Values)
                {
                    if (server.IsAvailable()) return true;
                }

                return false;
            }
        }

        public static List<BridgeServerProtocol> ListCustomServers()
        {
            lock (ServerPool)
            {
                List<BridgeServerProtocol> bridgeServers = new List<BridgeServerProtocol>();
                foreach (BridgeServerProtocol server in ServerPool.Values)
                {
                    if (server.IsPrivate) { 
                        bridgeServers.Add(server);
                    }
                }

                return bridgeServers;
            }
        }

        public static BridgeServerProtocol GetServerByProcessCode(string processCode)
        {
            lock (ServerPool)
            {
                foreach (BridgeServerProtocol server in ServerPool.Values)
                {
                    if (server.ProcessCode == processCode)
                    {
                        return server;
                    }
                }

                return null;
            }
        }

        public static List<BridgeServerProtocol> GetServers()
        {
            return ServerPool.Values.ToList();
        }
    }
}
