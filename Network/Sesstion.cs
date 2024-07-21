using System;
using System.Net.Sockets;

namespace Network
{
    public class Session
    {
        public Guid guid;

        public Transport.Transport transport;

        public Session(Transport.Transport transport, Guid guid)
        {
            this.guid = guid;
            this.transport = transport;

            transport.OnPacketReceived += OnPacketReceived;
        }

        public void Receive()
        {
            transport.ReceiveAsync();
        }

        private void OnPacketReceived(object sender, SocketAsyncEventArgs packetReceivedAsyncEventArgs)
        {
            // packetReceivedAsyncEventArgs.SocketError;
            // packetReceivedAsyncEventArgs.Buffer.Length;
        }
    }
}