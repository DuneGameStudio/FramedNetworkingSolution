using System;

namespace DunePresentation.Encryption.Interface
{
    public interface IPacketEncryptor
    {
        void Encrypt(ReadOnlySpan<byte> source, Span<byte> destination);
        void Decrypt(ReadOnlySpan<byte> source, Span<byte> destination);
    }
}
