using EvoS.Framework.Network.Static;
using EvoS.Framework.Network.WebSocket;
using System;

namespace EvoS.Framework.Network.NetworkMessages
{
    [Serializable]
    [EvosMessage(203)]
    public class RankedTradeRequest : WebSocketMessage
    {
        public RankedTradeData Trade;
    }
}
