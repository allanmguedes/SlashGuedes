using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using SlashText.Design;

namespace SlashText.Views;

internal sealed class ExternalOperationWindow : Window
{
    private bool _finished;
    private bool _canCancel = true;
    internal Button CancelAction { get; }
    internal ExternalOperationWindow(string message, CancellationTokenSource cancellation)
    {
        Title = UiText.Get("Processando arquivo"); Width = 440; SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var body = new StackPanel { Margin = new Thickness(24) };
        var text = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,0,0,16) };
        text.SetResourceReference(TextBlock.ForegroundProperty, "Lab.text");
        CancelAction = new Button { Content = UiText.Get("Cancelar"), MinHeight = 36, HorizontalAlignment = HorizontalAlignment.Right };
        CancelAction.SetResourceReference(StyleProperty, "Lab.Button");
        AutomationProperties.SetName(CancelAction, UiText.Get("Cancelar"));
        CancelAction.Click += (_, _) => RequestCancel();
        body.Children.Add(text); body.Children.Add(CancelAction);
        var surface = new Border { Child = body };
        surface.SetResourceReference(Border.BackgroundProperty, "Lab.panel"); Content = surface;
        SetResourceReference(BackgroundProperty, "Lab.panel"); SetResourceReference(FontFamilyProperty, "Lab.Font");
        AuxiliaryWindowTheme.Apply(this);
        Closing += (_, args) => { if (!_finished) { args.Cancel = true; RequestCancel(); } };
        void RequestCancel()
        {
            if (!_canCancel || cancellation.IsCancellationRequested) return;
            cancellation.Cancel(); CancelAction.IsEnabled = false; text.Text = UiText.Get("Cancelando. Aguarde a operação terminar com segurança.");
        }
    }
    internal void Complete() { _finished = true; Close(); }
    internal void PreventCancellation() { _canCancel = false; CancelAction.IsEnabled = false; }
}
