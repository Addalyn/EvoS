using EvoS.Framework.Network.WebSocket;
using System;

namespace EvoS.Framework.Network.NetworkMessages
{
    [Serializable]
    [EvosMessage(207)]
    public class RankedBanRequest : WebSocketMessage
    {
        public CharacterType Selection;
    }
}
