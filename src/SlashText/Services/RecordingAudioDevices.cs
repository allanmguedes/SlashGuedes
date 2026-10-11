using ScreenRecorderLib;
using SlashText.Models;

namespace SlashText.Services;

internal sealed record RecordingAudioDevice(string Id, string Name);

internal sealed record RecordingAudioDeviceList(IReadOnlyList<RecordingAudioDevice> Devices, string? Error)
{
    public static RecordingAudioDeviceList Load(AudioDeviceSource source)
    {
        try
        {
            return new(Recorder.GetSystemAudioDevices(source)
                .Select(device => new RecordingAudioDevice(device.DeviceName, device.FriendlyName)).ToArray(), null);
        }
        catch (Exception exception)
        {
            AppDiagnosticLog.Write("recording.audio-devices-failed", ("source", source), ("errorType", exception.GetType().Name));
            return new([], UiText.Get("Não foi possível consultar os dispositivos de áudio."));
        }
    }

    public string? Validate(bool enabled, string id, string label)
    {
        if (!enabled) return null;
        if (Error is not null) return Error + " Desative " + label + " ou tente novamente.";
        if (Devices.Count == 0) return UiText.Get("Nenhum dispositivo disponível para ") + label + UiText.Get(". Conecte um dispositivo ou desligue essa fonte.");
        if (!string.IsNullOrEmpty(id) && !Devices.Any(device => device.Id == id))
            return "O dispositivo escolhido para " + label + UiText.Get(" não está disponível. Escolha outro dispositivo ou o padrão do Windows.");
        return null;
    }
}

internal sealed record RecordingAudioDevices(RecordingAudioDeviceList Inputs, RecordingAudioDeviceList Outputs)
{
    public static RecordingAudioDevices Load() => new(
        RecordingAudioDeviceList.Load(AudioDeviceSource.InputDevices),
        RecordingAudioDeviceList.Load(AudioDeviceSource.OutputDevices));

    public string? Validate(RecordingAudioSettings audio) =>
        Inputs.Validate(audio.CaptureMicrophone, audio.InputDeviceId, "o microfone") ??
        Outputs.Validate(audio.CaptureComputer, audio.OutputDeviceId, UiText.Get("o áudio do PC"));
}
