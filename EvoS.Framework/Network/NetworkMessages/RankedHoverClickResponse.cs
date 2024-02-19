using EvoS.Framework.Network.WebSocket;
using System;

namespace EvoS.Framework.Network.NetworkMessages
{
    [Serializable]
    [EvosMessage(200)]
    public class RankedHoverClickResponse : WebSocketResponseMessage
    {
        public LocalizationPayload LocalizedFailure;
    }
}
