using System;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SoulstoneAP
{
    /// <summary>
    /// Minimal Archipelago websocket session. Runs entirely off the Unity thread;
    /// the main thread drains <see cref="Incoming"/> and fills <see cref="Outgoing"/>.
    /// </summary>
    internal sealed class ApSession
    {
        private readonly ConcurrentQueue<string> _incoming = new ConcurrentQueue<string>();
        private readonly ConcurrentQueue<string> _outgoing = new ConcurrentQueue<string>();
        private CancellationTokenSource _cts;

        public volatile string Status = "Not connected";
        public volatile bool Connected;

        public ConcurrentQueue<string> Incoming => _incoming;
        public ConcurrentQueue<string> Outgoing => _outgoing;
        public bool IsRunning => _cts != null && !_cts.IsCancellationRequested;

        public void Start(string host, int port, string slot, string password)
        {
            Stop();
            var cts = new CancellationTokenSource();
            _cts = cts;
            Connected = false;
            Status = $"Connecting to {host}:{port}...";
            Task.Run(() => RunAsync(host, port, slot, password, cts.Token));
        }

        public void Stop()
        {
            var cts = _cts;
            _cts = null;
            Connected = false;
            try { cts?.Cancel(); } catch { /* already gone */ }
            Status = "Not connected";
        }

        public void Send(string json)
        {
            if (json != null) _outgoing.Enqueue(json);
        }

        private async Task RunAsync(string host, int port, string slot, string password, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                using (var socket = new ClientWebSocket())
                {
                    try
                    {
                        socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
                        await socket.ConnectAsync(new Uri($"ws://{host}:{port}"), token).ConfigureAwait(false);
                        Status = "Connected, joining slot...";

                        var sender = Task.Run(() => SendLoopAsync(socket, token), token);
                        await ReceiveLoopAsync(socket, token).ConfigureAwait(false);
                        await sender.ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception error)
                    {
                        Status = "Error: " + error.Message;
                    }
                    finally
                    {
                        Connected = false;
                    }
                }

                if (token.IsCancellationRequested) break;

                Status = "Disconnected - retrying in 5s";
                try { await Task.Delay(5000, token).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }

            Status = "Not connected";
        }

        private async Task SendLoopAsync(ClientWebSocket socket, CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested && socket.State == WebSocketState.Open)
                {
                    while (_outgoing.TryDequeue(out var message))
                    {
                        var bytes = Encoding.UTF8.GetBytes(message);
                        await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, token)
                            .ConfigureAwait(false);
                    }
                    await Task.Delay(40, token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception error) { Status = "Send error: " + error.Message; }
        }

        private async Task ReceiveLoopAsync(ClientWebSocket socket, CancellationToken token)
        {
            var buffer = new byte[65536];
            var builder = new StringBuilder();

            while (!token.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), token).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close) break;

                builder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                if (!result.EndOfMessage) continue;

                _incoming.Enqueue(builder.ToString());
                builder.Clear();
            }
        }
    }
}
