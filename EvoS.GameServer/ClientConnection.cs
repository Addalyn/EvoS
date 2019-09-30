﻿using EvoS.Framework.Logging;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using EvoS.GameServer.Network.Unity;
using WebSocket = vtortola.WebSockets.WebSocket;
using WebSocketMessageType = vtortola.WebSockets.WebSocketMessageType;

namespace EvoS.GameServer
{
    public class ClientConnection
    {
        private WebSocket Socket;

        public ClientConnection(WebSocket socket)
        {
            Socket = socket;
        }

        public void Disconnect()
        {
            Log.Print(LogType.GameServer, "Client disconnected.");
            if (Socket.IsConnected)
                Socket.Close();

            Socket.Dispose();
        }

        public async Task SendMessage(object message)
        {
            throw new NotImplementedException();
        }

        public async void HandleConnection()
        {
            while (true)
            {
                var message = await Socket.ReadMessageAsync(CancellationToken.None);

                if (message == null)
                {
                    Log.Print(LogType.Warning, "Message is null");
                    Disconnect();
                    return;
                }

                switch (message.MessageType)
                {
                    case WebSocketMessageType.Text:
                        throw new NotImplementedException();
                    case WebSocketMessageType.Binary:
                    {
                        using (var ms = new MemoryStream())
                        {
                            await message.CopyToAsync(ms);
                            await ms.FlushAsync();

                            var messageBytes = ms.ToArray();

                            try
                            {
                                var unetBytes = UNetMessage.Deserialize(messageBytes);
                                ProcessUNetMessage(unetBytes);
                            }
                            catch (Exception e)
                            {
                                Log.Print(LogType.Error, Convert.ToBase64String(messageBytes));
                                Log.Print(LogType.Error, e.ToString());
                                continue;
                            }
                        }

                        break;
                    }
                }

                message.Dispose();
            }
        }

        private void ProcessUNetMessage(byte[] unetBytes)
        {
            var reader = new NetworkReader(unetBytes);
            while (reader.Position < unetBytes.Length)
            {
                var msgSeqNum = reader.ReadUInt32();
                var msgSize = reader.ReadUInt16();
                var msgId = reader.ReadInt16();
                var buffer = reader.ReadBytes(msgSize);
                var networkReader = new NetworkReader(buffer);

                Log.Print(LogType.Packet,
                    $"Received msgSeqNum={msgSeqNum}, msgSize={msgSize}, msgId={msgId} | {Convert.ToBase64String(buffer)}");
            }
        }
    }
}
