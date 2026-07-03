using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace BetterBarApp.Services;

public sealed record AudioDeviceInfo(string Id, string Name);

/// <summary>
/// App-wide audio access for the Audio Control item: the default render (speaker) and capture
/// (microphone) endpoints, their level/mute, per-channel peak metering, the device list, and
/// switching the default device. Built on NAudio's CoreAudioApi, plus the undocumented
/// IPolicyConfig COM interface for setting the default device (NAudio can't).
/// </summary>
public static class AudioService
{
    private static MMDeviceEnumerator? _enum;
    private static NotificationClient? _notify;

    public static AudioEndpoint Speaker    { get; private set; } = null!;
    public static AudioEndpoint Microphone { get; private set; } = null!;

    public static void Ensure()
    {
        if (_enum != null) return;
        _enum = new MMDeviceEnumerator();
        Speaker    = new AudioEndpoint(DataFlow.Render,  _enum);
        Microphone = new AudioEndpoint(DataFlow.Capture, _enum);
        _notify = new NotificationClient();
        _enum.RegisterEndpointNotificationCallback(_notify);
    }

    // IMMNotificationClient / volume callbacks run on an audio-service thread WHILE it holds internal
    // locks. Re-entering the audio API there (acquiring/releasing endpoints) deadlocks — Microsoft
    // forbids it. So these callbacks only POST work to the UI Dispatcher and return immediately; the
    // actual reacquire then runs outside the callback, on the same (UI/STA) thread that owns the
    // COM objects, and any failure is swallowed so it can never take the bar down.
    private static void Post(Action work)
    {
        var disp = System.Windows.Application.Current?.Dispatcher;
        if (disp == null) { try { work(); } catch { } return; }
        disp.BeginInvoke(() => { try { work(); } catch { } });
    }

    private static void OnDefaultChanged(DataFlow flow)
        => Post(() => { if (flow == DataFlow.Render) Speaker?.Reacquire(); else Microphone?.Reacquire(); });

    private static void OnDeviceListChanged()
        => Post(() => { Speaker?.RaiseChanged(); Microphone?.RaiseChanged(); });

    /// <summary>Soft confirmation tone (played through the default output) on speaker level release.</summary>
    public static void PlayTone()
    {
        try
        {
            var sg = new SignalGenerator(44100, 1) { Gain = 0.18, Frequency = 660, Type = SignalGeneratorType.Sin };
            var clip = new OffsetSampleProvider(sg) { TakeSamples = (int)(44100 * 0.13) };
            var output = new WaveOutEvent();
            output.Init(clip);
            output.PlaybackStopped += (_, _) => output.Dispose();
            output.Play();
        }
        catch { /* a tone failing is never worth surfacing */ }
    }

    private sealed class NotificationClient : IMMNotificationClient
    {
        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId) => OnDefaultChanged(flow);
        public void OnDeviceAdded(string pwstrDeviceId)   => OnDeviceListChanged();
        public void OnDeviceRemoved(string deviceId)       => OnDeviceListChanged();
        public void OnDeviceStateChanged(string deviceId, DeviceState newState) => OnDeviceListChanged();
        public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }
    }
}

/// <summary>One endpoint role (render = speaker, capture = microphone) tracking the current default.</summary>
public sealed class AudioEndpoint
{
    public DataFlow Flow { get; }

    /// <summary>Raised when the default device or its volume/mute changes (may fire off the UI thread).</summary>
    public event Action? Changed;

    private readonly MMDeviceEnumerator _enum;
    private readonly object _gate = new();
    private MMDevice? _device;

    // For capture (microphone) endpoints the bare IAudioMeterInformation reads zero unless something
    // is actively recording, so we open a shared WasapiCapture and compute the peak from its samples.
    // (This is why Windows shows the mic-in-use indicator while an Audio Control mic meter is visible.)
    private WasapiCapture? _capture;
    private volatile float _micPeak;
    private int _meterConsumers;   // how many visible meters need the live capture stream

    internal AudioEndpoint(DataFlow flow, MMDeviceEnumerator en)
    {
        Flow = flow;
        _enum = en;
        Reacquire();
    }

    internal void RaiseChanged() => Changed?.Invoke();

    internal void Reacquire()
    {
        lock (_gate)
        {
            if (_device != null)
            {
                StopCaptureLocked();
                try { _device.AudioEndpointVolume.OnVolumeNotification -= OnVolume; } catch { }
                try { _device.Dispose(); } catch { }
                _device = null;
            }
            AcquireLocked();
        }
        Changed?.Invoke();
    }

    // Acquire the current default endpoint if we don't have one. Caller must hold _gate.
    private void AcquireLocked()
    {
        if (_device != null) return;
        try
        {
            if (_enum.HasDefaultAudioEndpoint(Flow, Role.Multimedia))
            {
                _device = _enum.GetDefaultAudioEndpoint(Flow, Role.Multimedia);
                _device.AudioEndpointVolume.OnVolumeNotification += OnVolume;
                // Resume capture after a device change only if a meter is actually being shown.
                if (Flow == DataFlow.Capture && _meterConsumers > 0) StartCaptureLocked();
            }
        }
        catch { _device = null; }
    }

    private void OnVolume(AudioVolumeNotificationData data) => Changed?.Invoke();

    /// <summary>
    /// A visible meter is now reading this endpoint's peak. For the microphone this opens the shared
    /// capture stream (and the Windows mic-in-use indicator); for the speaker it is a no-op. Balance
    /// every call with <see cref="ReleaseMeter"/>.
    /// </summary>
    public void AcquireMeter()
    {
        lock (_gate)
        {
            _meterConsumers++;
            if (Flow == DataFlow.Capture && _capture == null && _device != null) StartCaptureLocked();
        }
    }

    /// <summary>A meter stopped reading this endpoint; closes the capture stream when none remain.</summary>
    public void ReleaseMeter()
    {
        lock (_gate)
        {
            if (--_meterConsumers > 0) return;
            _meterConsumers = 0;
            if (Flow == DataFlow.Capture) StopCaptureLocked();
        }
    }

    // Caller holds _gate. Starts a shared capture stream that drives the mic peak meter.
    private void StartCaptureLocked()
    {
        try
        {
            _capture = new WasapiCapture(_device);
            _capture.DataAvailable += OnCaptureData;
            _capture.StartRecording();
        }
        catch { _capture = null; }
    }

    // Caller holds _gate.
    private void StopCaptureLocked()
    {
        if (_capture == null) return;
        try { _capture.DataAvailable -= OnCaptureData; } catch { }
        try { _capture.StopRecording(); } catch { }
        try { _capture.Dispose(); } catch { }
        _capture = null;
        _micPeak = 0f;
    }

    // Fires on the capture thread: track the loudest sample in the buffer as the live peak.
    private void OnCaptureData(object? sender, WaveInEventArgs e)
    {
        var fmt = _capture?.WaveFormat;
        if (fmt == null) return;
        float max = 0f;
        if (fmt.Encoding == WaveFormatEncoding.IeeeFloat && fmt.BitsPerSample == 32)
        {
            for (int i = 0; i + 4 <= e.BytesRecorded; i += 4)
            {
                float v = Math.Abs(BitConverter.ToSingle(e.Buffer, i));
                if (v > max) max = v;
            }
        }
        else if (fmt.Encoding == WaveFormatEncoding.Pcm && fmt.BitsPerSample == 16)
        {
            for (int i = 0; i + 2 <= e.BytesRecorded; i += 2)
            {
                float v = Math.Abs(BitConverter.ToInt16(e.Buffer, i) / 32768f);
                if (v > max) max = v;
            }
        }
        _micPeak = Math.Min(1f, max);
    }

    public bool Available { get { lock (_gate) return _device != null; } }

    public string? DefaultId { get { lock (_gate) return _device?.ID; } }

    /// <summary>Master level, 0..1 (speaker volume / microphone recording level).</summary>
    public float Level
    {
        get { lock (_gate) { try { return _device?.AudioEndpointVolume.MasterVolumeLevelScalar ?? 0f; } catch { return 0f; } } }
        set { lock (_gate) { try { if (_device != null) _device.AudioEndpointVolume.MasterVolumeLevelScalar = Math.Clamp(value, 0f, 1f); } catch { } } }
    }

    public bool Mute
    {
        get { lock (_gate) { try { return _device?.AudioEndpointVolume.Mute ?? false; } catch { return false; } } }
        set { lock (_gate) { try { if (_device != null) _device.AudioEndpointVolume.Mute = value; } catch { } } }
    }

    /// <summary>Per-channel peak (0..1): left, right. Mono devices report the same value for both.</summary>
    public (float Left, float Right) Peaks
    {
        get
        {
            lock (_gate)
            {
                try
                {
                    if (_device == null) AcquireLocked();   // self-heal if the reference was lost
                    if (_device == null) return (0f, 0f);

                    // Microphone: the endpoint meter is flat unless something is recording, so report
                    // the peak measured from our own capture stream instead.
                    if (Flow == DataFlow.Capture) return (_micPeak, _micPeak);

                    // Speaker: prefer per-channel peaks, but some drivers leave those at zero while the
                    // master peak is live — fall back to the master so the meter still moves.
                    var meter = _device.AudioMeterInformation;
                    float master = meter.MasterPeakValue;
                    var pv = meter.PeakValues;
                    if (pv.Count >= 2 && (pv[0] > 0.0001f || pv[1] > 0.0001f)) return (pv[0], pv[1]);
                    return (master, master);
                }
                catch { return (0f, 0f); }
            }
        }
    }

    public IReadOnlyList<AudioDeviceInfo> Devices()
    {
        try
        {
            return _enum.EnumerateAudioEndPoints(Flow, DeviceState.Active)
                .Select(d => new AudioDeviceInfo(d.ID, d.FriendlyName))
                .ToList();
        }
        catch { return []; }
    }

    // Run off the UI thread: the IPolicyConfig call can be slow, and the resulting default-changed
    // notification re-acquires the endpoint asynchronously anyway. Failures are contained in PolicyConfig.
    public void SetDefault(string deviceId) =>
        System.Threading.Tasks.Task.Run(() => PolicyConfig.SetDefault(deviceId));
}

// ── Undocumented IPolicyConfig: the only way to set the default audio endpoint ──────────────────
internal static class PolicyConfig
{
    public static void SetDefault(string deviceId)
    {
        IPolicyConfig? pc = null;
        try
        {
            pc = (IPolicyConfig)new PolicyConfigClient();
            pc.SetDefaultEndpoint(deviceId, ERole.eConsole);
            pc.SetDefaultEndpoint(deviceId, ERole.eMultimedia);
            pc.SetDefaultEndpoint(deviceId, ERole.eCommunications);
        }
        catch { }
        finally { if (pc != null) Marshal.ReleaseComObject(pc); }
    }
}

internal enum ERole { eConsole = 0, eMultimedia = 1, eCommunications = 2 }

[ComImport, Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")]
internal class PolicyConfigClient { }

// Only SetDefaultEndpoint is used; the earlier methods exist only to keep the vtable slots aligned.
[ComImport, Guid("f8679f50-850a-41cf-9c72-430f290290c8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPolicyConfig
{
    [PreserveSig] int GetMixFormat();
    [PreserveSig] int GetDeviceFormat();
    [PreserveSig] int ResetDeviceFormat();
    [PreserveSig] int SetDeviceFormat();
    [PreserveSig] int GetProcessingPeriod();
    [PreserveSig] int SetProcessingPeriod();
    [PreserveSig] int GetShareMode();
    [PreserveSig] int SetShareMode();
    [PreserveSig] int GetPropertyValue();
    [PreserveSig] int SetPropertyValue();
    [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, ERole role);
    [PreserveSig] int SetEndpointVisibility();
}
