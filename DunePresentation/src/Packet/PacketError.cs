namespace DunePresentation.Packet
{
    /// <summary>
    /// Reason codes surfaced with Peer-level failure events.
    /// </summary>
    public enum PacketError
    {
        /// <summary>Underlying segment pool had no free segment when one was needed during serialization.</summary>
        PoolExhausted,

        /// <summary>Serialization failed (buffer write, field serialization, or encryptor exception during Serialize).</summary>
        SerializationError,

        /// <summary>Decrypt failed (encryptor threw during DeserializeReceivedPacket).</summary>
        DecryptError,

        /// <summary>Deserialize failed (packet.ReadFieldsFromBuffer returned false during DeserializeReceivedPacket).</summary>
        DeserializeError,

        /// <summary>The packet registry failed to resolve the received packet ID.</summary>
        RegistryError
    }
}
