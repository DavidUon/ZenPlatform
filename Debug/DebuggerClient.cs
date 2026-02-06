using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ZenPlatform.Debug
{
    public enum DebugMessageType
    {
        Info,
        Debug,
        Warn,
        Error,
        Trace
    }

    public sealed class DebuggerClient : IDisposable
    {
        private readonly Uri _serverUri;
        private readonly string _connectionName;
        private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);
        private ClientWebSocket? _socket;

        public DebuggerClient(string serverUrl = "ws://localhost:2026/ws", string connectionName = "台指二號")
        {
            _serverUri = new Uri(serverUrl);
            _connectionName = connectionName;
        }

        public bool IsConnected => _socket != null && _socket.State == WebSocketState.Open;

        public async Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            if (IsConnected)
            {
                return;
            }

            _socket?.Dispose();
            _socket = new ClientWebSocket();
            await _socket.ConnectAsync(_serverUri, cancellationToken).ConfigureAwait(false);
            await SendConnectionHelloAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task SendDebuggerMsgAsync(string message, string debugSession, DebugMessageType colorType = DebugMessageType.Info, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(message) || !IsConnected)
            {
                return;
            }

            var payload = FormatMessage(debugSession, colorType, message);
            var bytes = Encoding.UTF8.GetBytes(payload);
            var segment = new ArraySegment<byte>(bytes);

            await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await _socket!.SendAsync(segment, WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _sendLock.Release();
            }
        }

        public async Task SendRawAsync(string payload, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(payload) || !IsConnected)
            {
                return;
            }

            var bytes = Encoding.UTF8.GetBytes(payload);
            var segment = new ArraySegment<byte>(bytes);

            await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await _socket!.SendAsync(segment, WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _sendLock.Release();
            }
        }

        public void SendDebuggerMsg(string message, string debugSession, DebugMessageType colorType = DebugMessageType.Info)
        {
            SendDebuggerMsgAsync(message, debugSession, colorType).GetAwaiter().GetResult();
        }

        public async Task DisconnectAsync(CancellationToken cancellationToken = default)
        {
            if (_socket == null)
            {
                return;
            }

            if (_socket.State == WebSocketState.Open)
            {
                await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", cancellationToken)
                    .ConfigureAwait(false);
            }

            _socket.Dispose();
            _socket = null;
        }

        public void Dispose()
        {
            DisconnectAsync().GetAwaiter().GetResult();
            _sendLock.Dispose();
        }

        private async Task SendConnectionHelloAsync(CancellationToken cancellationToken)
        {
            if (_socket == null || _socket.State != WebSocketState.Open)
            {
                return;
            }

            var name = string.IsNullOrWhiteSpace(_connectionName) ? "Client" : _connectionName.Trim();
            var payload = $"__SYS__|INFO|CONNECT:{name}";
            var bytes = Encoding.UTF8.GetBytes(payload);
            var segment = new ArraySegment<byte>(bytes);

            await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await _socket.SendAsync(segment, WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _sendLock.Release();
            }
        }

        private static string FormatMessage(string debugSession, DebugMessageType colorType, string message)
        {
            var safeBlock = string.IsNullOrWhiteSpace(debugSession) ? "Uncategorized" : debugSession.Trim();
            var safeColorType = colorType.ToString().ToUpperInvariant();
            var safeMessage = message.Trim();
            return $"{safeBlock}|{safeColorType}|{safeMessage}";
        }
    }
}
