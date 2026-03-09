using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Microsoft.Maui.Controls;

namespace Chiko.WirelessControl.App.ViewModels;

public sealed class GeneralSettingsViewModel : INotifyPropertyChanged
{
    private readonly MainPageViewModel? _source;

    public event PropertyChangedEventHandler? PropertyChanged;

    public event Action<string>? LogRequested;

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
    public ICommand ClearInitialAirVolumeCommand { get; }
    public ICommand ManualPulseCommand { get; }
    public ICommand ResetSettingValueCommand { get; }

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

    private int _shakingTimeInterval = 0;
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

    private int _pulseIntervalSetting = 0;
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

    public bool IsShakingAvailable => _source?.IsShakingVisible ?? false;
    public bool IsPulseAvailable => _source?.IsPulseEnabled ?? false;

    public string ProductName => string.IsNullOrWhiteSpace(_source?.ModelName) ? "-" : _source!.ModelName;
    public string SerialNumber => string.IsNullOrWhiteSpace(_source?.SerialNumber) ? "-" : _source!.SerialNumber;
    public string ProgramVersion => string.IsNullOrWhiteSpace(_source?.ProgramVersion) ? "-" : _source!.ProgramVersion;

    public GeneralSettingsViewModel(MainPageViewModel? source)
    {
        _source = source;

        if (_source is not null)
            _source.PropertyChanged += OnSourcePropertyChanged;

        ClearInitialAirVolumeCommand = new Command(() =>
        {
            InitialAirVolume = "0.00";
            AirVolumeReductionThreshold = "0.00";
            LogRequested?.Invoke("[GENERAL] CLEAR INITIAL AIR VOLUME (stub)");
        });

        ManualPulseCommand = new Command(() =>
        {
            LogRequested?.Invoke("[GENERAL] MANUAL PULSE (stub)");
        });

        ResetSettingValueCommand = new Command(() =>
        {
            VolumeDownRate = "100";
            InitialAirVolume = "0.00";
            AirVolumeReductionThreshold = "0.00";
            ShakingTimeInterval = 0;
            ShakingOperatingTime = 20;
            PulseIntervalSetting = 0;
            IsAutoPulseEnabled = false;
            LogRequested?.Invoke("[GENERAL] RESET SETTING VALUE (stub)");
        });
    }

    public void Dispose()
    {
        if (_source is not null)
            _source.PropertyChanged -= OnSourcePropertyChanged;
    }

    private void OnSourcePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainPageViewModel.IsShakingVisible) or nameof(MainPageViewModel.IsPulseEnabled))
        {
            OnPropertyChanged(nameof(IsShakingAvailable));
            OnPropertyChanged(nameof(IsPulseAvailable));
        }

        if (e.PropertyName is nameof(MainPageViewModel.ModelName))
            OnPropertyChanged(nameof(ProductName));

        if (e.PropertyName is nameof(MainPageViewModel.SerialNumber))
            OnPropertyChanged(nameof(SerialNumber));

        if (e.PropertyName is nameof(MainPageViewModel.ProgramVersion))
            OnPropertyChanged(nameof(ProgramVersion));
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
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
