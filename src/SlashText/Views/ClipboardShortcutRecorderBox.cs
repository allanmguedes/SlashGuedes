using System.Windows.Controls;
using System.Windows.Input;
using SlashText.Services;

namespace SlashText.Views;

/// <summary>
/// Records a real keyboard combination, including OEM keys on ABNT2.
/// A Ctrl, Alt or Win modifier is mandatory; Shift is optional.
/// The name is normalized to Win32 virtual-key codes before persistence.
/// </summary>
public sealed class ClipboardShortcutRecorderBox : TextBox
{
    private const string Prompt = "Pressione Ctrl, Alt ou Win + uma tecla";
    private string _original = ClipboardShortcut.Default;

    public ClipboardShortcutRecorderBox()
    {
        IsReadOnly = true;
        Cursor = Cursors.Hand;
        ToolTip = "Clique e pressione sua combinação: Ctrl, Alt ou Win + qualquer tecla. Shift é opcional. Depois clique em Salvar.";
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
        var key = e.Key switch
        {
            Key.System => e.SystemKey,
            Key.ImeProcessed => e.ImeProcessedKey,
            Key.DeadCharProcessed => e.DeadCharProcessedKey,
            _ => e.Key
        };

        var flags = Keyboard.Modifiers;
        var names = new List<string>(4);
        if (Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin))
            names.Add("Win");
        if (flags.HasFlag(ModifierKeys.Control)) names.Add("Ctrl");
        if (flags.HasFlag(ModifierKeys.Alt)) names.Add("Alt");
        if (flags.HasFlag(ModifierKeys.Shift)) names.Add("Shift");

        var hasRequiredModifier = names.Any(s => s is "Ctrl" or "Alt" or "Win");
        if (key == Key.Escape && !hasRequiredModifier)
        {
            Text = _original;
            Keyboard.ClearFocus();
            return;
        }
        if (!hasRequiredModifier)
        {
            // A regular letter / Shift+letter never replaces the shortcut.
            return;
        }

        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or
            Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
            return;

        var vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        if (!ClipboardShortcut.TryDescribeKey(vk, out var keyName)) return;
        var value = string.Join("+", names.Append(keyName));
        if (!ClipboardShortcut.TryParse(value, out var normalized, out _, out _))
            return;
        Text = normalized;
        CaretIndex = Text.Length;
        Keyboard.ClearFocus();
    }
}
