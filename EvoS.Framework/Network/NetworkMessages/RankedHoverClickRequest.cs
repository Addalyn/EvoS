using EvoS.Framework.Network.WebSocket;
using System;

namespace EvoS.Framework.Network.NetworkMessages
{
    [Serializable]
    [EvosMessage(201)]
    public class RankedHoverClickRequest : WebSocketMessage
    {
        public CharacterType Selection;
    }
}
