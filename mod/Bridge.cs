using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace SoulstoneAP
{
    /// <summary>
    /// Minimal line-based request/response server on loopback.
    ///
    /// Requests are dispatched to the Unity main thread (game objects are not
    /// thread safe), so the socket thread blocks on a ManualResetEventSlim
    /// while the main thread produces the answer.
    ///
    /// Wire format (UTF-8, one line each way):
    ///   request:  CMD [key=percentEncodedValue]...
    ///   response: OK percentEncodedPayload   |   ERR message
    /// </summary>
    internal static class Bridge
    {
        private sealed class Job
        {
            public string Request;
            public string Response;
            public readonly ManualResetEventSlim Done = new ManualResetEventSlim(false);
        }

        private static readonly ConcurrentQueue<Job> Jobs = new ConcurrentQueue<Job>();
        private static TcpListener _listener;
        private static Thread _thread;
        private static volatile bool _running;

        /// <summary>Handles one request line. Runs on the Unity main thread.</summary>
        public static Func<string, string> Handler;

        public static bool IsRunning => _running;

        public static void Start(int port)
        {
            if (_running) return;
            _listener = new TcpListener(IPAddress.Loopback, port);
            _listener.Start();
            _running = true;
            _thread = new Thread(Loop) { IsBackground = true, Name = "SoulstoneAP-Bridge" };
            _thread.Start();
        }

        public static void Stop()
        {
            _running = false;
            try { _listener?.Stop(); } catch { /* shutting down */ }
            _listener = null;
        }

        /// <summary>Drains queued requests. Must be called every frame from the main thread.</summary>
        public static void Pump()
        {
            while (Jobs.TryDequeue(out var job))
            {
                try
                {
                    job.Response = Handler?.Invoke(job.Request) ?? "ERR no handler";
                }
                catch (Exception e)
                {
                    job.Response = "ERR " + e.GetType().Name + ": " + e.Message.Replace('\n', ' ');
                }
                job.Done.Set();
            }
        }

        private static void Loop()
        {
            while (_running)
            {
                TcpClient client;
                try
                {
                    client = _listener.AcceptTcpClient();
                }
                catch
                {
                    break; // listener stopped
                }

                try
                {
                    using (client)
                    using (var stream = client.GetStream())
                    using (var reader = new StreamReader(stream, Encoding.UTF8))
                    using (var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true })
                    {
                        client.ReceiveTimeout = 10000;
                        string line = reader.ReadLine();
                        if (line == null) continue;

                        var job = new Job { Request = line };
                        Jobs.Enqueue(job);
                        // Generous: the main thread may be busy loading a scene.
                        string response = job.Done.Wait(15000) ? job.Response : "ERR main thread timeout";
                        writer.WriteLine(response);
                    }
                }
                catch
                {
                    // A single bad connection must never take the server down.
                }
            }
        }
    }
}
