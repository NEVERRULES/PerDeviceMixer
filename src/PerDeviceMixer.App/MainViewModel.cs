using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using PerDeviceMixer.Core;

namespace PerDeviceMixer.App;

public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly MixerEngine _engine;
    private readonly ApplicationController _controller;
    private readonly UpdateCoordinator _updates;
    private readonly ApplicationIconProvider _iconProvider = new();
    private readonly IReadOnlyList<UpdateIntervalOption> _updateIntervals = UpdateIntervalOption.All;
    private readonly string _currentVersionText = "当前版本 " + ApplicationVersionInfo.Current;
    private readonly string _installTypeText = InstallationDetector.GetDisplayName();
    private readonly ApplicationInstallType _installType = InstallationDetector.GetInstallType();
    private string _currentDeviceName = "正在检测播放设备";
    private string _currentInputDeviceName = "正在检测录音设备";
    private string _settingsStatus = "设置会自动保存在本机";
    private double _masterVolume;
    private bool _masterMuted;
    private double _inputVolume;
    private bool _inputMuted;
    private AudioDeviceItem? _selectedOutputDevice;
    private bool _refreshing;
    private bool _settingsReady;
    private AppPage _currentPage = AppPage.Mixer;
    private bool _autoLearn = true;
    private bool _autoRestore = true;
    private bool _restoreNewSessions = true;
    private bool _saveMuteState = true;
    private bool _showDeviceSwitchToast = true;
    private bool _audioDiagnosticsEnabled;
    private double _saveDebounceMilliseconds = 500;
    private bool _startWithWindows;
    private bool _minimizeToTrayOnClose = true;
    private bool _automaticUpdateChecks = true;
    private UpdateIntervalOption _selectedUpdateInterval = UpdateIntervalOption.All[1];
    private string _updateStatus = "尚未检查更新";
    private bool _isCheckingForUpdates;
    private bool _isDownloadingUpdate;
    private int _updateDownloadProgress;
    private UpdateReleaseInfo? _availableRelease;
    private bool _disposed;

    internal MainViewModel(
        MixerEngine engine,
        ApplicationController controller,
        UpdateCoordinator updates)
    {
        _engine = engine;
        _controller = controller;
        _updates = updates;
        ShowMixerCommand = new RelayCommand(() => NavigateTo(AppPage.Mixer));
        ShowDevicesCommand = new RelayCommand(() => NavigateTo(AppPage.Devices));
        ShowSettingsCommand = new RelayCommand(() => NavigateTo(AppPage.Settings));
        SetDefaultOutputCommand = new RelayCommand<AudioDeviceItem>(SetDefaultOutputDevice);
        SetDefaultInputCommand = new RelayCommand<AudioDeviceItem>(SetDefaultInputDevice);
        OpenDevicePropertiesCommand = new RelayCommand<AudioDeviceItem>(OpenDeviceProperties);
        OpenSoundDevicesCommand = new RelayCommand(SystemSoundSettings.OpenSoundDevices);
        AddBluetoothDeviceCommand = new RelayCommand(SystemSoundSettings.OpenBluetoothDevices);
        OpenMonoAudioSettingsCommand = new RelayCommand(SystemSoundSettings.OpenMonoAudioSettings);
        CheckForUpdatesCommand = new AsyncRelayCommand(CheckForUpdatesAsync);
        DownloadAndInstallUpdateCommand = new AsyncRelayCommand(DownloadAndInstallUpdateAsync);
        OpenAvailableReleaseCommand = new RelayCommand(_controller.OpenAvailableRelease);
        OpenProjectCommand = new RelayCommand(ApplicationController.OpenProject);
        OpenReleasesCommand = new RelayCommand(ApplicationController.OpenReleases);
        OpenFeedbackCommand = new RelayCommand(_controller.ShowFeedback);
        _engine.MixerChanged += OnMixerChanged;
        _engine.ProfileSaveStateChanged += OnProfileSaveStateChanged;
        _updates.StateChanged += OnUpdateStateChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<ApplicationMixerItem> Applications { get; } = [];
    public ObservableCollection<AudioDeviceItem> OutputDevices { get; } = [];
    public ObservableCollection<AudioDeviceItem> InputDevices { get; } = [];

    public ICommand ShowMixerCommand { get; }
    public ICommand ShowDevicesCommand { get; }
    public ICommand ShowSettingsCommand { get; }
    public ICommand SetDefaultOutputCommand { get; }
    public ICommand SetDefaultInputCommand { get; }
    public ICommand OpenDevicePropertiesCommand { get; }
    public ICommand OpenSoundDevicesCommand { get; }
    public ICommand AddBluetoothDeviceCommand { get; }
    public ICommand OpenMonoAudioSettingsCommand { get; }
    public ICommand CheckForUpdatesCommand { get; }
    public ICommand DownloadAndInstallUpdateCommand { get; }
    public ICommand OpenAvailableReleaseCommand { get; }
    public ICommand OpenProjectCommand { get; }
    public ICommand OpenReleasesCommand { get; }
    public ICommand OpenFeedbackCommand { get; }

    public bool ShouldMinimizeToTray => MinimizeToTrayOnClose;
    public IReadOnlyList<UpdateIntervalOption> UpdateIntervals => _updateIntervals;
    public string CurrentVersionText => _currentVersionText;
    public string InstallTypeText => _installTypeText;

    private AppPage CurrentPage
    {
        get => _currentPage;
        set
        {
            if (!SetField(ref _currentPage, value)) return;
            OnPropertyChanged(nameof(IsMixerPage));
            OnPropertyChanged(nameof(IsDevicesPage));
            OnPropertyChanged(nameof(IsSettingsPage));
            OnPropertyChanged(nameof(MixerVisibility));
            OnPropertyChanged(nameof(DevicesVisibility));
            OnPropertyChanged(nameof(SettingsVisibility));
        }
    }

    public bool IsMixerPage => CurrentPage == AppPage.Mixer;
    public bool IsDevicesPage => CurrentPage == AppPage.Devices;
    public bool IsSettingsPage => CurrentPage == AppPage.Settings;
    public Visibility MixerVisibility => IsMixerPage ? Visibility.Visible : Visibility.Collapsed;
    public Visibility DevicesVisibility => IsDevicesPage ? Visibility.Visible : Visibility.Collapsed;
    public Visibility SettingsVisibility => IsSettingsPage ? Visibility.Visible : Visibility.Collapsed;

    public string CurrentDeviceName
    {
        get => _currentDeviceName;
        private set => SetField(ref _currentDeviceName, value);
    }

    public string CurrentInputDeviceName
    {
        get => _currentInputDeviceName;
        private set => SetField(ref _currentInputDeviceName, value);
    }

    public AudioDeviceItem? SelectedOutputDevice
    {
        get => _selectedOutputDevice;
        set
        {
            if (ReferenceEquals(_selectedOutputDevice, value)) return;
            _selectedOutputDevice = value;
            OnPropertyChanged();

            if (_refreshing || value is null || value.IsDefault) return;
            SetDefaultOutputDevice(value);
        }
    }

    public string SettingsStatus
    {
        get => _settingsStatus;
        private set => SetField(ref _settingsStatus, value);
    }

    public string ApplicationCountText => $"{Applications.Count} 个应用";

    public double MasterVolume
    {
        get => _masterVolume;
        set
        {
            if (!SetField(ref _masterVolume, Math.Clamp(value, 0, 100)) || _refreshing) return;
            _engine.SetMasterVolume((float)(_masterVolume / 100d));
        }
    }

    public bool MasterMuted
    {
        get => _masterMuted;
        set
        {
            if (!SetField(ref _masterMuted, value) || _refreshing) return;
            _engine.SetMasterVolume((float)(_masterVolume / 100d), value);
        }
    }

    public double InputVolume
    {
        get => _inputVolume;
        set
        {
            if (!SetField(ref _inputVolume, Math.Clamp(value, 0, 100)) || _refreshing) return;
            _engine.SetInputVolume((float)(_inputVolume / 100d));
        }
    }

    public bool InputMuted
    {
        get => _inputMuted;
        set
        {
            if (!SetField(ref _inputMuted, value) || _refreshing) return;
            _engine.SetInputVolume((float)(_inputVolume / 100d), value);
        }
    }

    public bool AutoLearn
    {
        get => _autoLearn;
        set
        {
            if (!SetField(ref _autoLearn, value)) return;
            SaveSetting(settings => settings.AutoLearn = value);
        }
    }

    public bool AutoRestore
    {
        get => _autoRestore;
        set
        {
            if (!SetField(ref _autoRestore, value)) return;
            SaveSetting(settings => settings.AutoRestore = value);
        }
    }

    public bool RestoreNewSessions
    {
        get => _restoreNewSessions;
        set
        {
            if (!SetField(ref _restoreNewSessions, value)) return;
            SaveSetting(settings => settings.RestoreNewSessions = value);
        }
    }

    public bool SaveMuteState
    {
        get => _saveMuteState;
        set
        {
            if (!SetField(ref _saveMuteState, value)) return;
            SaveSetting(settings => settings.SaveMuteState = value);
        }
    }

    public bool ShowDeviceSwitchToast
    {
        get => _showDeviceSwitchToast;
        set
        {
            if (!SetField(ref _showDeviceSwitchToast, value)) return;
            SaveSetting(settings => settings.ShowDeviceSwitchToast = value);
        }
    }

    public bool AudioDiagnosticsEnabled
    {
        get => _audioDiagnosticsEnabled;
        set
        {
            if (!SetField(ref _audioDiagnosticsEnabled, value)) return;
            SaveSetting(settings => settings.AudioDiagnosticsEnabled = value);
        }
    }

    public double SaveDebounceMilliseconds
    {
        get => _saveDebounceMilliseconds;
        set
        {
            var clamped = Math.Clamp(Math.Round(value / 100d) * 100d, 100d, 5000d);
            if (!SetField(ref _saveDebounceMilliseconds, clamped)) return;
            SaveSetting(settings => settings.SaveDebounceMilliseconds = (int)clamped);
        }
    }

    public bool StartWithWindows
    {
        get => _startWithWindows;
        set
        {
            if (!SetField(ref _startWithWindows, value) || !_settingsReady) return;
            try
            {
                StartupManager.SetEnabled(value);
                _engine.UpdateSettings(settings => settings.StartWithWindows = value);
                SettingsStatus = value ? "已启用开机自启动" : "已关闭开机自启动";
            }
            catch (Exception exception)
            {
                _startWithWindows = !value;
                OnPropertyChanged();
                SettingsStatus = "自启动设置失败：" + exception.Message;
            }
        }
    }

    public bool MinimizeToTrayOnClose
    {
        get => _minimizeToTrayOnClose;
        set
        {
            if (!SetField(ref _minimizeToTrayOnClose, value)) return;
            OnPropertyChanged(nameof(ShouldMinimizeToTray));
            SaveSetting(settings => settings.CloseBehavior = value
                ? CloseBehavior.MinimizeToTray
                : CloseBehavior.Exit);
        }
    }

    public bool AutomaticUpdateChecks
    {
        get => _automaticUpdateChecks;
        set
        {
            if (!SetField(ref _automaticUpdateChecks, value)) return;
            SaveSetting(settings => settings.AutomaticUpdateChecks = value);
            _updates.NotifySettingsChanged();
            OnPropertyChanged(nameof(IsUpdateIntervalEnabled));
        }
    }

    public bool IsUpdateIntervalEnabled => AutomaticUpdateChecks;

    public UpdateIntervalOption SelectedUpdateInterval
    {
        get => _selectedUpdateInterval;
        set
        {
            if (value is null || !SetField(ref _selectedUpdateInterval, value)) return;
            SaveSetting(settings => settings.UpdateCheckIntervalHours = value.Hours);
            _updates.NotifySettingsChanged();
        }
    }

    public string UpdateStatus
    {
        get => _updateStatus;
        private set => SetField(ref _updateStatus, value);
    }

    public bool IsCheckingForUpdates
    {
        get => _isCheckingForUpdates;
        private set => SetField(ref _isCheckingForUpdates, value);
    }

    public bool IsDownloadingUpdate
    {
        get => _isDownloadingUpdate;
        private set
        {
            if (!SetField(ref _isDownloadingUpdate, value)) return;
            OnPropertyChanged(nameof(CanDownloadAndInstallUpdate));
        }
    }

    public int UpdateDownloadProgress
    {
        get => _updateDownloadProgress;
        private set => SetField(ref _updateDownloadProgress, value);
    }

    public bool IsUpdateAvailable => _availableRelease is not null;
    public bool CanDownloadAndInstallUpdate =>
        IsUpdateAvailable &&
        !IsDownloadingUpdate &&
        _installType == ApplicationInstallType.Installed;
    public string UpdateActionText => _installType == ApplicationInstallType.Installed
        ? "下载并安装"
        : "便携版请打开下载页";
    public string AvailableVersionText => _availableRelease is null
        ? string.Empty
        : $"可用版本 {_availableRelease.Version}";

    public async Task InitializeAsync()
    {
        try
        {
            LoadSettings();
            RefreshSnapshot();
            RefreshDevices();
            await Task.CompletedTask;
        }
        catch (Exception exception)
        {
            CurrentDeviceName = "音频服务启动失败";
            SettingsStatus = "初始化失败：" + exception.Message;
        }
    }

    public void ToggleMasterMute() => MasterMuted = !MasterMuted;

    public void SetOutputDevice(AudioDeviceItem device) => SetDefaultOutputDevice(device);

    private void LoadSettings()
    {
        var settings = _engine.Profiles.Settings;
        _autoLearn = settings.AutoLearn;
        _autoRestore = settings.AutoRestore;
        _restoreNewSessions = settings.RestoreNewSessions;
        _saveMuteState = settings.SaveMuteState;
        _showDeviceSwitchToast = settings.ShowDeviceSwitchToast;
        _audioDiagnosticsEnabled = settings.AudioDiagnosticsEnabled;
        _saveDebounceMilliseconds = settings.SaveDebounceMilliseconds;
        _startWithWindows = StartupManager.IsEnabled;
        _minimizeToTrayOnClose = settings.CloseBehavior == CloseBehavior.MinimizeToTray;
        _automaticUpdateChecks = settings.AutomaticUpdateChecks;
        _selectedUpdateInterval = UpdateIntervalOption.All.FirstOrDefault(
            item => item.Hours == settings.UpdateCheckIntervalHours) ?? UpdateIntervalOption.All[1];
        _settingsReady = true;

        OnPropertyChanged(nameof(AutoLearn));
        OnPropertyChanged(nameof(AutoRestore));
        OnPropertyChanged(nameof(RestoreNewSessions));
        OnPropertyChanged(nameof(SaveMuteState));
        OnPropertyChanged(nameof(ShowDeviceSwitchToast));
        OnPropertyChanged(nameof(AudioDiagnosticsEnabled));
        OnPropertyChanged(nameof(SaveDebounceMilliseconds));
        OnPropertyChanged(nameof(StartWithWindows));
        OnPropertyChanged(nameof(MinimizeToTrayOnClose));
        OnPropertyChanged(nameof(ShouldMinimizeToTray));
        OnPropertyChanged(nameof(AutomaticUpdateChecks));
        OnPropertyChanged(nameof(IsUpdateIntervalEnabled));
        OnPropertyChanged(nameof(SelectedUpdateInterval));
        ApplyUpdateState(_updates.CurrentState);
    }

    private void SaveSetting(Action<MixerSettings> update)
    {
        if (!_settingsReady) return;
        try
        {
            _engine.UpdateSettings(update);
        }
        catch (Exception exception)
        {
            SettingsStatus = "设置保存失败：" + exception.Message;
        }
    }

    private void OnMixerChanged(object? sender, AudioStateChangedEventArgs eventArgs)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted) return;
        _ = dispatcher.BeginInvoke(() => ApplyAudioChange(eventArgs));
    }

    private void OnProfileSaveStateChanged(object? sender, ProfileSaveStateChangedEventArgs eventArgs)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted) return;
        _ = dispatcher.BeginInvoke(() =>
        {
            if (_disposed) return;
            SettingsStatus = eventArgs.State switch
            {
                ProfileSaveState.Pending => "正在自动保存…",
                ProfileSaveState.Saved => $"已自动保存 · {DateTime.Now:T}",
                ProfileSaveState.Failed => "自动保存失败：" + eventArgs.Exception?.Message,
                _ => SettingsStatus
            };
        });
    }

    private void OnUpdateStateChanged(object? sender, UpdateStateChangedEventArgs eventArgs)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted) return;
        _ = dispatcher.BeginInvoke(() => ApplyUpdateState(eventArgs.State));
    }

    private void ApplyUpdateState(UpdateState state)
    {
        if (_disposed) return;
        _availableRelease = state.AvailableRelease;
        UpdateStatus = state.Message;
        IsCheckingForUpdates = state.IsChecking;
        IsDownloadingUpdate = state.IsDownloading;
        UpdateDownloadProgress = state.DownloadProgress;
        OnPropertyChanged(nameof(IsUpdateAvailable));
        OnPropertyChanged(nameof(CanDownloadAndInstallUpdate));
        OnPropertyChanged(nameof(UpdateActionText));
        OnPropertyChanged(nameof(AvailableVersionText));
    }

    private async Task CheckForUpdatesAsync()
    {
        await _updates.CheckAsync(manual: true);
    }

    private async Task DownloadAndInstallUpdateAsync()
    {
        try
        {
            await _controller.DownloadAndInstallAvailableUpdateAsync();
        }
        catch (Exception exception)
        {
            UpdateStatus = "更新失败：" + exception.Message;
        }
    }

    private void ApplyAudioChange(AudioStateChangedEventArgs eventArgs)
    {
        if (_disposed) return;

        if (eventArgs.Kind == AudioChangeKind.MasterVolume &&
            eventArgs.Volume.HasValue &&
            eventArgs.IsMuted.HasValue)
        {
            UpdateMasterFromAudio(eventArgs.Volume.Value * 100d, eventArgs.IsMuted.Value);
            return;
        }

        if (eventArgs.Kind == AudioChangeKind.CaptureVolume &&
            eventArgs.Volume.HasValue &&
            eventArgs.IsMuted.HasValue)
        {
            UpdateInputFromAudio(eventArgs.Volume.Value * 100d, eventArgs.IsMuted.Value);
            return;
        }

        if (eventArgs.Kind == AudioChangeKind.SessionVolume &&
            eventArgs.ApplicationKey is not null &&
            eventArgs.Volume.HasValue &&
            eventArgs.IsMuted.HasValue)
        {
            Applications.FirstOrDefault(item => string.Equals(
                    item.ApplicationKey,
                    eventArgs.ApplicationKey,
                    StringComparison.OrdinalIgnoreCase))
                ?.UpdateFromAudio(eventArgs.Volume.Value * 100d, eventArgs.IsMuted.Value);
            return;
        }

        switch (eventArgs.Kind)
        {
            case AudioChangeKind.DefaultDevice:
                RefreshSnapshot();
                RefreshDevices();
                break;
            case AudioChangeKind.DefaultCaptureDevice:
                RefreshDevices();
                break;
            case AudioChangeKind.DeviceCollection:
                RefreshSnapshot();
                RefreshDevices();
                break;
            case AudioChangeKind.SessionCreated:
                RefreshSnapshot();
                break;
        }
    }

    private void UpdateMasterFromAudio(double volume, bool muted)
    {
        _refreshing = true;
        try
        {
            MasterVolume = volume;
            MasterMuted = muted;
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void UpdateInputFromAudio(double volume, bool muted)
    {
        _refreshing = true;
        try
        {
            InputVolume = volume;
            InputMuted = muted;
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void RefreshSnapshot()
    {
        if (_disposed) return;

        try
        {
            var snapshot = _engine.GetCurrentSnapshot();
            if (snapshot is null)
            {
                CurrentDeviceName = "没有可用的播放设备";
                Applications.Clear();
                OnPropertyChanged(nameof(ApplicationCountText));
                return;
            }

            _refreshing = true;
            CurrentDeviceName = snapshot.Endpoint.Name;
            MasterVolume = snapshot.Endpoint.MasterVolume * 100d;
            MasterMuted = snapshot.Endpoint.IsMuted;

            var incoming = snapshot.Sessions
                .GroupBy(item => item.ApplicationKey, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToDictionary(item => item.ApplicationKey, StringComparer.OrdinalIgnoreCase);

            for (var index = Applications.Count - 1; index >= 0; index--)
            {
                if (!incoming.ContainsKey(Applications[index].ApplicationKey))
                {
                    Applications.RemoveAt(index);
                }
            }

            foreach (var session in incoming.Values.OrderBy(
                         item => item.DisplayName,
                         StringComparer.CurrentCultureIgnoreCase))
            {
                var existing = Applications.FirstOrDefault(item => string.Equals(
                    item.ApplicationKey,
                    session.ApplicationKey,
                    StringComparison.OrdinalIgnoreCase));
                if (existing is null)
                {
                    Applications.Add(new ApplicationMixerItem(
                        session.ApplicationKey,
                        session.DisplayName,
                        _iconProvider.GetIcon(
                            session.ApplicationKey,
                            session.ExecutablePath,
                            session.IsSystemSounds),
                        session.Volume * 100d,
                        session.IsMuted,
                        ApplyApplicationChange));
                }
                else
                {
                    existing.UpdateFromAudio(session.Volume * 100d, session.IsMuted);
                }
            }

            OnPropertyChanged(nameof(ApplicationCountText));
        }
        catch (Exception exception)
        {
            SettingsStatus = "刷新音频状态失败：" + exception.Message;
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void RefreshDevices()
    {
        if (_disposed) return;

        try
        {
            _refreshing = true;
            ReplaceDevices(OutputDevices, _engine.GetDevices(), AudioDeviceDirection.Output);
            _selectedOutputDevice = OutputDevices.FirstOrDefault(device => device.IsDefault);
            OnPropertyChanged(nameof(SelectedOutputDevice));

            var captures = _engine.GetCaptureDevices();
            ReplaceDevices(InputDevices, captures, AudioDeviceDirection.Input);
            var currentInput = captures.FirstOrDefault(device => device.IsDefault);
            if (currentInput is null)
            {
                CurrentInputDeviceName = "没有可用的录音设备";
                InputVolume = 0;
                InputMuted = false;
            }
            else
            {
                CurrentInputDeviceName = currentInput.Name;
                InputVolume = currentInput.MasterVolume * 100d;
                InputMuted = currentInput.IsMuted;
            }
        }
        catch (Exception exception)
        {
            SettingsStatus = "刷新设备失败：" + exception.Message;
        }
        finally
        {
            _refreshing = false;
        }
    }

    private static void ReplaceDevices(
        ObservableCollection<AudioDeviceItem> target,
        IReadOnlyList<AudioEndpointInfo> devices,
        AudioDeviceDirection direction)
    {
        target.Clear();
        foreach (var device in devices.OrderByDescending(item => item.IsDefault)
                     .ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            target.Add(new AudioDeviceItem(
                device.Id,
                device.Name,
                direction,
                device.IsDefault,
                device.MasterVolume * 100d,
                device.IsMuted));
        }
    }

    private void SetDefaultOutputDevice(AudioDeviceItem device)
    {
        if (device.Direction != AudioDeviceDirection.Output || device.IsDefault) return;
        try
        {
            _engine.SetDefaultOutputDevice(device.Id);
            SettingsStatus = "默认输出已切换为 " + device.Name;
            RefreshSnapshot();
            RefreshDevices();
        }
        catch (Exception exception)
        {
            SettingsStatus = "切换输出设备失败：" + exception.Message;
            RefreshDevices();
        }
    }

    private void SetDefaultInputDevice(AudioDeviceItem device)
    {
        if (device.Direction != AudioDeviceDirection.Input || device.IsDefault) return;
        try
        {
            _engine.SetDefaultInputDevice(device.Id);
            SettingsStatus = "默认输入已切换为 " + device.Name;
            RefreshDevices();
        }
        catch (Exception exception)
        {
            SettingsStatus = "切换输入设备失败：" + exception.Message;
        }
    }

    private void OpenDeviceProperties(AudioDeviceItem device)
    {
        try
        {
            SystemSoundSettings.OpenDeviceProperties(device.Id);
        }
        catch (Exception exception)
        {
            SettingsStatus = "无法打开设备属性：" + exception.Message;
        }
    }

    private void ApplyApplicationChange(string key, double volume, bool? muted)
    {
        if (_refreshing) return;
        _engine.SetApplicationVolume(key, (float)(volume / 100d), muted);
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _engine.MixerChanged -= OnMixerChanged;
        _engine.ProfileSaveStateChanged -= OnProfileSaveStateChanged;
        _updates.StateChanged -= OnUpdateStateChanged;
        Applications.Clear();
        OutputDevices.Clear();
        InputDevices.Clear();
        _iconProvider.Dispose();
    }

    private void NavigateTo(AppPage page)
    {
        _controller.EnsurePageResources(page);
        CurrentPage = page;
    }

    internal void NavigateToSettings() => NavigateTo(AppPage.Settings);
}

public sealed record AudioDeviceItem(
    string Id,
    string Name,
    AudioDeviceDirection Direction,
    bool IsDefault,
    double Volume,
    bool IsMuted)
{
    public string StateText => IsDefault ? "默认设备" : "可用";
    public string DefaultButtonText => IsDefault ? "当前默认" : "设为默认";
    public bool CanSetDefault => !IsDefault;
    public override string ToString() => Name;
}

internal enum AppPage
{
    Mixer,
    Devices,
    Settings
}

internal sealed class RelayCommand(Action execute) : ICommand
{
    public event EventHandler? CanExecuteChanged
    {
        add { }
        remove { }
    }

    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => execute();
}

internal sealed class RelayCommand<T>(Action<T> execute) : ICommand where T : class
{
    public event EventHandler? CanExecuteChanged
    {
        add { }
        remove { }
    }

    public bool CanExecute(object? parameter) => parameter is T;

    public void Execute(object? parameter)
    {
        if (parameter is T value) execute(value);
    }
}

internal sealed class AsyncRelayCommand(Func<Task> execute) : ICommand
{
    private bool _executing;

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => !_executing;

    public async void Execute(object? parameter)
    {
        if (_executing) return;
        _executing = true;
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        try
        {
            await execute();
        }
        finally
        {
            _executing = false;
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}

public sealed class ApplicationMixerItem : INotifyPropertyChanged
{
    private readonly Action<string, double, bool?> _apply;
    private double _volume;
    private bool _isMuted;
    private bool _updatingFromAudio;

    public ApplicationMixerItem(
        string applicationKey,
        string displayName,
        BitmapSource iconSource,
        double volume,
        bool isMuted,
        Action<string, double, bool?> apply)
    {
        ApplicationKey = applicationKey;
        DisplayName = displayName;
        IconSource = iconSource;
        _volume = volume;
        _isMuted = isMuted;
        _apply = apply;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string ApplicationKey { get; }
    public string DisplayName { get; }
    public BitmapSource IconSource { get; }

    public double Volume
    {
        get => _volume;
        set
        {
            var clamped = Math.Clamp(value, 0, 100);
            if (Math.Abs(_volume - clamped) < 0.01) return;
            _volume = clamped;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Volume)));
            if (!_updatingFromAudio) _apply(ApplicationKey, _volume, null);
        }
    }

    public bool IsMuted
    {
        get => _isMuted;
        set
        {
            if (_isMuted == value) return;
            _isMuted = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsMuted)));
            if (!_updatingFromAudio) _apply(ApplicationKey, _volume, _isMuted);
        }
    }

    public void UpdateFromAudio(double volume, bool isMuted)
    {
        _updatingFromAudio = true;
        try
        {
            Volume = volume;
            IsMuted = isMuted;
        }
        finally
        {
            _updatingFromAudio = false;
        }
    }
}


public sealed record UpdateIntervalOption(int Hours, string Label)
{
    public static IReadOnlyList<UpdateIntervalOption> All { get; } =
    [
        new(6, "每 6 小时"),
        new(24, "每天"),
        new(72, "每 3 天"),
        new(168, "每 7 天")
    ];

    public override string ToString() => Label;
}
