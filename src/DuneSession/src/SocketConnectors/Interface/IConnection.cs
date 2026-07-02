using System;
using DuneTransport.Transport.Interface;

namespace DuneSession.SocketConnectors.Interface
{
    /// <summary>
    /// Wrapper around an established socket connection with embedded <see cref="ITransport"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Provides the connection lifecycle (connect, disconnect, dispose) and exposes
    /// the transport for packet I/O. Automatically fires <see cref="OnDisconnected"/>
    /// on remote disconnect or transport-level socket errors.
    /// </para>
    /// </remarks>
    public interface IConnection : IDisposable
    {
        /// <summary>Gets whether the connection is currently active.</summary>
        bool IsConnected { get; }

        /// <summary>
        /// Gets the transport layer for sending and receiving packet data.
        /// </summary>
        ITransport Transport { get; }

        /// <summary>
        /// Raised when the connection is terminated (graceful or error).
        /// </summary>
        /// <remarks>
        /// <para>
        /// Fires on both local disconnect (<see cref="DisconnectAsync"/>) and
        /// remote disconnect. Also fires when the transport detects a
        /// <c>TransportError.SocketDisconnected</c> condition.
        /// </para>
        /// </remarks>
        event Action? OnDisconnected;

        /// <summary>
        /// Initiates an asynchronous graceful disconnect.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Sends TCP FIN and waits for peer acknowledgment. Fires <see cref="OnDisconnected"/>
        /// when the disconnect completes. Does nothing if already disconnected or disposed.
        /// </para>
        /// </remarks>
        void DisconnectAsync();
    }
}
