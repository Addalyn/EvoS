using EvoS.Framework.Network.WebSocket;
using System;

namespace EvoS.Framework.Network.NetworkMessages
{
    [Serializable]
    [EvosMessage(205)]
    public class RankedSelectionRequest : WebSocketMessage
    {
        public CharacterType Selection;
    }
}
