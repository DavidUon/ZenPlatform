using System;
using System.Windows;
using System.Windows.Media;
using ZenPlatform.Strategy;

namespace ZenPlatform
{
    public partial class StrategySettingsWindow : Window
    {
        private const int DefaultTakeProfitPoints = 300;
        private const int DefaultAutoTakeProfitPoints = 300;
        private const int DefaultAbsoluteStopLossPoints = 300;
        private const int DefaultTotalProfitRiseArmBelowPoints = 100;
        private const int DefaultTotalProfitDropTriggerPoints = 500;
        private const int DefaultTotalProfitDropExitPoints = 100;
        private readonly RuleSet _ruleSet;
        private readonly bool _lockCoreOrderInputs;
        private bool _syncSharedMa;

        public StrategySettingsWindow(RuleSet ruleSet, bool lockCoreOrderInputs = false)
        {
            _ruleSet = ruleSet ?? throw new ArgumentNullException(nameof(ruleSet));
            _lockCoreOrderInputs = lockCoreOrderInputs;
            InitializeComponent();
            HookSharedMaSync();
            LoadFromRuleSet();
            ApplyEditLocks();
        }

        private void HookSharedMaSync()
        {
            ProfitRetracePercentBox.TextChanged += (_, __) =>
            {
                if (_syncSharedMa)
                {
                    return;
                }

                _syncSharedMa = true;
                LossRetracePercentBox.Text = ProfitRetracePercentBox.Text;
                _syncSharedMa = false;
            };

            LossRetracePercentBox.TextChanged += (_, __) =>
            {
                if (_syncSharedMa)
                {
                    return;
                }

                _syncSharedMa = true;
                ProfitRetracePercentBox.Text = LossRetracePercentBox.Text;
                _syncSharedMa = false;
            };
        }

        private void LoadFromRuleSet()
        {
            OrderSizeBox.Text = _ruleSet.OrderSize.ToString();
            KbarPeriodBox.Text = _ruleSet.KbarPeriod.ToString();
            TakeProfitBox.Text = (_ruleSet.TakeProfitPoints > 0 ? _ruleSet.TakeProfitPoints : DefaultTakeProfitPoints).ToString();
            AutoTakeProfitBox.Text = (_ruleSet.AutoTakeProfitPoints > 0 ? _ruleSet.AutoTakeProfitPoints : DefaultAutoTakeProfitPoints).ToString();
            TakeProfitFixedRadio.IsChecked = _ruleSet.TakeProfitPoints > 0;
            TakeProfitAutoRadio.IsChecked = _ruleSet.AutoTakeProfitPoints > 0;
            CoverLossBeforeTakeProfitCheckBox.IsChecked = _ruleSet.CoverLossBeforeTakeProfit;
            CoverLossTriggerPointsBox.Text = _ruleSet.CoverLossTriggerPoints.ToString();
            ExitOnTotalProfitRiseCheckBox.IsChecked = _ruleSet.ExitOnTotalProfitRise;
            ExitOnTotalProfitRiseArmBelowPointsBox.Text = (_ruleSet.ExitOnTotalProfitRiseArmBelowPoints > 0 ? _ruleSet.ExitOnTotalProfitRiseArmBelowPoints : DefaultTotalProfitRiseArmBelowPoints).ToString();
            ExitOnTotalProfitRisePointsBox.Text = _ruleSet.ExitOnTotalProfitRisePoints.ToString();
            ExitOnTotalProfitDropAfterTriggerCheckBox.IsChecked = _ruleSet.ExitOnTotalProfitDropAfterTrigger;
            ExitOnTotalProfitDropTriggerPointsBox.Text = (_ruleSet.ExitOnTotalProfitDropTriggerPoints > 0 ? _ruleSet.ExitOnTotalProfitDropTriggerPoints : DefaultTotalProfitDropTriggerPoints).ToString();
            ExitOnTotalProfitDropExitPointsBox.Text = (_ruleSet.ExitOnTotalProfitDropExitPoints > 0 ? _ruleSet.ExitOnTotalProfitDropExitPoints : DefaultTotalProfitDropExitPoints).ToString();
            ProfitRetraceExitCheckBox.IsChecked = _ruleSet.ProfitRetraceExitEnabled;
            ProfitRetraceTriggerPointsBox.Text = _ruleSet.ProfitRetraceTriggerPoints.ToString();
            var sharedRetraceMa = _ruleSet.LossRetracePercent > 0 ? _ruleSet.LossRetracePercent : _ruleSet.ProfitRetracePercent;
            ProfitRetracePercentBox.Text = sharedRetraceMa.ToString();
            StopLossBox.Text = _ruleSet.StopLossPoints.ToString();
            AbsoluteStopLossCheckBox.IsChecked = _ruleSet.EnableAbsoluteStopLoss;
            AbsoluteStopLossBox.Text = (_ruleSet.AbsoluteStopLossPoints > 0 ? _ruleSet.AbsoluteStopLossPoints : DefaultAbsoluteStopLossPoints).ToString();
            LossRetraceExitCheckBox.IsChecked = _ruleSet.LossRetraceExitEnabled;
            LossRetraceTriggerPointsBox.Text = _ruleSet.LossRetraceTriggerPoints.ToString();
            LossRetracePercentBox.Text = sharedRetraceMa.ToString();
            StopLossFixedRadio.IsChecked = _ruleSet.StopLossMode != StopLossMode.Auto;
            StopLossAutoRadio.IsChecked = _ruleSet.StopLossMode == StopLossMode.Auto;
            ReverseAfterStopLossCheckBox.IsChecked = _ruleSet.ReverseAfterStopLoss;
            MaxReverseBox.Text = _ruleSet.MaxReverseCount.ToString();
            MaxSessionBox.Text = _ruleSet.MaxSessionCount.ToString();
            SelectTrendMode(_ruleSet.TrendMode);
            MaPeriodBox.Text = _ruleSet.TrendMaPeriod.ToString();
            ForceLongRadio.IsChecked = _ruleSet.TrendForceSide == TrendForceSide.多;
            ForceShortRadio.IsChecked = _ruleSet.TrendForceSide == TrendForceSide.空;
            SameDirectionBlockMinutesBox.Text = _ruleSet.SameDirectionBlockMinutes.ToString();
            SameDirectionBlockRangeBox.Text = _ruleSet.SameDirectionBlockRange.ToString();
            DayStartBox.Text = _ruleSet.DaySessionStart.ToString(@"hh\:mm");
            DayEndBox.Text = _ruleSet.DaySessionEnd.ToString(@"hh\:mm");
            NightStartBox.Text = _ruleSet.NightSessionStart.ToString(@"hh\:mm");
            NightEndBox.Text = _ruleSet.NightSessionEnd.ToString(@"hh\:mm");
            AutoRolloverWhenHoldingCheckBox.IsChecked = _ruleSet.AutoRolloverWhenHolding;
            AutoRolloverTimeBox.Text = _ruleSet.AutoRolloverTime.ToString(@"hh\:mm");
            CloseBeforeDaySessionEndCheckBox.IsChecked = _ruleSet.CloseBeforeDaySessionEnd;
            CloseBeforeNightSessionEndCheckBox.IsChecked = _ruleSet.CloseBeforeNightSessionEnd;
            DayCloseBeforeTimeBox.Text = _ruleSet.DayCloseBeforeTime.ToString(@"hh\:mm");
            NightCloseBeforeTimeBox.Text = _ruleSet.NightCloseBeforeTime.ToString(@"hh\:mm");
            CloseBeforeLongHolidayCheckBox.IsChecked = _ruleSet.CloseBeforeLongHoliday;
            CloseBeforeLongHolidayTimeBox.Text = _ruleSet.CloseBeforeLongHolidayTime.ToString(@"hh\:mm");
            UpdateTrendOptionVisibility();
            UpdateTakeProfitOptionVisibility();
            UpdateAutoRolloverOptionVisibility();
            UpdateCloseBeforeSessionEndOptionVisibility();
            UpdateCloseBeforeLongHolidayOptionVisibility();
        }

        private void ApplyEditLocks()
        {
            if (!_lockCoreOrderInputs)
            {
                return;
            }

            KbarPeriodBox.IsEnabled = false;
            KbarPeriodBox.Opacity = 0.65;
            OrderSizeBox.IsEnabled = false;
            OrderSizeBox.Opacity = 0.65;
            MaxSessionBox.IsEnabled = false;
            MaxSessionBox.Opacity = 0.65;
            ProfitRetracePercentBox.IsEnabled = false;
            ProfitRetracePercentBox.Opacity = 0.65;
            LossRetracePercentBox.IsEnabled = false;
            LossRetracePercentBox.Opacity = 0.65;
        }

        private void OnOkClick(object sender, RoutedEventArgs e)
        {
            var orderSize = _ruleSet.OrderSize;
            if (!_lockCoreOrderInputs && !TryReadInt(OrderSizeBox, 1, int.MaxValue, out orderSize))
            {
                MessageBoxWindow.Show(this, "下單口數需為正整數。", "策略設定");
                return;
            }

            var kbar = _ruleSet.KbarPeriod;
            if (!_lockCoreOrderInputs && !TryReadInt(KbarPeriodBox, 1, 120, out kbar))
            {
                MessageBoxWindow.Show(this, "K線週期需為 1~120。", "策略設定");
                return;
            }

            var fixedTakeProfitEnabled = TakeProfitFixedRadio.IsChecked == true;
            var floatTakeProfitEnabled = TakeProfitAutoRadio.IsChecked == true;
            if (!TryReadInt(TakeProfitBox, 0, 100000, out var takeProfit))
            {
                MessageBoxWindow.Show(this, "任務總停利點數需為非負整數。", "策略設定");
                return;
            }

            if (!TryReadInt(AutoTakeProfitBox, 0, 100000, out var autoTakeProfit))
            {
                MessageBoxWindow.Show(this, "固定浮動損益停利點數需為非負整數。", "策略設定");
                return;
            }

            if (!TryReadInt(CoverLossTriggerPointsBox, 0, 100000, out var coverLossTriggerPoints))
            {
                MessageBoxWindow.Show(this, "N點需為非負整數。", "策略設定");
                return;
            }

            if (!TryReadInt(ExitOnTotalProfitRiseArmBelowPointsBox, 0, 100000, out var exitOnTotalProfitRiseArmBelowPoints))
            {
                MessageBoxWindow.Show(this, "任務總損益低於點數需為非負整數。", "策略設定");
                return;
            }

            if (!TryReadInt(ExitOnTotalProfitRisePointsBox, 0, 100000, out var exitOnTotalProfitRisePoints))
            {
                MessageBoxWindow.Show(this, "任務總損益拉回點數需為非負整數。", "策略設定");
                return;
            }

            if (!TryReadInt(ExitOnTotalProfitDropTriggerPointsBox, 0, 100000, out var exitOnTotalProfitDropTriggerPoints))
            {
                MessageBoxWindow.Show(this, "任務總損益超過點數需為非負整數。", "策略設定");
                return;
            }

            if (!TryReadInt(ExitOnTotalProfitDropExitPointsBox, 0, 100000, out var exitOnTotalProfitDropExitPoints))
            {
                MessageBoxWindow.Show(this, "任務總損益低於點數需為非負整數。", "策略設定");
                return;
            }

            if (!TryReadInt(ProfitRetraceTriggerPointsBox, 0, 100000, out var profitRetraceTriggerPoints))
            {
                MessageBoxWindow.Show(this, "獲利超過點數需為非負整數。", "策略設定");
                return;
            }

            var sharedRetraceMa = _ruleSet.LossRetracePercent > 0 ? _ruleSet.LossRetracePercent : _ruleSet.ProfitRetracePercent;
            if (!_lockCoreOrderInputs && !TryReadInt(LossRetracePercentBox, 0, 10000, out sharedRetraceMa))
            {
                MessageBoxWindow.Show(this, "停利/停損共用均線 MA 需為非負整數。", "策略設定");
                return;
            }

            if (CoverLossBeforeTakeProfitCheckBox.IsChecked == true && coverLossTriggerPoints <= 0)
            {
                MessageBoxWindow.Show(this, "啟用補足損失先平倉時，N點需大於 0。", "策略設定");
                return;
            }

            if (ExitOnTotalProfitRiseCheckBox.IsChecked == true && exitOnTotalProfitRisePoints <= 0)
            {
                MessageBoxWindow.Show(this, "啟用任務總損益低於後拉回平倉時，拉回點數需大於 0。", "策略設定");
                return;
            }

            if (ExitOnTotalProfitRiseCheckBox.IsChecked == true && exitOnTotalProfitRiseArmBelowPoints <= 0)
            {
                MessageBoxWindow.Show(this, "啟用任務總損益低於後拉回平倉時，低於點數需大於 0。", "策略設定");
                return;
            }

            var totalProfitDropAfterTriggerEnabled = ExitOnTotalProfitDropAfterTriggerCheckBox.IsChecked == true;
            if (totalProfitDropAfterTriggerEnabled)
            {
                if (exitOnTotalProfitDropTriggerPoints <= 0)
                {
                    MessageBoxWindow.Show(this, "啟用任務總損益超過後低於平倉時，超過點數需大於 0。", "策略設定");
                    return;
                }

                if (exitOnTotalProfitDropExitPoints < 0)
                {
                    MessageBoxWindow.Show(this, "啟用任務總損益超過後低於平倉時，低於點數需為非負整數。", "策略設定");
                    return;
                }

                if (exitOnTotalProfitDropExitPoints >= exitOnTotalProfitDropTriggerPoints)
                {
                    MessageBoxWindow.Show(this, "啟用任務總損益超過後低於平倉時，低於點數需小於超過點數。", "策略設定");
                    return;
                }
            }

            var profitRetraceExitEnabled = ProfitRetraceExitCheckBox.IsChecked == true;
            if (profitRetraceExitEnabled)
            {
                if (profitRetraceTriggerPoints <= 0)
                {
                    MessageBoxWindow.Show(this, "啟用停利均線平倉時，獲利超過點數需大於 0。", "策略設定");
                    return;
                }

                if (sharedRetraceMa <= 0)
                {
                    MessageBoxWindow.Show(this, "啟用停利均線平倉時，均線 MA 需大於 0。", "策略設定");
                    return;
                }
            }

            if (!TryReadInt(StopLossBox, 0, 100000, out var stopLoss))
            {
                if (StopLossFixedRadio.IsChecked == true)
                {
                    MessageBoxWindow.Show(this, "停損點數需為非負整數。", "策略設定");
                    return;
                }
                stopLoss = _ruleSet.StopLossPoints;
            }

            if (!TryReadInt(MaxReverseBox, 0, 1000, out var maxReverse))
            {
                MessageBoxWindow.Show(this, "最大反手次數需為非負整數。", "策略設定");
                return;
            }

            var absoluteStopLossEnabled = AbsoluteStopLossCheckBox.IsChecked == true;
            if (!TryReadInt(AbsoluteStopLossBox, 0, 100000, out var absoluteStopLossPoints))
            {
                MessageBoxWindow.Show(this, "絕對停損需為非負整數。", "策略設定");
                return;
            }
            if (absoluteStopLossEnabled && absoluteStopLossPoints <= 0)
            {
                MessageBoxWindow.Show(this, "啟用絕對停損時，絕對停損需大於 0。", "策略設定");
                return;
            }

            if (!TryReadInt(LossRetraceTriggerPointsBox, 0, 100000, out var lossRetraceTriggerPoints))
            {
                MessageBoxWindow.Show(this, "損失超過點數需為非負整數。", "策略設定");
                return;
            }

            var lossRetraceExitEnabled = LossRetraceExitCheckBox.IsChecked == true;
            if (lossRetraceExitEnabled)
            {
                if (lossRetraceTriggerPoints <= 0)
                {
                    MessageBoxWindow.Show(this, "啟用停損均線平倉時，損失超過點數需大於 0。", "策略設定");
                    return;
                }

                if (sharedRetraceMa <= 0)
                {
                    MessageBoxWindow.Show(this, "啟用停損均線平倉時，均線 MA 需大於 0。", "策略設定");
                    return;
                }
            }

            var maxSessions = _ruleSet.MaxSessionCount;
            if (!_lockCoreOrderInputs && !TryReadInt(MaxSessionBox, 1, 10000, out maxSessions))
            {
                MessageBoxWindow.Show(this, "最大任務數需為正整數。", "策略設定");
                return;
            }

            if (!TryReadInt(SameDirectionBlockMinutesBox, 1, 10000, out var sameDirectionBlockMinutes))
            {
                MessageBoxWindow.Show(this, "同方向限制分鐘需為正整數。", "策略設定");
                return;
            }

            if (!TryReadInt(SameDirectionBlockRangeBox, 0, 100000, out var sameDirectionBlockRange))
            {
                MessageBoxWindow.Show(this, "同方向限制範圍需為非負整數。", "策略設定");
                return;
            }

            if (!TryReadTime(DayStartBox, out var dayStart) ||
                !TryReadTime(DayEndBox, out var dayEnd))
            {
                MessageBoxWindow.Show(this, "日盤時間格式需為 HH:mm。", "策略設定");
                return;
            }

            if (!TryReadTime(NightStartBox, out var nightStart) ||
                !TryReadTime(NightEndBox, out var nightEnd))
            {
                MessageBoxWindow.Show(this, "夜盤時間格式需為 HH:mm。", "策略設定");
                return;
            }

            var autoRolloverWhenHolding = AutoRolloverWhenHoldingCheckBox.IsChecked == true;
            var autoRolloverTime = _ruleSet.AutoRolloverTime;
            if (autoRolloverWhenHolding && !TryReadTime(AutoRolloverTimeBox, out autoRolloverTime))
            {
                MessageBoxWindow.Show(this, "自動換月時間格式需為 HH:mm。", "策略設定");
                return;
            }

            var closeBeforeDaySessionEnd = CloseBeforeDaySessionEndCheckBox.IsChecked == true;
            var closeBeforeNightSessionEnd = CloseBeforeNightSessionEndCheckBox.IsChecked == true;
            var dayCloseBeforeTime = _ruleSet.DayCloseBeforeTime;
            var nightCloseBeforeTime = _ruleSet.NightCloseBeforeTime;
            if (closeBeforeDaySessionEnd)
            {
                if (!TryReadTime(DayCloseBeforeTimeBox, out dayCloseBeforeTime))
                {
                    MessageBoxWindow.Show(this, "早盤收盤前平倉時間格式需為 HH:mm。", "策略設定");
                    return;
                }
                if (dayCloseBeforeTime < new TimeSpan(8, 45, 0) || dayCloseBeforeTime > new TimeSpan(13, 45, 0))
                {
                    MessageBoxWindow.Show(this, "早盤收盤前平倉時間需在 08:45~13:45 內。", "策略設定");
                    return;
                }
            }

            if (closeBeforeNightSessionEnd)
            {
                if (!TryReadTime(NightCloseBeforeTimeBox, out nightCloseBeforeTime))
                {
                    MessageBoxWindow.Show(this, "晚盤收盤前平倉時間格式需為 HH:mm。", "策略設定");
                    return;
                }
                if (!IsNightTime(nightCloseBeforeTime, new TimeSpan(15, 0, 0), new TimeSpan(5, 0, 0)))
                {
                    MessageBoxWindow.Show(this, "晚盤收盤前平倉時間需在 15:00~05:00 內。", "策略設定");
                    return;
                }
            }

            var closeBeforeLongHoliday = CloseBeforeLongHolidayCheckBox.IsChecked == true;
            var closeBeforeLongHolidayTime = _ruleSet.CloseBeforeLongHolidayTime;
            if (closeBeforeLongHoliday)
            {
                if (!TryReadTime(CloseBeforeLongHolidayTimeBox, out closeBeforeLongHolidayTime))
                {
                    MessageBoxWindow.Show(this, "連續假日前平倉時間格式需為 HH:mm。", "策略設定");
                    return;
                }

                var min = TimeSpan.Zero;
                var max = new TimeSpan(4, 59, 0);
                if (closeBeforeLongHolidayTime < min || closeBeforeLongHolidayTime > max)
                {
                    MessageBoxWindow.Show(this, "連續假日前平倉時間需在 00:00~04:59 內。", "策略設定");
                    return;
                }
            }

            var selectedTrend = GetSelectedTrendMode();
            var takeProfitMode = floatTakeProfitEnabled
                ? TakeProfitMode.AutoAfterN
                : TakeProfitMode.FixedPoints;
            var trendMaPeriod = _ruleSet.TrendMaPeriod;
            if (selectedTrend == TrendMode.MovingAverage)
            {
                if (!TryReadInt(MaPeriodBox, 1, 10000, out trendMaPeriod))
                {
                    MessageBoxWindow.Show(this, "均線參數需為正整數。", "策略設定");
                    return;
                }
            }

            var forceSide = TrendForceSide.無;
            if (selectedTrend == TrendMode.Force)
            {
                if (ForceLongRadio.IsChecked == true)
                {
                    forceSide = TrendForceSide.多;
                }
                else if (ForceShortRadio.IsChecked == true)
                {
                    forceSide = TrendForceSide.空;
                }

                if (forceSide == TrendForceSide.無)
                {
                    MessageBoxWindow.Show(this, "強制判定需選擇多方或空方。", "策略設定");
                    return;
                }
            }

            var dayRangeStart = new TimeSpan(8, 45, 0);
            var dayRangeEnd = new TimeSpan(13, 45, 0);
            if (dayStart < dayRangeStart || dayStart > dayRangeEnd ||
                dayEnd < dayRangeStart || dayEnd > dayRangeEnd ||
                dayStart > dayEnd)
            {
                MessageBoxWindow.Show(this, "日盤時間需在 08:45~13:45 內，且起訖不可跨日。", "策略設定");
                return;
            }

            var nightRangeStart = new TimeSpan(15, 0, 0);
            var nightRangeEnd = new TimeSpan(5, 0, 0);
            if (!IsNightTime(nightStart, nightRangeStart, nightRangeEnd) ||
                !IsNightTime(nightEnd, nightRangeStart, nightRangeEnd))
            {
                MessageBoxWindow.Show(this, "夜盤時間需在 15:00~05:00 內。", "策略設定");
                return;
            }

            _ruleSet.OrderSize = orderSize;
            _ruleSet.KbarPeriod = kbar;
            _ruleSet.TakeProfitPoints = fixedTakeProfitEnabled ? takeProfit : 0;
            _ruleSet.TakeProfitMode = takeProfitMode;
            _ruleSet.AutoTakeProfitPoints = floatTakeProfitEnabled ? autoTakeProfit : 0;
            _ruleSet.CoverLossBeforeTakeProfit = CoverLossBeforeTakeProfitCheckBox.IsChecked == true;
            _ruleSet.CoverLossTriggerPoints = coverLossTriggerPoints;
            _ruleSet.ExitOnTotalProfitRise = ExitOnTotalProfitRiseCheckBox.IsChecked == true;
            _ruleSet.ExitOnTotalProfitRiseArmBelowPoints = exitOnTotalProfitRiseArmBelowPoints;
            _ruleSet.ExitOnTotalProfitRisePoints = exitOnTotalProfitRisePoints;
            _ruleSet.ExitOnTotalProfitDropAfterTrigger = totalProfitDropAfterTriggerEnabled;
            _ruleSet.ExitOnTotalProfitDropTriggerPoints = exitOnTotalProfitDropTriggerPoints;
            _ruleSet.ExitOnTotalProfitDropExitPoints = exitOnTotalProfitDropExitPoints;
            _ruleSet.ProfitRetraceExitEnabled = profitRetraceExitEnabled;
            _ruleSet.ProfitRetraceTriggerPoints = profitRetraceTriggerPoints;
            _ruleSet.ProfitRetracePercent = sharedRetraceMa;
            _ruleSet.StopLossPoints = stopLoss;
            _ruleSet.StopLossMode = StopLossAutoRadio.IsChecked == true ? StopLossMode.Auto : StopLossMode.FixedPoints;
            _ruleSet.EnableAbsoluteStopLoss = absoluteStopLossEnabled;
            _ruleSet.AbsoluteStopLossPoints = absoluteStopLossPoints;
            _ruleSet.LossRetraceExitEnabled = lossRetraceExitEnabled;
            _ruleSet.LossRetraceTriggerPoints = lossRetraceTriggerPoints;
            _ruleSet.LossRetracePercent = sharedRetraceMa;
            _ruleSet.ReverseAfterStopLoss = ReverseAfterStopLossCheckBox.IsChecked == true;
            _ruleSet.TrendMode = selectedTrend;
            _ruleSet.TrendMaPeriod = trendMaPeriod;
            _ruleSet.TrendForceSide = forceSide;
            _ruleSet.SameDirectionBlockMinutes = sameDirectionBlockMinutes;
            _ruleSet.SameDirectionBlockRange = sameDirectionBlockRange;
            _ruleSet.MaxReverseCount = maxReverse;
            _ruleSet.MaxSessionCount = maxSessions;
            _ruleSet.DaySessionStart = dayStart;
            _ruleSet.DaySessionEnd = dayEnd;
            _ruleSet.NightSessionStart = nightStart;
            _ruleSet.NightSessionEnd = nightEnd;
            _ruleSet.AutoRolloverWhenHolding = autoRolloverWhenHolding;
            _ruleSet.AutoRolloverTime = autoRolloverTime;
            _ruleSet.CloseBeforeDaySessionEnd = closeBeforeDaySessionEnd;
            _ruleSet.CloseBeforeNightSessionEnd = closeBeforeNightSessionEnd;
            _ruleSet.DayCloseBeforeTime = dayCloseBeforeTime;
            _ruleSet.NightCloseBeforeTime = nightCloseBeforeTime;
            _ruleSet.CloseBeforeLongHoliday = closeBeforeLongHoliday;
            _ruleSet.CloseBeforeLongHolidayTime = closeBeforeLongHolidayTime;

            DialogResult = true;
        }

        private void OnTrendModeChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            UpdateTrendOptionVisibility();
        }

        private void OnStopLossModeChanged(object sender, RoutedEventArgs e)
        {
            UpdateStopLossOptionVisibility();
        }

        private void OnTakeProfitModeChanged(object sender, RoutedEventArgs e)
        {
            UpdateTakeProfitOptionVisibility();
        }

        private void OnAutoRolloverChanged(object sender, RoutedEventArgs e)
        {
            UpdateAutoRolloverOptionVisibility();
        }

        private void OnCloseBeforeSessionEndChanged(object sender, RoutedEventArgs e)
        {
            UpdateCloseBeforeSessionEndOptionVisibility();
        }

        private void OnCloseBeforeLongHolidayChanged(object sender, RoutedEventArgs e)
        {
            UpdateCloseBeforeLongHolidayOptionVisibility();
        }

        private void UpdateTrendOptionVisibility()
        {
            var selected = GetSelectedTrendMode();
            var showMa = selected == TrendMode.MovingAverage;
            var showForce = selected == TrendMode.Force;

            MaRowPanel.Visibility = Visibility.Visible;
            MaPeriodBox.IsEnabled = showMa;
            MaRowPanel.Opacity = showMa ? 1.0 : 0.6;

            ForceRowPanel.Visibility = Visibility.Visible;
            ForceLongRadio.IsEnabled = showForce;
            ForceShortRadio.IsEnabled = showForce;
            ForceRowPanel.Opacity = showForce ? 1.0 : 0.6;
            UpdateStopLossOptionVisibility();
        }

        private void UpdateStopLossOptionVisibility()
        {
            var fixedMode = StopLossFixedRadio.IsChecked == true;
            StopLossBox.IsEnabled = fixedMode;
            StopLossBox.Opacity = fixedMode ? 1.0 : 0.6;

            var absoluteEnabled = AbsoluteStopLossCheckBox.IsChecked == true;
            AbsoluteStopLossBox.IsEnabled = absoluteEnabled;
            AbsoluteStopLossBox.Opacity = absoluteEnabled ? 1.0 : 0.6;

            var retraceEnabled = LossRetraceExitCheckBox.IsChecked == true;
            LossRetraceTriggerPointsBox.IsEnabled = retraceEnabled;
            LossRetraceTriggerPointsBox.Opacity = retraceEnabled ? 1.0 : 0.6;
            LossRetracePercentBox.IsEnabled = retraceEnabled;
            LossRetracePercentBox.Opacity = retraceEnabled ? 1.0 : 0.6;

            var reverseEnabled = ReverseAfterStopLossCheckBox.IsChecked == true;
            MaxReverseBox.IsEnabled = reverseEnabled;
            MaxReverseBox.Opacity = reverseEnabled ? 1.0 : 0.6;
        }

        private void UpdateTakeProfitOptionVisibility()
        {
            var fixedMode = TakeProfitFixedRadio.IsChecked == true;
            var floatMode = TakeProfitAutoRadio.IsChecked == true;
            TakeProfitBox.IsEnabled = fixedMode;
            TakeProfitBox.Opacity = fixedMode ? 1.0 : 0.6;
            AutoTakeProfitBox.IsEnabled = floatMode;
            AutoTakeProfitBox.Opacity = floatMode ? 1.0 : 0.6;

            var retraceMode = ProfitRetraceExitCheckBox.IsChecked == true;
            ProfitRetraceTriggerPointsBox.IsEnabled = retraceMode;
            ProfitRetraceTriggerPointsBox.Opacity = retraceMode ? 1.0 : 0.6;
            ProfitRetracePercentBox.IsEnabled = retraceMode;
            ProfitRetracePercentBox.Opacity = retraceMode ? 1.0 : 0.6;

            var coverLossEnabled = CoverLossBeforeTakeProfitCheckBox.IsChecked == true;
            CoverLossTriggerPointsBox.IsEnabled = coverLossEnabled;
            CoverLossTriggerPointsBox.Opacity = coverLossEnabled ? 1.0 : 0.6;

            var riseEnabled = ExitOnTotalProfitRiseCheckBox.IsChecked == true;
            ExitOnTotalProfitRiseArmBelowPointsBox.IsEnabled = riseEnabled;
            ExitOnTotalProfitRiseArmBelowPointsBox.Opacity = riseEnabled ? 1.0 : 0.6;
            ExitOnTotalProfitRisePointsBox.IsEnabled = riseEnabled;
            ExitOnTotalProfitRisePointsBox.Opacity = riseEnabled ? 1.0 : 0.6;

            var totalProfitDropEnabled = ExitOnTotalProfitDropAfterTriggerCheckBox.IsChecked == true;
            ExitOnTotalProfitDropTriggerPointsBox.IsEnabled = totalProfitDropEnabled;
            ExitOnTotalProfitDropTriggerPointsBox.Opacity = totalProfitDropEnabled ? 1.0 : 0.6;
            ExitOnTotalProfitDropExitPointsBox.IsEnabled = totalProfitDropEnabled;
            ExitOnTotalProfitDropExitPointsBox.Opacity = totalProfitDropEnabled ? 1.0 : 0.6;
        }

        private void UpdateAutoRolloverOptionVisibility()
        {
            var enabled = AutoRolloverWhenHoldingCheckBox.IsChecked == true;
            AutoRolloverTimeBox.IsEnabled = enabled;
            AutoRolloverTimeBox.Opacity = enabled ? 1.0 : 0.6;
        }

        private void UpdateCloseBeforeSessionEndOptionVisibility()
        {
            var dayEnabled = CloseBeforeDaySessionEndCheckBox.IsChecked == true;
            DayCloseBeforeTimeBox.IsEnabled = dayEnabled;
            DayCloseBeforeTimeBox.Opacity = dayEnabled ? 1.0 : 0.6;

            var nightEnabled = CloseBeforeNightSessionEndCheckBox.IsChecked == true;
            NightCloseBeforeTimeBox.IsEnabled = nightEnabled;
            NightCloseBeforeTimeBox.Opacity = nightEnabled ? 1.0 : 0.6;
        }

        private void UpdateCloseBeforeLongHolidayOptionVisibility()
        {
            var enabled = CloseBeforeLongHolidayCheckBox.IsChecked == true;
            CloseBeforeLongHolidayTimeBox.IsEnabled = enabled;
            CloseBeforeLongHolidayTimeBox.Opacity = enabled ? 1.0 : 0.6;
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void OnHelpButtonClick(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement element)
            {
                return;
            }

            var helpKey = element.Tag?.ToString() ?? string.Empty;
            var window = new HelpInfoWindow(helpKey)
            {
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.Manual
            };
            PositionHelpWindowNearCursor(window);
            window.ContentRendered += (_, _) => PositionHelpWindowNearCursor(window);
            window.Show();
        }

        private void PositionHelpWindowNearCursor(Window window)
        {
            var cursorPx = System.Windows.Forms.Cursor.Position;
            var dpi = VisualTreeHelper.GetDpi(this);
            var scaleX = dpi.DpiScaleX <= 0 ? 1.0 : dpi.DpiScaleX;
            var scaleY = dpi.DpiScaleY <= 0 ? 1.0 : dpi.DpiScaleY;

            var cursorX = cursorPx.X / scaleX;
            var cursorY = cursorPx.Y / scaleY;

            var width = window.Width;
            if (double.IsNaN(width) || width <= 0)
            {
                width = window.ActualWidth > 0 ? window.ActualWidth : 520.0;
            }

            var height = window.ActualHeight;
            if (height <= 0)
            {
                window.Measure(new Size(width, double.PositiveInfinity));
                height = window.DesiredSize.Height;
            }
            if (height <= 0)
            {
                height = 240.0;
            }

            var targetLeft = cursorX - (width / 2.0);
            var targetTop = cursorY + 12.0;

            var workAreaPx = System.Windows.Forms.Screen.FromPoint(cursorPx).WorkingArea;
            var minLeft = workAreaPx.Left / scaleX;
            var minTop = workAreaPx.Top / scaleY;
            var maxLeft = (workAreaPx.Right / scaleX) - width;
            var maxTop = (workAreaPx.Bottom / scaleY) - height;

            if (maxLeft < minLeft)
            {
                maxLeft = minLeft;
            }
            if (maxTop < minTop)
            {
                maxTop = minTop;
            }

            window.Left = Math.Max(minLeft, Math.Min(targetLeft, maxLeft));
            window.Top = Math.Max(minTop, Math.Min(targetTop, maxTop));
        }

        private static bool TryReadInt(System.Windows.Controls.TextBox box, int min, int max, out int value)
        {
            if (!int.TryParse(box.Text.Trim(), out value))
            {
                return false;
            }

            if (value < min || value > max)
            {
                return false;
            }

            return true;
        }

        private static bool TryReadTime(System.Windows.Controls.TextBox box, out TimeSpan value)
        {
            var text = box.Text.Trim();
            return TimeSpan.TryParseExact(text, @"hh\:mm", System.Globalization.CultureInfo.InvariantCulture, out value);
        }

        private static bool IsNightTime(TimeSpan time, TimeSpan nightStart, TimeSpan nightEnd)
        {
            return time >= nightStart || time <= nightEnd;
        }

        private TrendMode GetSelectedTrendMode()
        {
            if (TrendModeBox.SelectedItem is System.Windows.Controls.ComboBoxItem item && item.Tag is TrendMode mode)
            {
                return mode;
            }

            if (TrendModeBox.SelectedItem is System.Windows.Controls.ComboBoxItem contentItem)
            {
                var text = contentItem.Content?.ToString() ?? string.Empty;
                if (text.Contains("無方向"))
                {
                    return TrendMode.None;
                }
                if (text.Contains("均線"))
                {
                    return TrendMode.MovingAverage;
                }
                if (text.Contains("強制"))
                {
                    return TrendMode.Force;
                }
                if (text.Contains("自動"))
                {
                    return TrendMode.Auto;
                }
            }

            return TrendModeBox.SelectedIndex switch
            {
                0 => TrendMode.Auto,
                1 => TrendMode.None,
                2 => TrendMode.MovingAverage,
                3 => TrendMode.Force,
                _ => TrendMode.Auto
            };
        }

        private void SelectTrendMode(TrendMode mode)
        {
            foreach (var obj in TrendModeBox.Items)
            {
                if (obj is System.Windows.Controls.ComboBoxItem item && item.Tag is TrendMode taggedMode && taggedMode == mode)
                {
                    TrendModeBox.SelectedItem = item;
                    return;
                }
            }

            TrendModeBox.SelectedIndex = 0;
        }
    }
}
