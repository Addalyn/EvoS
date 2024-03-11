using System;
using System.Collections.Generic;
using System.Linq;
using CentralServer.LobbyServer.Character;
using EvoS.Framework.DataAccess;
using EvoS.Framework.DataAccess.Daos;
using EvoS.Framework.Network.Static;
using log4net;

namespace CentralServer.LobbyServer.Matchmaking;

public class MatchmakerRanked : Matchmaker
{
    private static readonly ILog log = LogManager.GetLogger(typeof(Matchmaker));
    
    private readonly AccountDao _accountDao;
    private readonly string _eloKey;

    public MatchmakerRanked(
        AccountDao accountDao,
        GameType gameType,
        GameSubType subType,
        string eloKey,
        Func<MatchmakingConfiguration> conf)
        : base(gameType, subType, conf)
    {
        _accountDao = accountDao;
        _eloKey = eloKey;
    }
    
    public MatchmakerRanked(
        GameType gameType,
        GameSubType subType,
        string eloKey,
        Func<MatchmakingConfiguration> conf)
        :this(DB.Get().AccountDao, gameType, subType, eloKey, conf)
    {
    }
    
    protected override IEnumerable<Match> FindMatches(List<MatchmakingGroup> queuedGroups)
    {
        return FindMatches(new MatchScratch(_subType), queuedGroups, new HashSet<long>(), _eloKey);
    }
    
    private IEnumerable<Match> FindMatches(
        MatchScratch matchScratch,
        List<MatchmakingGroup> queuedGroups,
        HashSet<long> processed,
        string eloKey)
    {
        foreach (MatchmakingGroup groupInfo in queuedGroups)
        {
            if (matchScratch.Push(groupInfo))
            {
                if (matchScratch.IsMatch())
                {
                    long hash = matchScratch.GetHash();
                    if (processed.Add(hash))
                    {
                        yield return matchScratch.ToMatch(_accountDao, eloKey);
                    }
                }
                else
                {
                    foreach (Match match in FindMatches(matchScratch, queuedGroups, processed, eloKey))
                    {
                        yield return match;
                    }
                }
                matchScratch.Pop();
            }
        }
    }

    protected override bool FilterMatch(Match match, DateTime now)
    {
        double waitingTime = GetReferenceTime(match, now);
        int maxEloDiff = Conf.MaxTeamEloDifferenceStart +
                         Convert.ToInt32((Conf.MaxTeamEloDifference - Conf.MaxTeamEloDifferenceStart)
                                         * Math.Clamp(waitingTime / Conf.MaxTeamEloDifferenceWaitTime.TotalSeconds, 0, 1));

        float eloDiff = Math.Abs(match.TeamA.Elo - match.TeamB.Elo);
        bool result = eloDiff <= maxEloDiff;
        log.Debug($"{(result ? "A": "Disa")}llowed {match}, elo diff {eloDiff}/{maxEloDiff}, reference queue time {TimeSpan.FromSeconds(waitingTime)}");
        return result;
    }

    private static double GetReferenceTime(Match match, DateTime now)
    {
        int cutoff = int.Max(1, Convert.ToInt32(MathF.Floor(match.Groups.Count() / 2.0f))); // don't want to keep the first ones to queue waiting for too long
        double waitingTime = match.Groups
            .Select(g => (now - g.QueueTime).TotalSeconds)
            .Order()
            .TakeLast(cutoff)
            .Average();
        return waitingTime;
    }
    
    protected override float RankMatch(Match match, DateTime now, bool infoLog = false)
    {
        float teamEloDifferenceFactor = 1 - Cap(Math.Abs(match.TeamA.Elo - match.TeamB.Elo) / Conf.MaxTeamEloDifference);
        float teammateEloDifferenceAFactor = 1 - Cap((match.TeamA.MaxElo - match.TeamA.MinElo) / Conf.TeammateEloDifferenceWeightCap);
        float teammateEloDifferenceBFactor = 1 - Cap((match.TeamB.MaxElo - match.TeamB.MinElo) / Conf.TeammateEloDifferenceWeightCap);
        float teammateEloDifferenceFactor = (teammateEloDifferenceAFactor + teammateEloDifferenceBFactor) * 0.5f;
        double waitTime = Math.Sqrt(match.Groups.Select(g => Math.Pow((now - g.QueueTime).TotalSeconds, 2)).Average());
        float waitTimeFactor = Cap((float)(waitTime / Conf.WaitingTimeWeightCap.TotalSeconds));
        float teamCompositionFactor = (GetTeamCompositionFactor(match.TeamA) + GetTeamCompositionFactor(match.TeamB)) * 0.5f;
        float teamBlockFactor = (GetBlocksFactor(match.TeamA) + GetBlocksFactor(match.TeamB)) * 0.5f;
        float teamConfidenceBalanceFactor = GetTeamConfidenceBalanceFactor(match);
        
        // TODO balance max - min elo in the team
        // TODO recently canceled matches factor
        // TODO match-to-match variance factor
        // TODO accumulated wait time (time spent in queue for the last hour?)
        // TODO win history factor (too many losses - try not to put into a disadvantaged team)?
        // TODO non-linearity?
        
        // TODO if you are waiting for 20 minutes, you must be in the next game
        // TODO overrides line "we must have this player in the next match" & "we must start next match by specific time"

        float score =
            teamEloDifferenceFactor * Conf.TeamEloDifferenceWeight
            + teammateEloDifferenceFactor * Conf.TeammateEloDifferenceWeight
            + waitTimeFactor * Conf.WaitingTimeWeight
            + teamCompositionFactor * Conf.TeamCompositionWeight
            + teamBlockFactor * Conf.TeamBlockWeight
            + teamConfidenceBalanceFactor * Conf.TeamConfidenceBalanceWeight;
        
        string msg = $"Score {score:0.00} " +
                  $"(tElo:{teamEloDifferenceFactor:0.00}, " +
                  $"tmElo:{teammateEloDifferenceFactor:0.00}, " +
                  $"q:{waitTimeFactor:0.00}, " +
                  $"tComp:{teamCompositionFactor:0.00}, " +
                  $"blocks:{teamBlockFactor:0.00}, " +
                  $"tConf:{teamConfidenceBalanceFactor:0.00}" +
                  $") {match}";
        if (infoLog)
        {
            log.Info(msg);
        }
        else
        {
            log.Debug(msg);
        }

        return score;
    }

    private static float Cap(float factor)
    {
        return Math.Clamp(factor, 0, 1);
    }

    private static float GetTeamCompositionFactor(Match.Team team)
    {
        if (team.Groups.Count == 1)
        {
            return 1;
        }
        
        float score = 0;
        Dictionary<CharacterRole,int> roles = team.Accounts.Values
            .Select(acc => acc.AccountComponent.LastCharacter)
            .Select(ch => CharacterConfigs.Characters[ch].CharacterRole)
            .GroupBy(role => role)
            .ToDictionary(el => el.Key, el => el.Count());
        
        if (roles.ContainsKey(CharacterRole.Tank))
        {
            score += 0.3f;
        }
        if (roles.ContainsKey(CharacterRole.Support))
        {
            score += 0.3f;
        }
        if (roles.TryGetValue(CharacterRole.Assassin, out int flNum))
        {
            score += 0.2f * Math.Min(flNum, 2);
        }
        if (roles.TryGetValue(CharacterRole.None, out int fillNum))
        {
            score += 0.27f * Math.Min(fillNum, 4);
        }

        return Math.Min(score, 1);
    }

    private static float GetBlocksFactor(Match.Team team)
    {
        if (team.Groups.Count == 1)
        {
            return 1;
        }
        
        int totalBlocks = team.Accounts.Values
            .Select(acc => team.AccountIds.Count(accId => acc.SocialComponent.BlockedAccounts.Contains(accId)))
            .Sum();

        return 1 - Math.Min(totalBlocks * 0.125f, 1);
    }

    private float GetTeamConfidenceBalanceFactor(Match match)
    {
        int diff = Math.Abs(match.TeamA.Accounts.Values.Select(GetEloConfidenceLevel).Sum()
                            - match.TeamB.Accounts.Values.Select(GetEloConfidenceLevel).Sum());
        return 1 - Cap(diff * 0.33f);
    }

    private int GetEloConfidenceLevel(PersistedAccountData acc)
    {
        acc.ExperienceComponent.EloValues.GetElo(_eloKey, out _, out int eloConfLevel);
        return eloConfLevel;
    }
    
    class MatchScratch
    {
        class Team
        {
            private readonly int _capacity;
            
            private readonly List<MatchmakingGroup> _groups = new(5);
            private int _size;

            public Team(int capacity)
            {
                _capacity = capacity;
            }

            public Team(Team other) : this(other._capacity)
            {
                _groups = other._groups.ToList();
                _size = other._size;
            }

            public bool IsFull => _size == _capacity;
            public List<MatchmakingGroup> Groups => _groups;

            public int GetHash()
            {
                return _groups
                    .SelectMany(g => g.Members)
                    .Order()
                    .Select(accountId => accountId.GetHashCode())
                    .Aggregate(17, (a, b) => a * 31 + b);
            }

            public bool Push(MatchmakingGroup groupInfo)
            {
                if (_capacity <= _size || _capacity - _size < groupInfo.Players)
                {
                    return false;
                }
                _size += groupInfo.Players;
                _groups.Add(groupInfo);
                return true;
            }

            public bool Pop(out long groupId)
            {
                groupId = -1;
                if (_groups.Count <= 0)
                {
                    return false;
                }
                MatchmakingGroup groupInfo = _groups[^1];
                _size -= groupInfo.Players;
                _groups.RemoveAt(_groups.Count - 1);
                groupId = groupInfo.GroupID;
                return true;
            }

            public override string ToString()
            {
                return
                    $"groups {string.Join(",", _groups.Select(g => g.GroupID))} " +
                    $"<{string.Join(",", _groups.SelectMany(g => g.Members))}>";
            }
        }
        
        private readonly Team _teamA;
        private readonly Team _teamB;
        private readonly HashSet<long> _usedGroupIds = new(10);

        public MatchScratch(GameSubType subType)
        {
            _teamA = new Team(subType.TeamAPlayers);
            _teamB = new Team(subType.TeamBPlayers);
        }

        private MatchScratch(Team teamA, Team teamB, HashSet<long> usedGroupIds)
        {
            _teamA = teamA;
            _teamB = teamB;
            _usedGroupIds = usedGroupIds;
        }

        public Match ToMatch(AccountDao accountDao, string eloKey)
        {
            return new Match(accountDao, _teamA.Groups.ToList(), _teamB.Groups.ToList(), eloKey);
        }

        public long GetHash()
        {
            int a = _teamA.GetHash();
            int b = _teamB.GetHash();
            return (long)Math.Min(a, b) << 32 | (uint)Math.Max(a, b);
        }

        public bool Push(MatchmakingGroup groupInfo)
        {
            if (_usedGroupIds.Contains(groupInfo.GroupID))
            {
                return false;
            }
            if (_teamA.Push(groupInfo) || _teamB.Push(groupInfo))
            {
                _usedGroupIds.Add(groupInfo.GroupID);
                return true;
            }
            return false;
        }

        public void Pop()
        {
            if (_teamB.Pop(out long groupId) || _teamA.Pop(out groupId))
            {
                _usedGroupIds.Remove(groupId);
                return;
            }
            
            throw new Exception("Matchmaking failure");
        }

        public bool IsMatch()
        {
            return _teamA.IsFull && _teamB.IsFull;
        }

        public override string ToString()
        {
            return $"{_teamA} vs {_teamB}";
        }
    }
}