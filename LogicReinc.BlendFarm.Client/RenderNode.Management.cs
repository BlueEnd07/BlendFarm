using System;
using System.Threading;
using System.Threading.Tasks;

namespace LogicReinc.BlendFarm.Client
{
    public partial class RenderNode
    {
        private bool _isEnabled = true;
        private bool _isSelected;
        private bool _connectionAccepted;
        private volatile bool _manualDisconnect;
        private int _disconnectGeneration;
        private readonly SemaphoreSlim _connectLock = new SemaphoreSlim(1, 1);
        private CancellationTokenSource _reconnectCancellation;
        private readonly object _reconnectLock = new object();
        private Func<string> _reconnectVersion;
        private bool _autoReconnect;
        public event Action<RenderNode, string> ConnectionFailed;

        public bool IsEnabled
        {
            get => _isEnabled;
            set { if (_isEnabled == value) return; _isEnabled = value; TriggerPropChange(nameof(IsEnabled), nameof(DragOpacity)); }
        }
        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; TriggerPropChange(nameof(IsSelected)); }
        }
        public string RenderTypeBadge => RenderType == Shared.RenderType.CPU ? "CPU" : "GPU";
        public DateTimeOffset? ConnectedSince { get; private set; }
        public DateTimeOffset? LastSeen { get; private set; }
        public string ConnectionTimeDisplay => Connected && ConnectedSince.HasValue
            ? $"Connected since {ConnectedSince.Value.ToLocalTime():g}"
            : LastSeen.HasValue ? $"Last seen {LastSeen.Value.ToLocalTime():g}" : "Not seen yet";
        public string ReconnectStatus { get; private set; } = "";
        public bool IsReconnecting => !string.IsNullOrEmpty(ReconnectStatus);

        public void ConfigureReconnect(bool enabled, Func<string> version)
        {
            _reconnectVersion = version;
            _autoReconnect = enabled;
            if (!enabled) StopReconnect();
        }

        private void UpdateConnectionTime(bool connected)
        {
            LastSeen = DateTimeOffset.Now;
            ConnectedSince = connected ? LastSeen : null;
            TriggerPropChange(nameof(ConnectionTimeDisplay), nameof(ConnectedSince), nameof(LastSeen));
        }

        private void SetReconnectStatus(string status)
        {
            ReconnectStatus = status;
            TriggerPropChange(nameof(ReconnectStatus), nameof(IsReconnecting));
        }

        private void StartReconnect()
        {
            lock (_reconnectLock)
            {
                if (!_autoReconnect || _manualDisconnect || _reconnectVersion == null || _reconnectCancellation != null)
                    return;
                var cancellation = new CancellationTokenSource();
                _reconnectCancellation = cancellation;
                _ = Reconnect(cancellation);
            }
        }

        private async Task Reconnect(CancellationTokenSource cancellation)
        {
            try
            {
                for (int attempt = 1; !cancellation.IsCancellationRequested; attempt++)
                {
                    int seconds = (int)NodeConnectionPolicy.ReconnectDelay(attempt).TotalSeconds;
                    while (seconds > 0)
                    {
                        SetReconnectStatus($"Reconnecting in {seconds}s (attempt {attempt})");
                        await Task.Delay(1000, cancellation.Token);
                        seconds--;
                    }
                    SetReconnectStatus($"Reconnecting (attempt {attempt})");
                    try
                    {
                        cancellation.Token.ThrowIfCancellationRequested();
                        await _connectLock.WaitAsync(cancellation.Token);
                        try
                        {
                            cancellation.Token.ThrowIfCancellationRequested();
                            await ConnectCore();
                        }
                        finally { _connectLock.Release(); }
                        cancellation.Token.ThrowIfCancellationRequested();
                        var version = _reconnectVersion;
                        if (Connected && version != null)
                            await PrepareVersion(version());
                        if (Connected && IsPrepared) return;
                    }
                    catch (Exception) when (!cancellation.IsCancellationRequested) { }
                }
            }
            catch (OperationCanceledException) { }
            finally
            {
                lock (_reconnectLock)
                {
                    if (_reconnectCancellation == cancellation)
                    {
                        _reconnectCancellation = null;
                        SetReconnectStatus("");
                        if (!cancellation.IsCancellationRequested && !Connected)
                            StartReconnect();
                    }
                    cancellation.Dispose();
                }
            }
        }

        public void StopReconnect()
        {
            lock (_reconnectLock)
            {
                var cancellation = _reconnectCancellation;
                _reconnectCancellation = null;
                cancellation?.Cancel();
                SetReconnectStatus("");
            }
        }

        private async Task<RenderClient> ConnectWithTimeout()
        {
            var connection = RenderClient.Connect(Address);
            if (await Task.WhenAny(connection, Task.Delay(15000)) == connection)
                return await connection;
            _ = CloseLateConnection(connection);
            throw new TimeoutException("TCP connection timed out");
        }

        private static async Task CloseLateConnection(Task<RenderClient> connection)
        {
            try { (await connection)?.Disconnect(); }
            catch (Exception) { }
        }
    }
}
