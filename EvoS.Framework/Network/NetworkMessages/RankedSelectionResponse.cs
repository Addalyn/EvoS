using EvoS.Framework.Network.WebSocket;
using System;

namespace EvoS.Framework.Network.NetworkMessages
{
    [Serializable]
    [EvosMessage(204)]
    public class RankedSelectionResponse : WebSocketResponseMessage
    {
        public LocalizationPayload LocalizedFailure;
    }
}
