using System.Windows.Controls;
using System.Windows.Input;
using SlashText.Services;

namespace SlashText.Views;

/// <summary>
/// Captures a hotkey without triggering a save during keyboard input.
/// Only accepts combinations supported by ClipboardShortcut.
/// </summary>
public sealed class ClipboardShortcutRecorderBox : TextBox
{
    private const string Prompt = "Pressione sua combinação...";
    private string _original = ClipboardShortcut.Default;

    public ClipboardShortcutRecorderBox()
    {
        IsReadOnly = true;
        Cursor = Cursors.Hand;
        ToolTip = "Clique e pressione, por exemplo, Ctrl + Alt + V. Depois clique em Salvar atalho.";
        GotKeyboardFocus += (_, _) =>
        {
            _original = Text;
            Text = Prompt;
            SelectAll();
        };
        LostKeyboardFocus += (_, _) =>
        {
            if (Text == Prompt) Text = _original;
        };
        PreviewKeyDown += CaptureKeys;
        PreviewTextInput += (_, e) => e.Handled = true;
    }

    private void CaptureKeys(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape)
        {
            Text = _original;
            Keyboard.ClearFocus();
            return;
        }
        if (key is Key.Back or Key.Delete)
        {
            Text = _original;
            Keyboard.ClearFocus();
            return;
        }
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or
            Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
            return;

        var flags = Keyboard.Modifiers;
        var names = new List<string>(4);
        if (Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin)) names.Add("Win");
        if (flags.HasFlag(ModifierKeys.Control)) names.Add("Ctrl");
        if (flags.HasFlag(ModifierKeys.Alt)) names.Add("Alt");
        if (flags.HasFlag(ModifierKeys.Shift)) names.Add("Shift");

        var name = key switch
        {
            >= Key.A and <= Key.Z => key.ToString().ToUpperInvariant(),
            >= Key.D0 and <= Key.D9 => ((int)key - (int)Key.D0).ToString(),
            >= Key.NumPad0 and <= Key.NumPad9 => ((int)key - (int)Key.NumPad0).ToString(),
            >= Key.F1 and <= Key.F12 => key.ToString().ToUpperInvariant(),
            _ => string.Empty
        };
        if (names.Count < 2 || string.IsNullOrEmpty(name)) return;
        var value = string.Join("+", names.Append(name));
        if (!ClipboardShortcut.TryParse(value, out var normalized, out _, out _))
            return;
        Text = normalized;
        CaretIndex = Text.Length;
        Keyboard.ClearFocus();
    }
}
