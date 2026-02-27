using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using ZenPlatform.MVVM.Cs.Help;

namespace ZenPlatform;

public partial class HelpInfoWindow : Window
{
    private bool _isClosing;

    public HelpInfoWindow(string helpKey)
    {
        InitializeComponent();
        var content = HelpContentProvider.Get(helpKey);
        Title = content.Title;
        TitleText.Text = content.Title;
        BodyText.Text = content.Body;
    }

    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        Focus();
        Keyboard.Focus(this);
    }

    private void OnWindowPreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        SafeClose();
    }

    private void OnWindowPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        SafeClose();
    }

    private void OnWindowDeactivated(object? sender, System.EventArgs e)
    {
        SafeClose();
    }

    private void SafeClose()
    {
        if (_isClosing || !IsLoaded)
        {
            return;
        }

        _isClosing = true;
        try
        {
            Close();
        }
        catch (InvalidOperationException)
        {
            // Ignore re-entrant close during window teardown.
        }
    }

    protected override void OnClosed(System.EventArgs e)
    {
        base.OnClosed(e);

        if (Owner is Window owner && owner.IsVisible)
        {
            owner.Dispatcher.BeginInvoke(new System.Action(() =>
            {
                owner.Activate();
                owner.Focus();
            }), DispatcherPriority.Background);
        }
    }
}
