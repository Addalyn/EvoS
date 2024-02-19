using EvoS.Framework.Network.WebSocket;
using System;

namespace EvoS.Framework.Network.NetworkMessages
{
    [Serializable]
    [EvosMessage(206)]
    public class RankedBanResponse : WebSocketResponseMessage
    {
        public LocalizationPayload LocalizedFailure;
    }
}
