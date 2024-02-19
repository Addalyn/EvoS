using EvoS.Framework.Network.WebSocket;
using System;

namespace EvoS.Framework.Network.NetworkMessages
{
    [Serializable]
    [EvosMessage(202)]
    public class RankedTradeResponse : WebSocketResponseMessage
    {
        public LocalizationPayload LocalizedFailure;
    }
}
