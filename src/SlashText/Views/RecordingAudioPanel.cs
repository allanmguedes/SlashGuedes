using System.Windows;
using System.Windows.Controls;
using SlashText.Models;
using SlashText.Services;

namespace SlashText.Views;

// Shared by Capture settings and the preparation toolbar; edits remain local until saved.
internal sealed class RecordingAudioPanel : StackPanel
{
    private readonly CheckBox _computer = new() { Content = UiText.Get("Áudio do computador") };
    private readonly CheckBox _microphone = new() { Content = UiText.Get("Microfone") };
    private readonly ComboBox _output = new();
    private readonly ComboBox _input = new();
    private readonly Slider _outputVolume = new() { Minimum = 0, Maximum = 1, TickFrequency = .1 };
    private readonly Slider _inputVolume = new() { Minimum = 0, Maximum = 1, TickFrequency = .1 };
    private readonly TextBlock _message = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
    private bool _loading;
    private RecordingAudioSettings _initial;
    public event EventHandler? Changed;

    public RecordingAudioPanel(RecordingAudioSettings settings)
    {
        _initial = settings.Copy();
        AddSource(_computer, _output, _outputVolume);
        AddSource(_microphone, _input, _inputVolume);
        Children.Add(new TextBlock
        {
            Text = UiText.Get("O áudio do PC inclui os sons da saída escolhida. As fontes e dispositivos são definidos antes de iniciar. GIF não tem áudio."),
            TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 12, 0, 0)
        });
        Children.Add(_message);
        _loading = true;
        _computer.IsChecked = _initial.CaptureComputer;
        _microphone.IsChecked = _initial.CaptureMicrophone;
        _outputVolume.Value = _initial.OutputVolume;
        _inputVolume.Value = _initial.InputVolume;
        Fill(_output, [], _initial.OutputDeviceId);
        Fill(_input, [], _initial.InputDeviceId);
        _loading = false;
        UpdateEnabled();
        Loaded += async (_, _) => await RefreshDevicesAsync();
    }

    public RecordingAudioSettings ReadSettings() => new()
    {
        CaptureComputer = _computer.IsChecked == true,
        CaptureMicrophone = _microphone.IsChecked == true,
        OutputDeviceId = (_output.SelectedItem as RecordingAudioDevice)?.Id ?? _initial.OutputDeviceId,
        InputDeviceId = (_input.SelectedItem as RecordingAudioDevice)?.Id ?? _initial.InputDeviceId,
        OutputVolume = (float)_outputVolume.Value,
        InputVolume = (float)_inputVolume.Value
    };

    public void ToggleSource(bool microphone)
    {
        var source = microphone ? _microphone : _computer;
        source.IsChecked = source.IsChecked != true;
    }

    public async Task<string?> RefreshDevicesAsync()
    {
        var devices = await Task.Run(RecordingAudioDevices.Load);
        var settings = ReadSettings();
        _loading = true;
        Fill(_input, devices.Inputs.Devices, settings.InputDeviceId);
        Fill(_output, devices.Outputs.Devices, settings.OutputDeviceId);
        _loading = false;
        var error = devices.Validate(settings);
        _message.Text = error ?? string.Empty;
        return error;
    }

    private void AddSource(CheckBox toggle, ComboBox devices, Slider volume)
    {
        toggle.Style = (Style)Application.Current.FindResource("Lab.Switch");
        toggle.Margin = new Thickness(0, 14, 0, 8);
        devices.Style = (Style)Application.Current.FindResource("Lab.Combo");
        devices.DisplayMemberPath = nameof(RecordingAudioDevice.Name);
        devices.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        volume.Margin = new Thickness(0, 6, 0, 0);
        volume.ToolTip = UiText.Get("Volume na gravação: 0 a 100%. Não altera o volume do Windows.");
        Children.Add(toggle);
        Children.Add(devices);
        Children.Add(new TextBlock { Text = UiText.Get("Volume na gravação"), FontSize = 12, Margin = new Thickness(0, 10, 0, 0) });
        Children.Add(volume);
        toggle.Checked += OnChanged;
        toggle.Unchecked += OnChanged;
        devices.SelectionChanged += OnChanged;
        volume.ValueChanged += OnChanged;
    }

    private static void Fill(ComboBox box, IReadOnlyList<RecordingAudioDevice> devices, string selected)
    {
        box.Items.Clear();
        box.Items.Add(new RecordingAudioDevice(string.Empty, UiText.Get("Padrão do Windows")));
        foreach (var device in devices) box.Items.Add(device);
        if (!string.IsNullOrEmpty(selected) && !devices.Any(device => device.Id == selected))
            box.Items.Add(new RecordingAudioDevice(selected, UiText.Get("Dispositivo indisponível — escolha outro")));
        box.SelectedItem = box.Items.Cast<RecordingAudioDevice>().First(device => device.Id == selected);
    }

    private void OnChanged(object sender, RoutedEventArgs args)
    {
        if (_loading) return;
        UpdateEnabled();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateEnabled()
    {
        _output.IsEnabled = _outputVolume.IsEnabled = _computer.IsChecked == true;
        _input.IsEnabled = _inputVolume.IsEnabled = _microphone.IsChecked == true;
    }
}
