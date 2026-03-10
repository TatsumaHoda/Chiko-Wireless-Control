using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Chiko.WirelessControl.App.Services;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using Microsoft.Maui.Controls;
using SkiaSharp;

namespace Chiko.WirelessControl.App.ViewModels;

public sealed class OperationHistoryViewModel : INotifyPropertyChanged
{
    private const int RequestedCount = 1160;

    private readonly MainPageViewModel _source;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ICommand ReadCommand { get; }

    public ObservableCollection<LogHistoryRow> Rows { get; } = new();
    public ObservableCollection<MetricToggle> MetricToggles { get; } = new();

    public ObservableCollection<ISeries> ChartSeries { get; } = new();
    public ObservableCollection<Axis> ChartXAxes { get; } = new();
    public ObservableCollection<Axis> ChartYAxes { get; } = new();

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(IsNotBusy));
                OnPropertyChanged(nameof(IsOverlayVisible));
            }
        }
    }

    public bool IsNotBusy => !IsBusy;
    public bool IsOverlayVisible => IsBusy;

    private string _statusMessage = "Ready.";
    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    private int _currentReadCount;
    public int CurrentReadCount
    {
        get => _currentReadCount;
        private set
        {
            if (SetProperty(ref _currentReadCount, value))
                OnPropertyChanged(nameof(ReadProgressText));
        }
    }

    public string ReadProgressText => $"{CurrentReadCount} / {RequestedCount}";

    private string _noDataMessage = string.Empty;
    public string NoDataMessage
    {
        get => _noDataMessage;
        private set => SetProperty(ref _noDataMessage, value);
    }

    private bool _isNoDataVisible;
    public bool IsNoDataVisible
    {
        get => _isNoDataVisible;
        private set => SetProperty(ref _isNoDataVisible, value);
    }

    public OperationHistoryViewModel(MainPageViewModel source)
    {
        _source = source;
        ReadCommand = new Command(async () => await ReadAsync(), () => !IsBusy);

        ChartXAxes.Add(new Axis
        {
            Name = "Time",
            LabelsRotation = 0,
            Labeler = value => FormatXLabel(value)
        });

        ChartYAxes.Add(new Axis
        {
            Name = "Value"
        });

        AddMetric("air", "Air Volume", "#4FC3F7");
        AddMetric("op", "OP", "#64B5F6");
        AddMetric("sp", "SP", "#9575CD");
        AddMetric("dp", "DP", "#81C784");
        AddMetric("ep", "EP", "#FFD54F");
        AddMetric("temp", "Blower / Motor Temp", "#FF8A65");
        AddMetric("rpm", "RPM", "#E57373");
        AddMetric("hours", "Operating Hours", "#A1887F");
        AddMetric("lv", "Lv", "#90A4AE");
    }

    public async Task ReadAsync()
    {
        if (IsBusy)
            return;

        try
        {
            IsBusy = true;
            (ReadCommand as Command)?.ChangeCanExecute();

            CurrentReadCount = 0;
            IsNoDataVisible = false;
            NoDataMessage = string.Empty;
            StatusMessage = "Reading operation history...";

            var progress = new Progress<int>(count => CurrentReadCount = count);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(240));
            var items = await _source.ReadOperationHistoryAsync(cts.Token, progress);

            Rows.Clear();
            foreach (var item in items.OrderBy(x => x.Timestamp))
            {
                Rows.Add(LogHistoryRow.From(item));
            }

            if (Rows.Count == 0)
            {
                IsNoDataVisible = true;
                NoDataMessage = "No data";
                StatusMessage = "No history data.";
            }
            else
            {
                StatusMessage = $"Loaded {Rows.Count} records.";
            }

            RebuildChart();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to read operation history: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            (ReadCommand as Command)?.ChangeCanExecute();
        }
    }

    private void AddMetric(string key, string label, string colorHex)
    {
        var metric = new MetricToggle(key, label, colorHex, true);
        metric.PropertyChanged += OnMetricPropertyChanged;
        MetricToggles.Add(metric);
    }

    private void OnMetricPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MetricToggle.IsChecked))
            RebuildChart();
    }

    private void RebuildChart()
    {
        ChartSeries.Clear();

        if (Rows.Count == 0)
        {
            OnPropertyChanged(nameof(ChartSeries));
            return;
        }

        foreach (var metric in MetricToggles.Where(x => x.IsChecked))
        {
            var points = new List<ObservablePoint>(Rows.Count);
            for (var i = 0; i < Rows.Count; i++)
            {
                points.Add(new ObservablePoint(i, GetMetricValue(Rows[i], metric.Key)));
            }

            ChartSeries.Add(new LineSeries<ObservablePoint>
            {
                Values = points,
                Name = metric.Label,
                GeometrySize = 0,
                Fill = null,
                Stroke = new SolidColorPaint(SKColor.Parse(metric.ColorHex), 2)
            });
        }

        OnPropertyChanged(nameof(ChartSeries));
    }

    private static double GetMetricValue(LogHistoryRow row, string key)
    {
        return key switch
        {
            "air" => row.AirVolume,
            "op" => row.OutsidePressure,
            "sp" => row.SuctionPressure,
            "dp" => row.DifferentialPressure ?? 0,
            "ep" => row.ExhaustPressure,
            "temp" => row.BlowerMotorTemperature,
            "rpm" => row.Rpm,
            "hours" => row.OperationHours,
            "lv" => row.Level,
            _ => row.AirVolume
        };
    }

    private string FormatXLabel(double value)
    {
        if (Rows.Count == 0)
            return string.Empty;

        var index = (int)Math.Round(value);
        if (index < 0 || index >= Rows.Count)
            return string.Empty;

        var step = Math.Max(1, Rows.Count / 8);
        if (index % step != 0 && index != Rows.Count - 1)
            return string.Empty;

        return Rows[index].Timestamp.ToString("MM-dd HH:mm", CultureInfo.InvariantCulture);
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

    public sealed class MetricToggle : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        public string Key { get; }
        public string Label { get; }
        public string ColorHex { get; }

        private bool _isChecked;
        public bool IsChecked
        {
            get => _isChecked;
            set
            {
                if (_isChecked == value)
                    return;

                _isChecked = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked)));
            }
        }

        public MetricToggle(string key, string label, string colorHex, bool isChecked)
        {
            Key = key;
            Label = label;
            ColorHex = colorHex;
            _isChecked = isChecked;
        }
    }

    public sealed class LogHistoryRow
    {
        public DateTime Timestamp { get; init; }
        public string TimestampText { get; init; } = string.Empty;
        public string Trigger { get; init; } = string.Empty;
        public int Level { get; init; }
        public double AirVolume { get; init; }
        public double OutsidePressure { get; init; }
        public double SuctionPressure { get; init; }
        public double? DifferentialPressure { get; init; }
        public double ExhaustPressure { get; init; }
        public double BlowerMotorTemperature { get; init; }
        public int Rpm { get; init; }
        public int OperationHours { get; init; }
        public string ErrorDetails { get; init; } = string.Empty;

        public string LvText => Level.ToString(CultureInfo.InvariantCulture);
        public string AirVolumeText => AirVolume.ToString("0.00", CultureInfo.InvariantCulture);
        public string OutsideText => OutsidePressure.ToString("0.00", CultureInfo.InvariantCulture);
        public string SuctionText => SuctionPressure.ToString("0.00", CultureInfo.InvariantCulture);
        public string DifferentialText => DifferentialPressure.HasValue ? DifferentialPressure.Value.ToString("0.00", CultureInfo.InvariantCulture) : "-";
        public string ExhaustText => ExhaustPressure.ToString("0.00", CultureInfo.InvariantCulture);
        public string BlowerTempText => BlowerMotorTemperature.ToString("0.0", CultureInfo.InvariantCulture);
        public string RpmText => Rpm.ToString(CultureInfo.InvariantCulture);
        public string HoursText => OperationHours.ToString(CultureInfo.InvariantCulture);

        public static LogHistoryRow From(ChikoTpSession.LogHistoryItem item)
        {
            return new LogHistoryRow
            {
                Timestamp = item.Timestamp,
                TimestampText = item.Timestamp.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                Trigger = item.TriggerType switch
                {
                    1 => "Run state change (RUN)",
                    2 => "Hourly logging",
                    3 => "Error occurred",
                    4 => "Run state change (STOP)",
                    _ => "Unknown"
                },
                Level = item.Level,
                AirVolume = item.AirVolume,
                OutsidePressure = item.OutsidePressure,
                SuctionPressure = item.SuctionPressure,
                DifferentialPressure = item.DifferentialPressure,
                ExhaustPressure = item.ExhaustPressure,
                BlowerMotorTemperature = item.BlowerMotorTemperature,
                Rpm = item.Rpm,
                OperationHours = item.OperationHours,
                ErrorDetails = string.IsNullOrWhiteSpace(item.ErrorSummary)
                    ? item.ErrorCodeRaw
                    : $"{item.ErrorCodeRaw} ({item.ErrorSummary})"
            };
        }
    }
}

