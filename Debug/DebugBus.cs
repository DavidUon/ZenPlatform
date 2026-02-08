using System.Threading;
using System.Threading.Tasks;

namespace ZenPlatform.Debug
{
    public static class DebugBus
    {
        private static readonly object SyncRoot = new object();
        private static DebuggerClient _client = new DebuggerClient();
        private static bool _connectAttempted;

        public static void Initialize(string serverUrl = "ws://localhost:2026/ws", string connectionName = "Client")
        {
            lock (SyncRoot)
            {
                _client.Dispose();
                _client = new DebuggerClient(serverUrl, connectionName);
            }
        }

        public static async Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            if (_connectAttempted)
            {
                return;
            }

            _connectAttempted = true;
            await _client.ConnectAsync(cancellationToken).ConfigureAwait(false);
        }

        public static Task SendAsync(string message, string debugSession, DebugMessageType colorType = DebugMessageType.Info, CancellationToken cancellationToken = default)
        {
            return _client.SendDebuggerMsgAsync(message, debugSession, colorType, cancellationToken);
        }

        public static void Send(string message, string debugSession, DebugMessageType colorType = DebugMessageType.Info)
        {
            _ = _client.SendDebuggerMsgAsync(message, debugSession, colorType);
        }

        public static Task ClearBlockAsync(string debugSession, CancellationToken cancellationToken = default)
        {
            var name = string.IsNullOrWhiteSpace(debugSession) ? "General" : debugSession.Trim();
            var payload = $"__SYS__|CLEAR|{name}";
            return _client.SendRawAsync(payload, cancellationToken);
        }

        public static void ClearBlock(string debugSession)
        {
            var name = string.IsNullOrWhiteSpace(debugSession) ? "General" : debugSession.Trim();
            var payload = $"__SYS__|CLEAR|{name}";
            _client.SendRawAsync(payload).GetAwaiter().GetResult();
        }
    }
}
