using System.Net;
using System.Net.Sockets;
using DuneTransport.Transport;

namespace HardeningValidation
{
    internal static class Program
    {
        private static int _pass, _fail;

        private static void Main()
        {
            RunLoopback("send-on-disconnected-releases",TestSendOnDisconnectedReleases);
            RunLoopback("oversized-frame-rejects",      TestOversizedFrameRejects);
            RunLoopback("zero-length-frame-rejects",    TestZeroLengthFrameRejects);
            RunLoopback("fin-mid-header-cleans-up",     TestFinMidHeaderCleansUp);
            RunLoopback("handler-throw-no-leak",        TestHandlerThrowNoLeak);
            RunLoopback("dispose-during-receive",       TestDisposeDuringReceive);
            RunLoopback("double-dispose-noop",          TestDoubleDisposeNoop);
            Console.WriteLine($"\n{_pass} passed, {_fail} failed.");
            Environment.ExitCode = _fail == 0 ? 0 : 1;
        }

        private static void RunLoopback(string name, Action<DuneTransport.Transport.Transport, Socket> body)
        {
            using var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            listener.Listen(1);
            var port = ((IPEndPoint)listener.LocalEndPoint!).Port;

            using var clientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            clientSocket.Connect(IPAddress.Loopback, port);
            using var serverSocket = listener.Accept();

            var t = new DuneTransport.Transport.Transport(clientSocket);

            try
            {
                body(t, serverSocket);
                Report(name, true, null);
            }
            catch (Exception e)
            {
                Report(name, false, e.ToString());
            }
            finally
            {
                try { t.Dispose(); } catch { }
            }
        }

        private static void Report(string name, bool ok, string? err)
        {
            if (ok) { _pass++; Console.WriteLine($"PASS  {name}"); }
            else    { _fail++; Console.WriteLine($"FAIL  {name}\n      {err}"); }
        }

        private static void TestSendOnDisconnectedReleases(DuneTransport.Transport.Transport t, Socket peer)
        {
            peer.Close();
            Thread.Sleep(50);
            t.TryReserveSendPacket(out var p);
            try { t.SendAsync(p, 0); } catch (InvalidOperationException) { }
            AssertBaseline(t);
        }

        private static void TestOversizedFrameRejects(DuneTransport.Transport.Transport t, Socket peer)
        {
            var rejected = false;
            t.OnPacketReceiveFailed += (_, reason) => { if (reason == TransportError.ProtocolError) rejected = true; };
            t.ReceiveAsync();
            var overSize = (ushort)(t.receiveBuffer.SegmentSize + 1);
            peer.Send(BitConverter.GetBytes(overSize));
            Thread.Sleep(100);
            if (!rejected) throw new Exception("oversized frame not rejected");
            AssertBaseline(t);
        }

        private static void TestZeroLengthFrameRejects(DuneTransport.Transport.Transport t, Socket peer)
        {
            var rejected = false;
            t.OnPacketReceiveFailed += (_, reason) => { if (reason == TransportError.ProtocolError) rejected = true; };
            t.ReceiveAsync();
            peer.Send(BitConverter.GetBytes((ushort)0));
            Thread.Sleep(100);
            if (!rejected) throw new Exception("zero frame not rejected");
            AssertBaseline(t);
        }

        private static void TestFinMidHeaderCleansUp(DuneTransport.Transport.Transport t, Socket peer)
        {
            var disc = false;
            t.OnDisconnectRequested += () => disc = true;
            t.ReceiveAsync();
            peer.Send(new byte[] { 0x01 });
            peer.Shutdown(SocketShutdown.Send);
            Thread.Sleep(100);
            if (!disc) throw new Exception("FIN not observed");
            AssertBaseline(t);
        }

        private static void TestHandlerThrowNoLeak(DuneTransport.Transport.Transport t, Socket peer)
        {
            var failed = false;
            t.OnPacketReceived += (_, __, seg) => throw new Exception("handler bug");
            t.OnPacketReceiveFailed += (_, reason) => { if (reason == TransportError.HandlerFailed) failed = true; };
            t.ReceiveAsync();
            peer.Send(BitConverter.GetBytes((ushort)1));
            peer.Send(new byte[] { 0x42 });
            Thread.Sleep(100);
            if (!failed) throw new Exception("HandlerFailed not raised");
            AssertBaseline(t);
        }

        private static void TestDisposeDuringReceive(DuneTransport.Transport.Transport t, Socket peer)
        {
            t.ReceiveAsync();
            t.Dispose();
            try { t.ReceiveAsync(); throw new Exception("expected ObjectDisposedException"); }
            catch (ObjectDisposedException) { }
            AssertBaseline(t);
        }

        private static void TestDoubleDisposeNoop(DuneTransport.Transport.Transport t, Socket peer)
        {
            t.Dispose();
            t.Dispose();
            AssertBaseline(t);
        }

        private static void AssertBaseline(DuneTransport.Transport.Transport t)
        {
            var rcv = t.receiveBuffer.SegmentCount - t.receiveBuffer.FreeCount;
            var snd = t.sendBuffer.SegmentCount    - t.sendBuffer.FreeCount;
            if (rcv != 0 || snd != 0)
                throw new Exception($"pool leaked: receive={rcv}, send={snd}");
        }
    }
}
