using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Chiko.WirelessControl.App.Services;
using Microsoft.Maui.Controls;

namespace Chiko.WirelessControl.App.ViewModels;

public sealed class GeneralSettingsViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly MainPageViewModel? _source;
    private readonly SemaphoreSlim _busyLock = new(1, 1);

    private readonly Command _clearInitialAirVolumeCommand;
    private readonly Command _manualPulseCommand;
    private readonly Command _resetSettingValueCommand;

    private bool _isApplying;
    private bool _initialized;

    private int? _lastCommittedVolumeDownRate;
    private int? _lastCommittedPulseInterval;
    private int? _lastCommittedShakingInterval;
    private int? _lastCommittedShakingOperating;
    private int? _lastCommittedRemoteOutput;
    private bool? _lastCommittedAutoPulse;

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action<string>? LogRequested;
    public event Func<string, string, Task>? ShowAlertRequested;

    public IReadOnlyList<int> ShakingIntervalOptions { get; } = new[] { 0, 5, 10, 15, 20, 25, 30, 35, 40, 45, 50, 55, 60 };
    public IReadOnlyList<int> ShakingOperatingTimeOptions { get; } = new[]
    {
        20, 25, 30, 35, 40, 45, 50, 55, 60,
        65, 70, 75, 80, 85, 90, 95, 100, 105,
        110, 115, 120, 125, 130, 135, 140, 145,
        150, 155, 160, 165, 170, 175, 180
    };

    public IReadOnlyList<string> RemoteOutputSignalOptions { get; } = new[]
    {
        "AIR VOLUME",
        "OP",
        "SP",
        "DP",
        "EP"
    };

    public ICommand ClearInitialAirVolumeCommand => _clearInitialAirVolumeCommand;
    public ICommand ManualPulseCommand => _manualPulseCommand;
    public ICommand ResetSettingValueCommand => _resetSettingValueCommand;

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value))
                return;

            _clearInitialAirVolumeCommand.ChangeCanExecute();
            _manualPulseCommand.ChangeCanExecute();
            _resetSettingValueCommand.ChangeCanExecute();
        }
    }

    public bool IsApplying => _isApplying;

    private string _statusMessage = "";
    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    private string _volumeDownRate = "100";
    public string VolumeDownRate
    {
        get => _volumeDownRate;
        set => SetProperty(ref _volumeDownRate, value);
    }

    private string _initialAirVolume = "0.00";
    public string InitialAirVolume
    {
        get => _initialAirVolume;
        set => SetProperty(ref _initialAirVolume, value);
    }

    private string _airVolumeReductionThreshold = "0.00";
    public string AirVolumeReductionThreshold
    {
        get => _airVolumeReductionThreshold;
        set => SetProperty(ref _airVolumeReductionThreshold, value);
    }

    private int _shakingTimeInterval;
    public int ShakingTimeInterval
    {
        get => _shakingTimeInterval;
        set => SetProperty(ref _shakingTimeInterval, value);
    }

    private int _shakingOperatingTime = 20;
    public int ShakingOperatingTime
    {
        get => _shakingOperatingTime;
        set => SetProperty(ref _shakingOperatingTime, value);
    }

    private int _pulseIntervalSetting;
    public int PulseIntervalSetting
    {
        get => _pulseIntervalSetting;
        set => SetProperty(ref _pulseIntervalSetting, Math.Max(0, value));
    }

    private bool _isAutoPulseEnabled;
    public bool IsAutoPulseEnabled
    {
        get => _isAutoPulseEnabled;
        set => SetProperty(ref _isAutoPulseEnabled, value);
    }

    private string _selectedRemoteOutputSignal = "AIR VOLUME";
    public string SelectedRemoteOutputSignal
    {
        get => _selectedRemoteOutputSignal;
        set => SetProperty(ref _selectedRemoteOutputSignal, value);
    }

    private bool _isShakingAvailable;
    public bool IsShakingAvailable
    {
        get => _isShakingAvailable;
        private set => SetProperty(ref _isShakingAvailable, value);
    }

    private bool _isPulseAvailable;
    public bool IsPulseAvailable
    {
        get => _isPulseAvailable;
        private set => SetProperty(ref _isPulseAvailable, value);
    }

    private string _productName = "-";
    public string ProductName
    {
        get => _productName;
        private set => SetProperty(ref _productName, value);
    }

    private string _serialNumber = "-";
    public string SerialNumber
    {
        get => _serialNumber;
        private set => SetProperty(ref _serialNumber, value);
    }

    private string _programVersion = "-";
    public string ProgramVersion
    {
        get => _programVersion;
        private set => SetProperty(ref _programVersion, value);
    }

    public GeneralSettingsViewModel(MainPageViewModel? source)
    {
        _source = source;

        _clearInitialAirVolumeCommand = new Command(async () => await ClearInitialAirVolumeAsync(), () => !IsBusy);
        _manualPulseCommand = new Command(async () => await ManualPulseAsync(), () => !IsBusy);
        _resetSettingValueCommand = new Command(async () => await ResetSettingValuesAsync(), () => !IsBusy);
    }

    public async Task LoadOnOpenAsync()
    {
        if (_initialized)
            return;

        _initialized = true;
        await ReloadAllAsync();
    }

    public async Task ReloadAllAsync()
    {
        if (_source is null)
        {
            await NotifyAlertAsync("Load Failed", "Main page context is not available.");
            return;
        }

        await _busyLock.WaitAsync();
        try
        {
            if (IsBusy)
                return;

            IsBusy = true;
            StatusMessage = "Loading settings...";

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            var pageData = await _source.LoadGeneralSettingsPageDataAsync(cts.Token);

            ApplyLoadedData(pageData);

            StatusMessage = "Settings loaded.";
            LogRequested?.Invoke("[GENERAL] settings loaded (S7+S5)");
        }
        catch (Exception ex)
        {
            StatusMessage = "Failed to load settings.";
            LogRequested?.Invoke("[GENERAL] load error: " + ex);
            await NotifyAlertAsync("Load Failed", ex.Message);
        }
        finally
        {
            IsBusy = false;
            _busyLock.Release();
        }
    }

    public async Task CommitVolumeDownRateAsync()
    {
        if (_isApplying)
            return;

        if (!TryParseVolumeDownRate(out var value, out var err))
        {
            await NotifyAlertAsync("Input Error", err);
            return;
        }

        if (_lastCommittedVolumeDownRate == value)
            return;

        await CommitAsync(async ct =>
        {
            await _source!.SaveVolumeDownRateAsync(value, ct);
            _lastCommittedVolumeDownRate = value;
        }, "VOLUME DOWN RATE saved.", "VOLUME DOWN RATE save failed.");
    }

    public async Task CommitRemoteOutputSignalAsync()
    {
        if (_isApplying)
            return;

        int code = ToRemoteOutputCode(SelectedRemoteOutputSignal);
        if (_lastCommittedRemoteOutput == code)
            return;

        await CommitAsync(async ct =>
        {
            await _source!.SaveRemoteOutputSignalAsync(code, ct);
            _lastCommittedRemoteOutput = code;
        }, "REMOTE OUTPUT saved.", "REMOTE OUTPUT save failed.");
    }

    public async Task CommitShakingIntervalAsync()
    {
        if (_isApplying || !IsShakingAvailable)
            return;

        int value = ShakingTimeInterval;
        if (value < 0 || value > 60)
        {
            await NotifyAlertAsync("Input Error", "TIME INTERVAL must be in range 0..60.");
            return;
        }

        if (_lastCommittedShakingInterval == value)
            return;

        await CommitAsync(async ct =>
        {
            await _source!.SaveShakingIntervalAsync(value, ct);
            _lastCommittedShakingInterval = value;
        }, "SHAKING interval saved.", "SHAKING interval save failed.");
    }

    public async Task CommitShakingOperatingTimeAsync()
    {
        if (_isApplying || !IsShakingAvailable)
            return;

        int value = ShakingOperatingTime;
        if (value < 20 || value > 180)
        {
            await NotifyAlertAsync("Input Error", "OPERATING TIME must be in range 20..180.");
            return;
        }

        if (_lastCommittedShakingOperating == value)
            return;

        await CommitAsync(async ct =>
        {
            await _source!.SaveShakingOperatingTimeAsync(value, ct);
            _lastCommittedShakingOperating = value;
        }, "SHAKING operating time saved.", "SHAKING operating time save failed.");
    }

    public async Task CommitPulseIntervalAsync()
    {
        if (_isApplying || !IsPulseAvailable)
            return;

        int value = PulseIntervalSetting;
        if (value < 0 || value > 9999)
        {
            await NotifyAlertAsync("Input Error", "PULSE INTERVAL must be in range 0..9999.");
            return;
        }

        if (_lastCommittedPulseInterval == value)
            return;

        await CommitAsync(async ct =>
        {
            await _source!.SavePulseIntervalAsync(value, ct);
            _lastCommittedPulseInterval = value;
        }, "PULSE interval saved.", "PULSE interval save failed.");
    }

    public async Task CommitAutoPulseAsync()
    {
        if (_isApplying || !IsPulseAvailable)
            return;

        bool value = IsAutoPulseEnabled;
        if (_lastCommittedAutoPulse.HasValue && _lastCommittedAutoPulse.Value == value)
            return;

        await CommitAsync(async ct =>
        {
            await _source!.SaveAutoPulseAsync(value, ct);
            _lastCommittedAutoPulse = value;
        }, "AUTO MODE saved.", "AUTO MODE save failed.");
    }

    private async Task ClearInitialAirVolumeAsync()
    {
        await CommitAsync(async ct =>
        {
            await _source!.ClearInitialAirVolumeFromSettingsAsync(ct);
            await ReloadSettingsOnlyAsync(ct);
        }, "Initial air volume cleared.", "Failed to clear initial air volume.");
    }

    private async Task ManualPulseAsync()
    {
        if (!IsPulseAvailable)
        {
            await NotifyAlertAsync("Operation Unavailable", "Pulse is not available for this model.");
            return;
        }

        await CommitAsync(async ct =>
        {
            await _source!.TriggerManualPulseFromSettingsAsync(ct);
        }, "Manual pulse sent.", "Failed to send manual pulse.");
    }

    private async Task ResetSettingValuesAsync()
    {
        await CommitAsync(async ct =>
        {
            await _source!.ResetSettingValuesFromSettingsAsync(ct);
            await ReloadSettingsOnlyAsync(ct);
        }, "Settings reset.", "Failed to reset settings.");
    }

    private async Task CommitAsync(Func<CancellationToken, Task> action, string successMessage, string failureMessage)
    {
        if (_source is null)
        {
            await NotifyAlertAsync("Operation Failed", "Main page context is not available.");
            return;
        }

        await _busyLock.WaitAsync();
        try
        {
            if (IsBusy)
                return;

            IsBusy = true;
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await action(cts.Token);

            StatusMessage = successMessage;
            LogRequested?.Invoke("[GENERAL] " + successMessage);
        }
        catch (Exception ex)
        {
            StatusMessage = failureMessage;
            LogRequested?.Invoke("[GENERAL] commit error: " + ex);
            await NotifyAlertAsync("Operation Failed", ex.Message);
        }
        finally
        {
            IsBusy = false;
            _busyLock.Release();
        }
    }

    private async Task ReloadSettingsOnlyAsync(CancellationToken ct)
    {
        var settings = await _source!.LoadGeneralSettingsAsync(ct);
        ApplyLoadedSettings(settings);
    }

    private void ApplyLoadedData(MainPageViewModel.GeneralSettingsPageData pageData)
    {
        ProductName = string.IsNullOrWhiteSpace(pageData.Product.ProductName) ? "-" : pageData.Product.ProductName;
        SerialNumber = string.IsNullOrWhiteSpace(pageData.Product.SerialNumber) ? "-" : pageData.Product.SerialNumber;
        ProgramVersion = string.IsNullOrWhiteSpace(pageData.Product.ProgramVersion) ? "-" : pageData.Product.ProgramVersion;

        ApplyLoadedSettings(pageData.Settings);
    }

    private void ApplyLoadedSettings(ChikoTpSession.GeneralSettingsSnapshot s)
    {
        _isApplying = true;
        OnPropertyChanged(nameof(IsApplying));
        try
        {
            IsShakingAvailable = s.ShakingAvailable;
            IsPulseAvailable = s.PulseAvailable;

            VolumeDownRate = s.VolumeDownRatePercent.ToString(CultureInfo.InvariantCulture);
            InitialAirVolume = s.InitialAirVolume_m3min.ToString("0.00", CultureInfo.InvariantCulture);
            AirVolumeReductionThreshold = s.AirVolumeReductionThreshold_m3min.ToString("0.00", CultureInfo.InvariantCulture);
            ShakingTimeInterval = s.ShakingTimeIntervalMinutes;
            ShakingOperatingTime = s.ShakingOperatingSeconds;
            PulseIntervalSetting = s.PulseIntervalSeconds;
            IsAutoPulseEnabled = s.PulseAutoMode;
            SelectedRemoteOutputSignal = ToRemoteOutputLabel(s.OperationAnalogSignal);

            _lastCommittedVolumeDownRate = s.VolumeDownRatePercent;
            _lastCommittedPulseInterval = s.PulseIntervalSeconds;
            _lastCommittedShakingInterval = s.ShakingTimeIntervalMinutes;
            _lastCommittedShakingOperating = s.ShakingOperatingSeconds;
            _lastCommittedRemoteOutput = s.OperationAnalogSignal;
            _lastCommittedAutoPulse = s.PulseAutoMode;
        }
        finally
        {
            _isApplying = false;
            OnPropertyChanged(nameof(IsApplying));
        }
    }

    private bool TryParseVolumeDownRate(out int value, out string error)
    {
        value = 0;
        error = "";

        if (!int.TryParse(VolumeDownRate?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
        {
            error = "VOLUME DOWN RATE must be numeric.";
            return false;
        }

        if (value < 0 || value > 100)
        {
            error = "VOLUME DOWN RATE must be in range 0..100.";
            return false;
        }

        return true;
    }

    private static int ToRemoteOutputCode(string? label)
    {
        return (label ?? "").Trim().ToUpperInvariant() switch
        {
            "AIR VOLUME" => 0,
            "OP" => 1,
            "SP" => 2,
            "DP" => 3,
            "EP" => 4,
            _ => 0
        };
    }

    private static string ToRemoteOutputLabel(int value)
    {
        return value switch
        {
            1 => "OP",
            2 => "SP",
            3 => "DP",
            4 => "EP",
            _ => "AIR VOLUME"
        };
    }

    private async Task NotifyAlertAsync(string title, string message)
    {
        if (ShowAlertRequested is null)
            return;

        await ShowAlertRequested(title, message);
    }

    public void Dispose()
    {
        _busyLock.Dispose();
    }

    private bool SetProperty<T>(ref T backingStore, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(backingStore, value))
            return false;

        backingStore = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
