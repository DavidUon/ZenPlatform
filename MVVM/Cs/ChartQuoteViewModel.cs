using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace ZenPlatform.MVVM.Cs
{
    public sealed class ChartQuoteViewModel : INotifyPropertyChanged
    {
        private string _bidText = "買進:---";
        private Brush _bidBrush = Brushes.White;
        private string _askText = "賣出:---";
        private Brush _askBrush = Brushes.White;
        private string _lastText = "成交:---";
        private Brush _lastBrush = Brushes.White;
        private string _volText = "量:---";
        private Brush _volBrush = Brushes.White;
        private string _changeText = "漲跌:---";
        private Brush _changeBrush = Brushes.White;

        public event PropertyChangedEventHandler? PropertyChanged;

        public string BidText
        {
            get => _bidText;
            set => SetField(ref _bidText, value);
        }

        public Brush BidBrush
        {
            get => _bidBrush;
            set => SetField(ref _bidBrush, value);
        }

        public string AskText
        {
            get => _askText;
            set => SetField(ref _askText, value);
        }

        public Brush AskBrush
        {
            get => _askBrush;
            set => SetField(ref _askBrush, value);
        }

        public string LastText
        {
            get => _lastText;
            set => SetField(ref _lastText, value);
        }

        public Brush LastBrush
        {
            get => _lastBrush;
            set => SetField(ref _lastBrush, value);
        }

        public string VolumeText
        {
            get => _volText;
            set => SetField(ref _volText, value);
        }

        public Brush VolumeBrush
        {
            get => _volBrush;
            set => SetField(ref _volBrush, value);
        }

        public string ChangeText
        {
            get => _changeText;
            set => SetField(ref _changeText, value);
        }

        public Brush ChangeBrush
        {
            get => _changeBrush;
            set => SetField(ref _changeBrush, value);
        }

        private void SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (Equals(field, value))
            {
                return;
            }

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
