import React, {useEffect, useState} from 'react';
import {GameData, getStatus, GroupData, PlayerData, Status} from "../lib/Evos";
import {LinearProgress} from "@mui/material";
import Queue from "./Queue";
import {useAuthHeader} from "react-auth-kit";
import Server from "./Server";
import {useNavigate} from "react-router-dom";
import {EvosError, processError} from "../lib/Error";
import ErrorDialog from "./ErrorDialog";

function StatusPage() {
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState<EvosError>();
    const [status, setStatus] = useState<Status>();
    const [players, setPlayers] = useState<Map<number, PlayerData>>();
    const [groups, setGroups] = useState<Map<number, GroupData>>();
    const [games, setGames] = useState<Map<string, GameData>>();

    const authHeader = useAuthHeader()();
    const navigate = useNavigate();

    useEffect(() => {
        console.log("loading data");
        getStatus(authHeader)
            .then((resp) => {
                setStatus(resp.data);
                setLoading(false);
            })
            .catch((error) => processError(error, setError, navigate))
    }, [authHeader, navigate])

    useEffect(() => {
        if (!status) {
            return;
        }
        const _players = status.players.reduce((res, p) => {
            res.set(p.accountId, p);
            return res;
        }, new Map<number, PlayerData>());
        setPlayers(_players);
        const _groups = status.groups.reduce((res, g) => {
            res.set(g.groupId, g);
            return res;
        }, new Map<number, GroupData>());
        setGroups(_groups);
        const _games = status.games.reduce((res, g) => {
            res.set(g.server, g);
            return res;
        }, new Map<string, GameData>());
        setGames(_games);
    }, [status])

    const queuedGroups = new Set(status?.queues?.flatMap(q => q.groupIds));
    const notQueuedGroups = groups && [...groups.keys()].filter(g => !queuedGroups.has(g));
    const inGame = games && new Set([...games.values()]
        .flatMap(g => [...g.teamA, ...g.teamB])
        .map(t => t.accountId));

    return (
        <div className="App">
            <header className="App-header">
                {loading && <LinearProgress />}
                {error && <ErrorDialog error={error} onDismiss={() => setError(undefined)} />}
                {status && players && games
                    && status.servers
                        .sort((s1, s2) => s1.name.localeCompare(s2.name))
                        .map(s => <Server key={s.id} info={s} game={games.get(s.id)} playerData={players}/>)}
                {status && groups && players
                    && status.queues.map(q => <Queue key={q.type} info={q} groupData={groups} playerData={players} />)}
                {notQueuedGroups && groups && players && inGame
                    && <Queue
                        key={'not_queued'}
                        info={{type: "Not queued", groupIds: notQueuedGroups}}
                        groupData={groups}
                        playerData={players}
                        hidePlayers={inGame}
                    />}
            </header>
        </div>
    );
}

export default StatusPage;
