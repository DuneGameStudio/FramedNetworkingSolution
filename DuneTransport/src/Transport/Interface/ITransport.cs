using System;
using System.Net.Sockets;
using DuneTransport.BufferManager;

namespace DuneTransport.Transport.Interface
{
    public interface ITransport : IDisposable
    {
        bool IsConnected { get; }
        bool IsDisposed { get; }

        SegmentedBuffer receiveBuffer { get; }
        SegmentedBuffer sendBuffer { get; }

        event Action<ITransport>? OnPacketSent;

        /// <summary>
        /// Raised after a send attempt fails. The Segment argument has ALREADY
        /// been released by Transport — subscribers receive it for inspection
        /// only (e.g. logging <c>SegmentIndex</c>) and MUST NOT call
        /// <c>Release()</c> on it. Double-release will corrupt the pool.
        /// </summary>
        event Action<ITransport, Segment, TransportError>? OnPacketSendFailed;

        /// <summary>
        /// Raised when a complete framed packet has been received. Segment
        /// ownership TRANSFERS to the subscriber — the subscriber is
        /// responsible for calling <c>Release()</c> when done.
        /// </summary>
        event Action<ITransport, SocketAsyncEventArgs, Segment>? OnPacketReceived;

        /// <summary>
        /// Raised when a receive attempt fails. Any segment Transport had
        /// rented has already been released; no segment is passed to the
        /// subscriber. The <see cref="TransportError"/> argument indicates
        /// which branch fired.
        /// </summary>
        event Action<ITransport, TransportError>? OnPacketReceiveFailed;

        event Action? OnDisconnectRequested;

        void ReceiveAsync();
        void SendAsync(Segment packet, int packetSize);
        bool TryReserveSendPacket(out Segment segment);
    }
}