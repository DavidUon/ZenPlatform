using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace Utility
{
    /// <summary>
    /// 專供繪圖使用的 Session 線資料基底，讓業務層繼承並直接修改屬性以控制線狀態。
    /// 移至 Utility 以避免 Trading 參考 Charts。
    /// </summary>
    public class SessionScheduleLine : INotifyPropertyChanged
    {
        private string _lineId = Guid.NewGuid().ToString();
        public string LineId { get => _lineId; set { if (_lineId != value) { _lineId = value; OnPropertyChanged(); } } }

        private decimal _linePrice;
        public decimal LinePrice { get => _linePrice; set { if (_linePrice != value) { _linePrice = value; OnPropertyChanged(); } } }

        private DateTime _lineStartTime;
        public DateTime LineStartTime { get => _lineStartTime; set { if (_lineStartTime != value) { _lineStartTime = value; OnPropertyChanged(); } } }

        private DateTime? _lineEndTime;
        public DateTime? LineEndTime { get => _lineEndTime; set { if (_lineEndTime != value) { _lineEndTime = value; OnPropertyChanged(); } } }

        private string? _lineLabel;
        public string? LineLabel { get => _lineLabel; set { if (_lineLabel != value) { _lineLabel = value; OnPropertyChanged(); } } }

        private Color _lineColor = Color.FromRgb(255, 255, 255);
        public Color LineColor { get => _lineColor; set { if (_lineColor != value) { _lineColor = value; OnPropertyChanged(); } } }

        private bool _lineVisible = true;
        public bool LineVisible { get => _lineVisible; set { if (_lineVisible != value) { _lineVisible = value; OnPropertyChanged(); } } }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
