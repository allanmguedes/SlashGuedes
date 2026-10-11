using System.ComponentModel;
using System.Runtime.InteropServices;
using SlashText.Models;
using WpfClipboard = System.Windows.Clipboard;
using WpfDataFormats = System.Windows.DataFormats;
using WpfDataObject = System.Windows.DataObject;
using WpfIDataObject = System.Windows.IDataObject;

namespace SlashText.Services;

public sealed class TextExpansionService
{
    private const uint InputKeyboard = 1;
    private const uint KeyEventKeyUp = 0x0002;
    private const uint KeyEventUnicode = 0x0004;
    private const ushort VkBack = 0x08;
    private const ushort VkTab = 0x09;
    private const ushort VkControl = 0x11;
    private const ushort VkV = 0x56;
    private const uint GaRoot = 2;
    private const string ClipboardMarkerFormat = "SlashDesk.Expansion.Marker";
    private readonly TemplateEngine _templateEngine = new();
    private readonly SingleFlightGate _singleFlight = new();

    public bool IsExpanding => _singleFlight.IsActive;

    public IReadOnlyList<TemplateField> GetFillableFields(Snippet snippet) =>
        _templateEngine.GetFillableFields(snippet.Content, literalCodeBlocks: snippet.Format == SnippetFormat.Markdown);

    public Task<int> ExpandAsync(
        Snippet snippet,
        IReadOnlyDictionary<string, string> values,
        IntPtr targetWindow) =>
        ExpandAsync(snippet, values, targetWindow, snippet.Trigger.Length, CancellationToken.None);

    public async Task<int> ExpandAsync(
        Snippet snippet,
        IReadOnlyDictionary<string, string> values,
        IntPtr targetWindow,
        int typedCharacterCount,
        CancellationToken cancellationToken)
    {
        using var lease = _singleFlight.TryEnter();
        if (lease is null)
        {
            throw new ExpansionBusyException();
        }

        var rendered = _templateEngine.Render(snippet.Content, values, literalCodeBlocks: snippet.Format == SnippetFormat.Markdown);
        var plan = ExpansionPlan.Create(rendered);
        var insertedCharacters = plan.Sum(step =>
            snippet.Format == SnippetFormat.Markdown
                ? RichTextMarkdownConverter.ToPlainText(step.Segment).Length
                : step.Segment.Length);
        var useClipboard = snippet.Format == SnippetFormat.Markdown ||
                           plan.Any(step => RequiresClipboard(step.Segment));
        WpfIDataObject? previousClipboard = null;
        var marker = Guid.NewGuid().ToString("N");
        SafeDiagnosticLog.Write("expansion.plan", new Dictionary<string, object?>
        {
            ["segmentCount"] = plan.Count,
            ["usesClipboard"] = useClipboard,
            ["typedCharacterCount"] = typedCharacterCount
        });

        try
        {
            await RecoverTargetAsync(targetWindow, cancellationToken);
            if (useClipboard)
            {
                previousClipboard = await CaptureClipboardAsync(cancellationToken);
            }

            await RequireTargetAsync(targetWindow, cancellationToken);
            SendBackspaces(Math.Max(0, typedCharacterCount));
            await WaitForTargetAsync(targetWindow, cancellationToken);

            for (var index = 0; index < plan.Count; index++)
            {
                var step = plan[index];
                SafeDiagnosticLog.Write("expansion.segment", new Dictionary<string, object?>
                {
                    ["segmentIndex"] = index,
                    ["sendTab"] = step.SendTabAfter
                });
                await RequireTargetAsync(targetWindow, cancellationToken);
                if (useClipboard)
                {
                    await SetClipboardSegmentAsync(step.Segment, snippet.Format, marker, cancellationToken);
                    await RequireTargetAsync(targetWindow, cancellationToken);
                    SendPaste();
                }
                else
                {
                    SendUnicode(step.Segment);
                }

                await WaitForTargetAsync(targetWindow, cancellationToken);
                if (step.SendTabAfter)
                {
                    await RequireTargetAsync(targetWindow, cancellationToken);
                    SendKey(VkTab);
                    SafeDiagnosticLog.Write("expansion.tab_sent", new Dictionary<string, object?>
                    {
                        ["segmentIndex"] = index
                    });
                    await WaitForTargetAsync(targetWindow, cancellationToken);
                }
            }
            return insertedCharacters;
        }
        finally
        {
            if (previousClipboard is not null)
            {
                await TryRestoreClipboardAsync(previousClipboard, marker);
            }
        }
    }

    private static bool RequiresClipboard(string value) =>
        value.Length > 2000 || value.Contains('\r') || value.Contains('\n');

    private static async Task RecoverTargetAsync(IntPtr targetWindow, CancellationToken cancellationToken)
    {
        ValidateTarget(targetWindow);
        if (IsTargetForeground(targetWindow))
        {
            return;
        }
        _ = SetForegroundWindow(targetWindow);
        for (var attempt = 0; attempt < 20; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsTargetForeground(targetWindow))
            {
                return;
            }
            await Task.Delay(25, cancellationToken);
        }
        throw new InvalidOperationException(UiText.Get("Não foi possível devolver o foco ao aplicativo onde o atalho foi digitado."));
    }

    private static Task RequireTargetAsync(IntPtr targetWindow, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateTarget(targetWindow);
        if (!IsTargetForeground(targetWindow))
        {
            throw new OperationCanceledException(
                UiText.Get("A expansão foi interrompida porque o usuário mudou de aplicativo."),
                cancellationToken);
        }
        return Task.CompletedTask;
    }

    private static void ValidateTarget(IntPtr targetWindow)
    {
        if (targetWindow == IntPtr.Zero || !IsWindow(targetWindow))
        {
            throw new InvalidOperationException(UiText.Get("A janela de destino não está mais disponível."));
        }
        _ = GetWindowThreadProcessId(targetWindow, out var processId);
        if (processId == Environment.ProcessId)
        {
            throw new InvalidOperationException(UiText.Get("O SlashDesk não envia expansões para suas próprias janelas."));
        }
    }

    private static bool IsTargetForeground(IntPtr targetWindow)
    {
        var foreground = GetForegroundWindow();
        return foreground == targetWindow ||
               (foreground != IntPtr.Zero && GetAncestor(foreground, GaRoot) == GetAncestor(targetWindow, GaRoot));
    }

    private static async Task WaitForTargetAsync(IntPtr targetWindow, CancellationToken cancellationToken)
    {
        await Task.Delay(35, cancellationToken);
        _ = SendMessageTimeout(targetWindow, 0, IntPtr.Zero, IntPtr.Zero, 0x0002, 350, out _);
        await RequireTargetAsync(targetWindow, cancellationToken);
    }

    private static async Task<WpfIDataObject?> CaptureClipboardAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 8; attempt++)
        {
            try
            {
                var source = WpfClipboard.GetDataObject();
                if (source is null)
                {
                    return null;
                }
                var copy = new WpfDataObject();
                foreach (var format in source.GetFormats(autoConvert: false))
                {
                    try
                    {
                        copy.SetData(format, source.GetData(format, autoConvert: false));
                    }
                    catch (Exception) when (attempt < 7)
                    {
                        // Um formato atrasado pode desaparecer entre a enumeração e a cópia.
                    }
                }
                return copy;
            }
            catch (ExternalException) when (attempt < 7)
            {
                await Task.Delay(20 + (attempt * 15), cancellationToken);
            }
        }
        return null;
    }

    private static async Task SetClipboardSegmentAsync(
        string value,
        SnippetFormat format,
        string marker,
        CancellationToken cancellationToken)
    {
        var plain = format == SnippetFormat.Markdown
            ? RichTextMarkdownConverter.ToPlainText(value)
            : value;
        var data = new WpfDataObject();
        data.SetData(WpfDataFormats.UnicodeText, plain);
        data.SetData(WpfDataFormats.Text, plain);
        data.SetData(ClipboardMarkerFormat, marker);
        if (format == SnippetFormat.Markdown)
        {
            data.SetData(WpfDataFormats.Html, HtmlClipboardFormatter.Create(RichTextMarkdownConverter.ToHtml(value)));
        }
        await SetClipboardAsync(data, cancellationToken);
    }

    private static async Task SetClipboardAsync(WpfIDataObject data, CancellationToken cancellationToken)
    {
        ExternalException? lastException = null;
        for (var attempt = 0; attempt < 8; attempt++)
        {
            try
            {
                WpfClipboard.SetDataObject(data, true);
                return;
            }
            catch (ExternalException exception)
            {
                lastException = exception;
                await Task.Delay(20 + (attempt * 15), cancellationToken);
            }
        }
        throw new InvalidOperationException(UiText.Get("A área de transferência está ocupada. Tente novamente."), lastException);
    }

    private static async Task TryRestoreClipboardAsync(WpfIDataObject data, string marker)
    {
        try
        {
            var current = WpfClipboard.GetDataObject();
            if (!string.Equals(current?.GetData(ClipboardMarkerFormat, false) as string, marker, StringComparison.Ordinal))
            {
                return;
            }
            await SetClipboardAsync(data, CancellationToken.None);
        }
        catch (ExternalException)
        {
            // Não sobrescreva um clipboard que outro processo passou a controlar.
        }
    }

    private static void SendBackspaces(int count)
    {
        if (count == 0) return;
        var inputs = new Input[count * 2];
        for (var index = 0; index < count; index++)
        {
            inputs[index * 2] = KeyboardInput(VkBack, false);
            inputs[(index * 2) + 1] = KeyboardInput(VkBack, true);
        }
        Send(inputs);
    }

    private static void SendUnicode(string value)
    {
        foreach (var character in value)
        {
            Send([UnicodeInput(character, false), UnicodeInput(character, true)]);
        }
    }

    private static void SendPaste() => Send([
        KeyboardInput(VkControl, false), KeyboardInput(VkV, false),
        KeyboardInput(VkV, true), KeyboardInput(VkControl, true)]);

    private static void SendKey(ushort key) => Send([KeyboardInput(key, false), KeyboardInput(key, true)]);

    private static Input KeyboardInput(ushort key, bool keyUp) => new()
    {
        Type = InputKeyboard,
        Data = new InputUnion { Keyboard = new KeyboardInputData
        {
            VirtualKey = key,
            Flags = keyUp ? KeyEventKeyUp : 0,
            ExtraInfo = GetMessageExtraInfo()
        }}
    };

    private static Input UnicodeInput(char value, bool keyUp) => new()
    {
        Type = InputKeyboard,
        Data = new InputUnion { Keyboard = new KeyboardInputData
        {
            ScanCode = value,
            Flags = KeyEventUnicode | (keyUp ? KeyEventKeyUp : 0),
            ExtraInfo = GetMessageExtraInfo()
        }}
    };

    private static void Send(Input[] inputs)
    {
        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
        if (sent != inputs.Length)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), UiText.Get("O Windows não permitiu inserir o texto no aplicativo atual."));
        }
    }

    [StructLayout(LayoutKind.Sequential)] private struct Input { public uint Type; public InputUnion Data; }
    [StructLayout(LayoutKind.Explicit)] private struct InputUnion
    {
        [FieldOffset(0)] public MouseInputData Mouse;
        [FieldOffset(0)] public KeyboardInputData Keyboard;
        [FieldOffset(0)] public HardwareInputData Hardware;
    }
    [StructLayout(LayoutKind.Sequential)] private struct MouseInputData { public int X; public int Y; public uint MouseData; public uint Flags; public uint Time; public UIntPtr ExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] private struct KeyboardInputData { public ushort VirtualKey; public ushort ScanCode; public uint Flags; public uint Time; public UIntPtr ExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] private struct HardwareInputData { public uint Message; public ushort ParameterLow; public ushort ParameterHigh; }

    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint inputCount, Input[] inputs, int inputSize);
    [DllImport("user32.dll")] private static extern UIntPtr GetMessageExtraInfo();
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window, uint flags);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SendMessageTimeout(
        IntPtr window, uint message, IntPtr wParam, IntPtr lParam,
        uint flags, uint timeout, out IntPtr result);
}

public sealed class ExpansionBusyException() : InvalidOperationException(
    UiText.Get("Já existe uma expansão em andamento. Aguarde a conclusão e tente novamente."));
