using System;

namespace ZenPlatform.Strategy
{
    public sealed class Session : RuleBase, System.ComponentModel.INotifyPropertyChanged
    {
        public int Id { get; set; }
        private DateTime _startTime;
        private int _position;
        private int _startPosition;
        private decimal _avgEntryPrice;
        private decimal _floatProfit;
        private decimal _realizedProfit;
        private int _tradeCount;

        public DateTime StartTime
        {
            get => _startTime;
            set
            {
                if (_startTime == value) return;
                _startTime = value;
                OnPropertyChanged(nameof(StartTime));
            }
        }

        public override bool IsFinished
        {
            get => base.IsFinished;
            protected set
            {
                if (base.IsFinished == value) return;
                base.IsFinished = value;
                OnPropertyChanged(nameof(IsFinished));
            }
        }

        public int Position
        {
            get => _position;
            set
            {
                if (_position == value) return;
                _position = value;
                OnPropertyChanged(nameof(Position));
            }
        }

        public int StartPosition
        {
            get => _startPosition;
            set
            {
                if (_startPosition == value) return;
                _startPosition = value;
                OnPropertyChanged(nameof(StartPosition));
            }
        }

        public decimal AvgEntryPrice
        {
            get => _avgEntryPrice;
            set
            {
                if (_avgEntryPrice == value) return;
                _avgEntryPrice = value;
                OnPropertyChanged(nameof(AvgEntryPrice));
            }
        }

        public decimal FloatProfit
        {
            get => _floatProfit;
            set
            {
                if (_floatProfit == value) return;
                _floatProfit = value;
                OnPropertyChanged(nameof(FloatProfit));
                OnPropertyChanged(nameof(TotalProfit));
            }
        }

        public decimal RealizedProfit
        {
            get => _realizedProfit;
            set
            {
                if (_realizedProfit == value) return;
                _realizedProfit = value;
                OnPropertyChanged(nameof(RealizedProfit));
                OnPropertyChanged(nameof(TotalProfit));
            }
        }

        public int TradeCount
        {
            get => _tradeCount;
            set
            {
                if (_tradeCount == value) return;
                _tradeCount = value;
                OnPropertyChanged(nameof(TradeCount));
            }
        }

        public decimal TotalProfit => FloatProfit + RealizedProfit;

        public void Start(bool isBuy)
        {
            StartTime = DateTime.Now;
            StartPosition = isBuy ? 1 : -1;
            Position = StartPosition;
            SetFinished(false);
            var qty = Manager?.RuleSet.OrderSize ?? 1;
            Trade(isBuy, qty, isBuy ? "使用者建立多單任務" : "使用者建立空單任務");
        }

        public void CloseAll()
        {
            var totalKou = PositionManager.TotalKou;
            if (totalKou == 0)
            {
                SetFinished(true);
                return;
            }

            var isBuy = totalKou < 0;
            var qty = Math.Abs(totalKou);
            Trade(isBuy, qty, "任務平倉且結束");
            SetFinished(true);
        }

        internal void RestoreFinished(bool isFinished)
        {
            SetFinished(isFinished);
        }

        public override void OnTick()
        {
            base.OnTick();
        }

        public override void OnMinute()
        {
            // Default session has no per-minute behavior.
        }


        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged(string propertyName)
        {
            // Ensure PropertyChanged is invoked on the UI thread
            if (System.Windows.Application.Current != null && System.Windows.Application.Current.Dispatcher != null)
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
                });
            }
            else
            {
                // Fallback for cases where Dispatcher is not available (e.g., unit tests or shutdown)
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
            }
        }
    }
}
