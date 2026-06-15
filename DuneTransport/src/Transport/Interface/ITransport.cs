using System;
using System.Net.Sockets;
using DuneTransport.BufferManager;

namespace DuneTransport.Transport.Interface
{
    public interface ITransport : IDisposable
    {
        bool IsConnected { get; }
        bool IsDisposed { get; }

        event Action<ITransport>? OnPacketSent;

        /// <summary>
        /// Raised when a send attempt fails. Segment ownership TRANSFERS to
        /// the subscriber — the segment is NOT released by Transport. The
        /// subscriber owns the segment and must either:
        /// <list type="bullet">
        ///   <item>Call <c>SendAsync()</c> again to retry (same segment, same data).</item>
        ///   <item>Call <c>Release()</c> to give up and return it to the pool.</item>
        /// </list>
        /// The <see cref="TransportError"/> argument indicates why the send failed.
        /// All error types — transient and terminal — follow this same ownership
        /// contract; the application decides what to do based on the error code.
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

        /// <summary>
        /// Begins an asynchronous send of the given segment.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Only one send may be in flight at a time. If a previous send has not
        /// yet completed (via <see cref="OnPacketSent"/> or
        /// <see cref="OnPacketSendFailed"/>), calling this method again will fail
        /// with <c>TransportError.SendAlreadyPending</c>.
        /// </para>
        /// <para>
        /// <b>API Caveat — Retry contract:</b> When <see cref="OnPacketSendFailed"/>
        /// fires, the <c>_sendInFlight</c> guard is cleared and the transport becomes
        /// technically capable of accepting a new send. However, the application MUST
        /// NOT call <c>SendAsync()</c> with a different packet until it has resolved
        /// the failed one — either by retrying the same segment or releasing it.
        /// Sending a new packet while a previous failure is unresolved is an
        /// application-level error: the library provides no ordering or queuing
        /// guarantees, and packets may arrive out of intended sequence.
        /// </para>
        /// </remarks>
        void SendAsync(Segment packet, int packetSize);

        void ReceiveAsync();
        bool TryReserveSendPacket(out Segment segment);
    }
}