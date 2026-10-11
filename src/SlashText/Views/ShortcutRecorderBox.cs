using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using SlashText.Services;

namespace SlashText.Views;

public sealed class ShortcutRecorderBox : TextBox
{
    private const string ListeningText = "Pressione uma combinação...";
    private string _committedValue = string.Empty;
    private HwndSource? _source;

    public ShortcutRecorderBox()
    {
        IsReadOnly = true;
        Cursor = Cursors.Hand;
        MinHeight = 42;
        VerticalContentAlignment = System.Windows.VerticalAlignment.Center;
        ToolTip = UiText.Get("Clique e pressione a tecla, roda ou botão do mouse desejado");
        GotKeyboardFocus += (_, _) =>
        {
            _committedValue = Text;
            Text = ListeningText;
            SelectAll();
            _source = PresentationSource.FromVisual(this) as HwndSource;
            _source?.AddHook(WindowHook);
        };
        LostKeyboardFocus += (_, _) =>
        {
            _source?.RemoveHook(WindowHook);
            _source = null;
            if (Text == ListeningText)
            {
                Text = _committedValue;
            }
        };
        PreviewKeyDown += OnPreviewKeyDown;
        PreviewKeyUp += OnPreviewKeyUp;
        PreviewMouseDown += OnPreviewMouseDown;
        PreviewMouseWheel += OnPreviewMouseWheel;
    }

    private IntPtr WindowHook(
        IntPtr handle,
        int message,
        IntPtr wParam,
        IntPtr lParam,
        ref bool handled)
    {
        const int keyDown = 0x0100;
        const int keyUp = 0x0101;
        const int systemKeyDown = 0x0104;
        const int systemKeyUp = 0x0105;
        const int printScreen = 0x2C;
        if (message is keyDown or keyUp or systemKeyDown or systemKeyUp &&
            wParam.ToInt32() == printScreen)
        {
            handled = true;
            Dispatcher.BeginInvoke(new Action(CommitPrintScreen));
        }
        return IntPtr.Zero;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (IsPrintScreen(key))
        {
            CommitPrintScreen();
            return;
        }
        if (key == Key.Escape)
        {
            Text = _committedValue;
            Keyboard.ClearFocus();
            return;
        }
        if (key is Key.Back or Key.Delete)
        {
            Commit(string.Empty);
            return;
        }
        if (key is Key.LeftCtrl or Key.RightCtrl or
            Key.LeftAlt or Key.RightAlt or
            Key.LeftShift or Key.RightShift or
            Key.LWin or Key.RWin)
        {
            return;
        }

        var shortcut = GlobalCaptureShortcutService.FormatKeyboardShortcut(
            key,
            Keyboard.Modifiers);
        if (!string.IsNullOrWhiteSpace(shortcut))
        {
            Commit(shortcut);
        }
    }

    private void OnPreviewKeyUp(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (!IsPrintScreen(key))
        {
            return;
        }

        e.Handled = true;
        CommitPrintScreen();
    }

    private static bool IsPrintScreen(Key key) =>
        key == Key.Snapshot || KeyInterop.VirtualKeyFromKey(key) == 0x2C;

    private void CommitPrintScreen()
    {
        var shortcut = GlobalCaptureShortcutService.FormatKeyboardShortcut(
            Key.Snapshot,
            Keyboard.Modifiers);
        if (!string.IsNullOrWhiteSpace(shortcut))
        {
            Commit(shortcut);
        }
    }

    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        e.Handled = true;
        var shortcut = GlobalCaptureShortcutService.FormatWheelShortcut(
            e.Delta,
            Keyboard.Modifiers);
        if (!string.IsNullOrWhiteSpace(shortcut))
        {
            Commit(shortcut);
        }
        else
        {
            Text = UiText.Get("A roda exige Ctrl, Alt, Shift ou Win");
        }
    }

    private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton is MouseButton.Left or MouseButton.Right)
        {
            return;
        }

        e.Handled = true;
        var shortcut = GlobalCaptureShortcutService.FormatMouseShortcut(
            e.ChangedButton,
            Keyboard.Modifiers);
        if (!string.IsNullOrWhiteSpace(shortcut))
        {
            Commit(shortcut);
        }
    }

    private void Commit(string value)
    {
        _committedValue = value;
        Text = value;
        CaretIndex = Text.Length;
        Keyboard.ClearFocus();
    }
}
