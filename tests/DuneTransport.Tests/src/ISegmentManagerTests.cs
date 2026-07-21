using System;
using DuneTransport.BufferManager;
using DuneTransport.BufferManager.Interface;
using DuneTransport.Transport;
using DuneTransport.Transport.Interface;
using Xunit;

namespace DuneTransport.Tests
{
    /// <summary>
    /// Tests for ISegmentManager default interface methods (Serialize/Deserialize).
    /// These cover the 0% coverage gap in ISegmentManager.cs:79-119.
    /// </summary>
    public class ISegmentManagerTests
    {
        // Test implementation of ISegmentManager for testing default methods
        private class TestSegmentManager : ISegmentManager
        {
            public Segment segment { get; set; }
            public int PacketSize { get; set; }
            public bool OnSerializeCalled { get; private set; }
            public bool OnDeserializeCalled { get; private set; }
            public bool OnSerializeResult { get; set; } = true;
            public bool OnDeserializeResult { get; set; } = true;

            public bool OnSerialize()
            {
                OnSerializeCalled = true;
                return OnSerializeResult;
            }

            public bool OnDeserialize()
            {
                OnDeserializeCalled = true;
                return OnDeserializeResult;
            }
        }

        // Minimal transport that can reserve segments
        private class TestTransport : ITransport
        {
            private readonly SegmentedBuffer _buffer = new SegmentedBuffer(1024, 4);
            public bool IsConnected => true;
            public bool IsDisposed => false;
            public bool ReceiveArmed => false;
            public bool SendArmed => false;
            public Segment? LastReservedSegment { get; private set; }

            public event Action<ITransport>? OnPacketSent;
            public event Action<ITransport, Segment, TransportError>? OnPacketSendFailed;
            public event Action<ITransport, Segment>? OnPacketReceived;
            public event Action<ITransport, TransportError>? OnPacketReceiveFailed;

            public void ReceiveAsync() { }
            public void SendAsync(Segment packet, int packetSize) { }

            public bool TryReserveSendPacket(out Segment segment)
            {
                if (_buffer.TryReserveSegment(out segment))
                {
                    LastReservedSegment = segment;
                    return true;
                }
                segment = default;
                return false;
            }

            public void Dispose() { }
        }

        [Fact]
        public void ISegmentManager_Serialize_Success_CallsOnSerializeAndCallback()
        {
            // ARRANGE
            var manager = new TestSegmentManager();
            var transport = new TestTransport();
            Segment? callbackSegment = null;
            int callbackSize = -1;

            // ACT - Cast to interface to access default methods
            bool result = ((ISegmentManager)manager).Serialize(transport, (seg, size) =>
            {
                callbackSegment = seg;
                callbackSize = size;
            });

            // ASSERT
            Assert.True(result, "Serialize should return true on success");
            Assert.True(manager.OnSerializeCalled, "OnSerialize should be called");
            Assert.NotNull(callbackSegment);
            Assert.True(callbackSegment.Value.SegmentIndex > 0, "Segment should be reserved");
            Assert.Equal(transport.LastReservedSegment?.SegmentIndex, callbackSegment?.SegmentIndex);
            Assert.True(callbackSize >= 0, "Callback should receive size");
        }

        [Fact]
        public void ISegmentManager_Serialize_OnSerializeFails_ReturnsFalseAndReleasesSegment()
        {
            // ARRANGE
            var manager = new TestSegmentManager { OnSerializeResult = false };
            var transport = new TestTransport();

            // ACT
            bool result = ((ISegmentManager)manager).Serialize(transport, (_, _) => { });

            // ASSERT
            Assert.False(result, "Serialize should return false when OnSerialize fails");
            Assert.True(manager.OnSerializeCalled, "OnSerialize should still be called");
            // Segment should be released back to pool (FreeCount back to 4)
            var buf = (SegmentedBuffer)typeof(TestTransport).GetField("_buffer", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(transport)!;
            Assert.Equal(4, buf.FreeCount);
        }

        [Fact]
        public void ISegmentManager_Serialize_PoolExhausted_ReturnsFalse()
        {
            // ARRANGE - Exhaust the pool
            var transport = new TestTransport();
            for (int i = 0; i < 4; i++)
                transport.TryReserveSendPacket(out _);

            var manager = new TestSegmentManager();

            // ACT
            bool result = ((ISegmentManager)manager).Serialize(transport, (_, _) => { });

            // ASSERT
            Assert.False(result, "Serialize should return false when pool exhausted");
            Assert.False(manager.OnSerializeCalled, "OnSerialize should not be called when no segment available");
        }

        [Fact]
        public void ISegmentManager_Serialize_TransportDisposed_ReturnsFalse()
        {
            // ARRANGE
            var manager = new TestSegmentManager();
            var transport = new DisposedTransport();

            // ACT
            bool result = ((ISegmentManager)manager).Serialize(transport, (_, _) => { });

            // ASSERT
            Assert.False(result, "Serialize should return false for disposed transport");
        }

        [Fact]
        public void ISegmentManager_Deserialize_Success_CallsOnDeserializeAndReleases()
        {
            // ARRANGE
            var buffer = new SegmentedBuffer(1024, 4);
            buffer.TryReserveSegment(out var segment);
            segment.Memory = segment.Memory.Slice(0, 10); // Simulate 10-byte packet

            var manager = new TestSegmentManager
            {
                segment = segment,
                PacketSize = 10
            };

            Segment? beforeCallbackSeg = null;
            int beforeCallbackSize = -1;

            // ACT
            bool result = ((ISegmentManager)manager).Deserialize((seg, size) =>
            {
                beforeCallbackSeg = seg;
                beforeCallbackSize = size;
            });

            // ASSERT
            Assert.True(result, "Deserialize should return true on success");
            Assert.True(manager.OnDeserializeCalled, "OnDeserialize should be called");
            Assert.Equal(segment.SegmentIndex, beforeCallbackSeg?.SegmentIndex);
            Assert.Equal(10, beforeCallbackSize);
            // Segment should be released back to pool
            Assert.Equal(4, buffer.FreeCount);
        }

        [Fact]
        public void ISegmentManager_Deserialize_OnDeserializeFails_StillReleasesSegment()
        {
            // ARRANGE
            var buffer = new SegmentedBuffer(1024, 4);
            buffer.TryReserveSegment(out var segment);
            segment.Memory = segment.Memory.Slice(0, 10);

            var manager = new TestSegmentManager
            {
                segment = segment,
                PacketSize = 10,
                OnDeserializeResult = false // Simulate failure
            };

            // ACT
            bool result = ((ISegmentManager)manager).Deserialize((_, _) => { });

            // ASSERT
            Assert.False(result, "Deserialize should return false when OnDeserialize fails");
            Assert.True(manager.OnDeserializeCalled, "OnDeserialize should be called");
            // Segment MUST be released even on failure
            Assert.Equal(4, buffer.FreeCount);
        }

        [Fact]
        public void ISegmentManager_Deserialize_CallbackReceivesSegmentAndSize()
        {
            // ARRANGE
            var buffer = new SegmentedBuffer(1024, 4);
            buffer.TryReserveSegment(out var segment);
            segment.Memory = segment.Memory.Slice(0, 42); // 42 bytes

            var manager = new TestSegmentManager
            {
                segment = segment,
                PacketSize = 42
            };

            // ACT
            bool callbackInvoked = false;
            ((ISegmentManager)manager).Deserialize((seg, size) =>
            {
                callbackInvoked = true;
                Assert.Equal(segment.SegmentIndex, seg.SegmentIndex);
                Assert.Equal(42, size);
            });

            // ASSERT
            Assert.True(callbackInvoked, "BeforeDeserialize callback should be invoked");
        }

        private class DisposedTransport : ITransport
        {
            public bool IsConnected => false;
            public bool IsDisposed => true;
            public bool ReceiveArmed => false;
            public bool SendArmed => false;

            public event Action<ITransport>? OnPacketSent;
            public event Action<ITransport, Segment, TransportError>? OnPacketSendFailed;
            public event Action<ITransport, Segment>? OnPacketReceived;
            public event Action<ITransport, TransportError>? OnPacketReceiveFailed;

            public void ReceiveAsync() { }
            public void SendAsync(Segment packet, int packetSize) { }
            public bool TryReserveSendPacket(out Segment segment)
            {
                segment = default;
                return false;
            }
            public void Dispose() { }
        }
    }
}