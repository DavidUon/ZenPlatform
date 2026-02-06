using System;
using System.Threading.Tasks;

namespace ZenPlatform.Core
{
    public sealed class PriceSwitchCoordinator
    {
        private readonly Func<bool> _isBrokerConnected;
        private readonly Func<string?> _getUserEmail;
        private readonly Func<string> _getProgramName;
        private readonly Func<DateTime> _nowUtc;
        private readonly Action<string> _logAll;
        private readonly Func<string, string, string, Task> _sendMailAsync;

        private DateTime _startupAtUtc = DateTime.UtcNow;
        private DateTime? _lastSwitchMailAt;
        private DateTime? _lastRecoverMailAt;
        private int _sourceChangeSkipCount;
        private bool _ddeLostSinceStartup;

        public PriceSwitchCoordinator(
            Func<bool> isBrokerConnected,
            Func<string?> getUserEmail,
            Func<string> getProgramName,
            Func<DateTime> nowUtc,
            Action<string> logAll,
            Func<string, string, string, Task> sendMailAsync)
        {
            _isBrokerConnected = isBrokerConnected ?? throw new ArgumentNullException(nameof(isBrokerConnected));
            _getUserEmail = getUserEmail ?? throw new ArgumentNullException(nameof(getUserEmail));
            _getProgramName = getProgramName ?? throw new ArgumentNullException(nameof(getProgramName));
            _nowUtc = nowUtc ?? throw new ArgumentNullException(nameof(nowUtc));
            _logAll = logAll ?? throw new ArgumentNullException(nameof(logAll));
            _sendMailAsync = sendMailAsync ?? throw new ArgumentNullException(nameof(sendMailAsync));
        }

        public void MarkStartup()
        {
            _startupAtUtc = _nowUtc();
            _sourceChangeSkipCount = 2;
            _ddeLostSinceStartup = false;
        }

        public void HandleSourceChanged(PriceManager.PriceSource from, PriceManager.PriceSource to)
        {
            var fromText = from switch
            {
                PriceManager.PriceSource.Dde => "全都賺報價",
                PriceManager.PriceSource.Network => "網路報價",
                _ => ""
            };
            var toText = to switch
            {
                PriceManager.PriceSource.Dde => "全都賺報價",
                PriceManager.PriceSource.Network => "網路報價",
                _ => ""
            };
            var text = string.IsNullOrWhiteSpace(fromText)
                ? $"報價切換:{toText}"
                : $"報價切換:{fromText} -> {toText}";
            if (_sourceChangeSkipCount > 0)
            {
                _sourceChangeSkipCount--;
            }
            else
            {
                _logAll(text);
            }

            if (from == PriceManager.PriceSource.Network && to == PriceManager.PriceSource.Dde)
            {
                if (_ddeLostSinceStartup)
                {
                    _ = SendPriceRecoveredMailAsync();
                }
            }
            else if (from == PriceManager.PriceSource.Dde && to == PriceManager.PriceSource.Network)
            {
                _ddeLostSinceStartup = true;
            }
        }

        public void HandleAutoSwitchedToNetwork(PriceMonitor.PriceStallInfo info)
        {
            _ddeLostSinceStartup = true;
            _ = SendPriceMonitorMailAsync(info);
        }

        private async Task SendPriceMonitorMailAsync(PriceMonitor.PriceStallInfo info)
        {
            if (_isBrokerConnected())
            {
                if ((_nowUtc() - _startupAtUtc) < TimeSpan.FromMinutes(5))
                {
                    return;
                }

                var now = _nowUtc();
                var last = _lastSwitchMailAt;
                var minInterval = TimeSpan.FromHours(5);
                if (last.HasValue && (now - last.Value) < minInterval)
                {
                    return;
                }
                _lastSwitchMailAt = now;
            }

            var email = _getUserEmail();
            if (string.IsNullOrWhiteSpace(email))
            {
                return;
            }

            var subject = "【系統通知】DDE 報價中斷，已切換至網路報價備援";
            var lastChange = info.LastChangeAt.ToString("yyyy/MM/dd HH:mm:ss");
            var lastDde = info.LastDdeAt?.ToString("yyyy/MM/dd HH:mm:ss") ?? "---";
            var lastNet = info.LastNetworkAt?.ToString("yyyy/MM/dd HH:mm:ss") ?? "---";
            var programName = _getProgramName();
            var body =
                $"親愛的使用者您好：\n\n" +
                $"{programName} 系統已偵測到您的 DDE 報價來源發生中斷並停止更新，\n" +
                $"系統已於第一時間自動切換至「網路報價」來源作為備援，以維持報價更新與交易流程的基本運作。\n\n" +
                $"目前系統狀態如下：\n\n" +
                $"原報價來源：DDE（已停止更新）\n\n" +
                $"目前使用報價來源：網路報價（備援）\n\n" +
                $"系統運作狀態：正常（備援模式運行中）\n\n" +
                $"需特別提醒您，網路報價僅作為臨時備援用途，其穩定性與即時性可能與原 DDE 報價來源有所差異。\n" +
                $"為確保報價品質與交易安全，建議您儘早檢查並排除 DDE 報價中斷的原因，使系統恢復至正常主要報價來源運作。\n\n" +
                $"待 DDE 報價恢復正常後，系統將依設定自動切換或另行通知。\n\n" +
                $"如您在處理過程中有任何疑問，歡迎隨時與我們聯繫。\n\n" +
                $"祝\n交易順利\n\n" +
                $"Magistock 系統自動通知";

            await _sendMailAsync(email, subject, body).ConfigureAwait(false);
        }

        private async Task SendPriceRecoveredMailAsync()
        {
            if (_isBrokerConnected())
            {
                var now = _nowUtc();
                var minInterval = TimeSpan.FromHours(1);
                if (_lastRecoverMailAt.HasValue && (now - _lastRecoverMailAt.Value) < minInterval)
                {
                    return;
                }
                _lastRecoverMailAt = now;
            }

            var email = _getUserEmail();
            if (string.IsNullOrWhiteSpace(email))
            {
                return;
            }

            var subject = "【系統通知】DDE 報價已恢復，已切回主要報價來源";
            var programName = _getProgramName();
            var body =
                $"親愛的使用者您好：\n\n" +
                $"{programName} 系統已偵測到 DDE 報價恢復正常，\n" +
                $"目前已切回「DDE 報價」作為主要報價來源。\n\n" +
                $"系統運作狀態：正常\n\n" +
                $"若您仍有任何疑慮，請隨時與我們聯繫。\n\n" +
                $"祝\n交易順利\n\n" +
                $"Magistock 系統自動通知";

            await _sendMailAsync(email, subject, body).ConfigureAwait(false);
        }
    }
}
