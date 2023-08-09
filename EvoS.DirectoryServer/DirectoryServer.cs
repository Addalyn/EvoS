using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using CentralServer.LobbyServer.Session;
using EvoS.DirectoryServer.Account;
using EvoS.DirectoryServer.Inventory;
using EvoS.Framework;
using EvoS.Framework.Auth;
using EvoS.Framework.Constants.Enums;
using EvoS.Framework.DataAccess;
using EvoS.Framework.Misc;
using EvoS.Framework.Network.NetworkMessages;
using EvoS.Framework.Network.Static;
using log4net;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.IdentityModel.Tokens;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EvoS.DirectoryServer
{
    public class Program
    {
        public static void Main(string[] args = null)
        {
            var host = WebHost.CreateDefaultBuilder()
                .SuppressStatusMessages(true)
                .UseKestrel(koptions => koptions.Listen(IPAddress.Parse("0.0.0.0"), EvosConfiguration.GetDirectoryServerPort()))
                .UseStartup<DirectoryServer>()
                .Build();

            Console.CancelKeyPress += async (sender, @event) =>
            {
                await host.StopAsync();
                host.Dispose();
            };

            host.Run();
        }
    }

    public class DirectoryServer
    {
        private static readonly ILog log = LogManager.GetLogger(typeof(DirectoryServer));
        private static long _nextTmpAccountId = 1;
        
        public void Configure(IApplicationBuilder app)
        {
            var serverAddressesFeature = app.ServerFeatures.Get<IServerAddressesFeature>();
            log.Info($"Started DirectoryServer on '0.0.0.0:{EvosConfiguration.GetDirectoryServerPort()}'");

            app.Run(async (context) =>
            {
                var syncIOFeature = context.Features.Get<IHttpBodyControlFeature>();
                if (syncIOFeature != null)
                {
                    syncIOFeature.AllowSynchronousIO = true;
                }
                
                context.Response.ContentType = "application/json";
                MemoryStream ms = new MemoryStream();
                context.Request.Body.CopyTo(ms);
                ms.Position = 0;
                string requestBody = new StreamReader(ms).ReadToEnd();
                ms.Dispose();
                
                AssignGameClientRequest request = JsonConvert.DeserializeObject<AssignGameClientRequest>(requestBody);
                log.Debug($"< {request.GetType().Name} {DefaultJsonSerializer.Serialize(request)}");
                AssignGameClientResponse response = ProcessRequest(request, context);
                log.Debug($"> {response.GetType().Name} {DefaultJsonSerializer.Serialize(response)}");
                await context.Response.WriteAsync(JsonConvert.SerializeObject(response));
            });
        }

        private static AssignGameClientResponse ProcessRequest(AssignGameClientRequest request, HttpContext context)
        {
            string ticket = request.AuthInfo.GetTicket();

            if (!ticket.IsNullOrEmpty())
            {
                // If we received a valid TicketData, it means we were previously logged on (reconnection)
                SessionTicketData ticketData = SessionTicketData.FromString(ticket);
                if (ticketData != null)
                {
                    request.SessionInfo.AccountId = ticketData.AccountID;
                    request.SessionInfo.SessionToken = ticketData.SessionToken;
                    request.SessionInfo.ReconnectSessionToken = ticketData.ReconnectionSessionToken;

                    AssignGameClientResponse resp = HandleReconnection(request, context);
                    if (resp != null)
                    {
                        return resp;
                    }
                }
                
                if (EvosConfiguration.GetAllowTicketAuth())
                {
                    try
                    {
                        AuthTicket authTicket = AuthTicket.Parse(ticket);
                        return HandleConnectionWithToken(authTicket.AccountId, authTicket.Token, request, context);
                    }
                    catch (Exception)
                    {
                        log.Warn("Received ticket data but failed to parse it");
                    }
                }
            }

            string username = request.AuthInfo.UserName;
            string password = request.AuthInfo._Password;
            return HandleConnectionWithPassword(username, password, request, context);
        }

        private static AssignGameClientResponse HandleConnectionWithToken(long accountId, string token, AssignGameClientRequest request, HttpContext context)
        {
            if (token == null)
            {
                return Fail(request, "No credentials provided");
            }
            
            EvosAuth.TokenData tokenData;
            try
            {
                tokenData = EvosAuth.ValidateToken(EvosAuth.Context.TICKET_AUTH, token);
            }
            // specific token exceptions
            catch (SecurityTokenInvalidLifetimeException e)
            {
                log.Info("Obsolete ticket", e);
                return Fail(request, "Failed to log in");
            }
            // generic exceptions
            catch (SecurityTokenException e)
            {
                log.Info("Invalid ticket", e);
                return Fail(request, "Failed to log in");
            }
            catch (Exception e)
            {
                log.Error("Error while validating ticket", e);
                return Fail(request, "Failed to log in");
            }

            if (tokenData is null)
            {
                return Fail(request, AuthTicket.TICKET_CORRUPT);
            }

            if (accountId != tokenData.AccountId)
            {
                log.Info($"Account id mismatch on auth: {accountId} vs {tokenData.AccountId}");
                return Fail(request, "Failed to log in");
            }

            if (!tokenData.IpAddress.Equals(context.Connection.RemoteIpAddress) &&
                (context.Connection.RemoteIpAddress is null
                 || !IPAddress.IsLoopback(tokenData.IpAddress)
                 || !IPAddress.IsLoopback(context.Connection.RemoteIpAddress)))
            {
                log.Info($"qIP address mismatch on auth: {context.Connection.RemoteIpAddress} vs {tokenData.IpAddress}");
                return Fail(request, AuthTicket.INVALID_IP_ADDRESS);
            }

            return HandleConnection(tokenData.AccountId, request, context);
        }

        private static AssignGameClientResponse HandleConnectionWithPassword(string username, string password, AssignGameClientRequest request, HttpContext context)
        {
            if (username == null || password == null)
            {
                return Fail(request, "No credentials provided");
            }
            
            if (!EvosConfiguration.GetAllowUsernamePasswordAuth())
            {
                return Fail(request, "Username and password auth is not allowed");
            }

            long accountId;
            try
            {
                accountId = LoginManager.RegisterOrLogin(request.AuthInfo);
            }
            catch (Exception e) when (e is ArgumentException or ApplicationException or EvosException or ConflictException)
            {
                return Fail(request, e.Message);
            }

            return HandleConnection(accountId, request, context);
        }

        private static AssignGameClientResponse HandleConnection(long accountId, AssignGameClientRequest request, HttpContext context)
        {
            AssignGameClientResponse response = new AssignGameClientResponse
            {
                ResponseId = request.RequestId,
                Success = true,
                ErrorMessage = ""
            };

            if (SessionManager.GetSessionInfo(accountId) != null)
            {
                log.Info($"Concurrent login: {accountId}");
                return Fail(request, "This account is already logged in");
            }

            PersistedAccountData account;
            try
            {
                account = DB.Get().AccountDao.GetAccount(accountId);
                if (account == null)
                {
                    log.Error($"Player {accountId} does not exist");
                    string username = request.AuthInfo.UserName;
                    account = LoginManager.CreateAccount(accountId, username);
                }
                else
                {
                    log.Info($"Player {account.Handle}/{account.AccountId} logged in");
                }
            }
            catch (Exception e)
            {
                long tmpAccId = Interlocked.Increment(ref _nextTmpAccountId);
                account = AccountManager.CreateAccount(tmpAccId, "temp_user#" + tmpAccId);
                log.Error($"Temp user {account.Handle}/{account.AccountId}: {e}");
            }

            // Someday we'll make a db migration tool but not today
            if (PatchAccountData(account))
            {
                DB.Get().AccountDao.UpdateAccount(account);
            }

            response.SessionInfo = SessionManager.CreateSession(accountId, request.SessionInfo, context.Connection.RemoteIpAddress);
            response.LobbyServerAddress = EvosConfiguration.GetLobbyServerAddress();

            LobbyGameClientProxyInfo proxyInfo = new LobbyGameClientProxyInfo
            {
                AccountId = response.SessionInfo.AccountId,
                SessionToken = request.SessionInfo.SessionToken,
                AssignmentTime = DateTimeOffset.Now.ToUnixTimeSeconds(),
                Handle = request.SessionInfo.Handle,
                Status = ClientProxyStatus.Assigned
            };

            response.ProxyInfo = proxyInfo;
            return response;
        }

        private static AssignGameClientResponse HandleReconnection(AssignGameClientRequest request, HttpContext context)
        {
            LobbySessionInfo session = SessionManager.GetDisconnectedSessionInfo(request.SessionInfo.AccountId);

            if (session == null)
            {
                return null;
            }
            
            if (session.SessionToken != request.SessionInfo.SessionToken)
            {
                return Fail(request, "ReconnectionError: SessionToken invalid");
            }

            if (session.ReconnectSessionToken != request.SessionInfo.ReconnectSessionToken)
            {
                return Fail(request, "ReconnectionError: ReconnectSessionToken invalid");
            }
            
            // TODO extra security? Reconnect tokens are possible to hijack

            // SessionManager.CleanSessionAfterReconnect(request.SessionInfo.AccountId);
            session = SessionManager.CreateSession(request.SessionInfo.AccountId, request.SessionInfo, context.Connection.RemoteIpAddress);
            
            return new AssignGameClientResponse
            {
                Success = true,
                ResponseId = request.RequestId,
                LobbyServerAddress = EvosConfiguration.GetLobbyServerAddress(),
                SessionInfo = session,
                ProxyInfo = new LobbyGameClientProxyInfo
                {
                    AccountId = session.AccountId,
                    SessionToken = session.SessionToken,
                    AssignmentTime = DateTimeOffset.Now.ToUnixTimeSeconds(),
                    Handle = session.Handle,
                    Status = ClientProxyStatus.Assigned
                }
            };
        }

        private static bool PatchAccountData(PersistedAccountData account)
        {

#if DEBUG
            account.AccountComponent.AppliedEntitlements.TryAdd("DEVELOPER_ACCESS", 1);
#endif

            // Check if WillFill is missing in CharacterData, if it is add it
            account.CharacterData.TryGetValue(CharacterType.PendingWillFill, out PersistedCharacterData willFill);
            if (willFill == null)
            {
                account.CharacterData.TryAdd(CharacterType.PendingWillFill, new PersistedCharacterData(CharacterType.PendingWillFill));
                willFill = account.CharacterData[CharacterType.PendingWillFill];
            }

            // PATCH Make sure PendingWillFill has default CharacterLoadouts
            if (willFill.CharacterComponent.CharacterLoadouts.Count == 0) 
            {
                willFill.CharacterComponent.CharacterLoadouts = new List<CharacterLoadout>()
                {
                    new CharacterLoadout
                    (
                        new CharacterModInfo() { ModForAbility0 = 0, ModForAbility1 = 0, ModForAbility2 = 0, ModForAbility3 = 0, ModForAbility4 = 0 },
                        new CharacterAbilityVfxSwapInfo() { VfxSwapForAbility0 = 0, VfxSwapForAbility1 = 0, VfxSwapForAbility2 = 0, VfxSwapForAbility3 = 0, VfxSwapForAbility4 = 0 },
                        "Default",
                        ModStrictness.AllModes
                    )
                };
                willFill.CharacterComponent.LastSelectedLoadout = 0;
            }

            foreach (PersistedCharacterData persistedCharacterData in account.CharacterData.Values)
            {
                persistedCharacterData.CharacterComponent.UnlockSkinsAndTaunts(persistedCharacterData.CharacterType);
                if (EvosStoreConfiguration.AreAllCharactersForFree()) persistedCharacterData.ExperienceComponent.Level = EvosStoreConfiguration.GetStartingCharactersLevel();
                persistedCharacterData.CharacterComponent.UnlockVFX(persistedCharacterData.CharacterType);
            }

            if (EvosStoreConfiguration.AreEmojisFree()) account.AccountComponent.UnlockedEmojiIDs = InventoryManager.GetUnlockedEmojiIDs(account.AccountId);
            if (EvosStoreConfiguration.AreLoadingScreenBackgroundFree())
            {
                Dictionary<int, bool> activatedBgs = account.AccountComponent.UnlockedLoadingScreenBackgroundIdsToActivatedState;
                if (!CompilerExtensions.IsNullOrEmpty(activatedBgs) && activatedBgs.Values.Any(x => !x))
                {
                    // preserve which backgrounds are activated
                    account.AccountComponent.UnlockedLoadingScreenBackgroundIdsToActivatedState = InventoryManager.GetActivatedLoadingScreenBackgroundIds(account.AccountId, false);
                    foreach ((int bgId, bool activated) in activatedBgs)
                    {
                        account.AccountComponent.UnlockedLoadingScreenBackgroundIdsToActivatedState[bgId] = activated;
                    }
                }
                else
                {
                    account.AccountComponent.UnlockedLoadingScreenBackgroundIdsToActivatedState = InventoryManager.GetActivatedLoadingScreenBackgroundIds(account.AccountId, true);
                }
            }
            if (EvosStoreConfiguration.AreOverconsFree()) account.AccountComponent.UnlockedOverconIDs = InventoryManager.GetUnlockedOverconIDs(account.AccountId);
            if (EvosStoreConfiguration.AreTitlesFree()) account.AccountComponent.UnlockedTitleIDs = InventoryManager.GetUnlockedTitleIDs(account.AccountId);
            if (EvosStoreConfiguration.AreBannersFree()) account.AccountComponent.UnlockedBannerIDs = InventoryManager.GetUnlockedBannerIDs(account.AccountId);

            if (account.AccountComponent.AppliedEntitlements.ContainsKey("DEVELOPER_ACCESS"))
            {
                //Give developers access to the Developer title
                account.AccountComponent.UnlockedTitleIDs.Add(26);
            }

            if (account.SocialComponent.BlockedAccounts == null)
            {
                account.SocialComponent.BlockedAccounts = new HashSet<long>();
            }

            return true;
        }

        private static AssignGameClientResponse Fail(AssignGameClientRequest request, string reason)
        {
            return new AssignGameClientResponse
            {
                RequestId = 0,
                ResponseId = request.RequestId,
                Success = false,
                ErrorMessage = reason
            };
        }
    }
}
