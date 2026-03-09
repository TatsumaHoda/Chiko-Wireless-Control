using Chiko.WirelessControl.App.Services;
using Chiko.WirelessControl.App.Views;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.Measure;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Maui;
using LiveChartsCore.SkiaSharpView.Painting;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using SkiaSharp;
using SkiaSharp.Extended.UI.Controls;
using SkiaSharp.Views.Maui;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace Chiko.WirelessControl.App.ViewModels;

public sealed class MainPageViewModel : INotifyPropertyChanged
{
    // ★DI（OSごとに実体が入る想定）
    private readonly IWifiApScanner? _wifiApScanner;
    private readonly IBleScanner? _bleScanner;
    private readonly IClassicBtScanner? _classicBtScanner;
    private readonly IWifiSsidConnector? _wifiSsidConnector;
    private readonly IClassicBtConnector? _classicBtConnector;
    private readonly IBleConnector? _bleConnector;

    public ObservableCollection<DeviceItem> Devices { get; } = new();

    // ===== INotifyPropertyChanged =====
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private bool SetProperty<T>(ref T backingStore, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(backingStore, value)) return false;
        backingStore = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    public event Func<string, string, Task>? ShowAlertRequested;

    // ★入力要求（毎回パスワードを聞く）
    public event Func<string, string, string, bool, Task<string?>>? PromptRequested;

    private Task<string?> PromptAsync(string title, string message, string placeholder, bool isPassword)
        => PromptRequested is null ? Task.FromResult<string?>(null) : PromptRequested(title, message, placeholder, isPassword);

    // ===== 画面上部：機器情報 =====
    private string _modelName = "";
    public string ModelName { get => _modelName; set => SetProperty(ref _modelName, value); }

    private string _serialNumber = "";
    public string SerialNumber { get => _serialNumber; set => SetProperty(ref _serialNumber, value); }
    // ===== 運転状態表示 =====
    private bool _isRunning;
    public bool IsRunning
    {
        get => _isRunning;
        set
        {
            if (SetProperty(ref _isRunning, value))
            {
                OnPropertyChanged(nameof(RunStateText));
                OnPropertyChanged(nameof(RunStateColor));
            }
        }
    }
    public string RunStateText => IsRunning ? "RUN" : "STOP";

    // RUN=緑 / STOP=赤（好みの色に調整OK）
    public Color RunStateColor => IsRunning ? Color.FromArgb("#1BAA5B") : Color.FromArgb("#D64545");

    // ===== 配管径入力 =====
    private string _pipeDiameterInput = "";
    public string PipeDiameterInput { get => _pipeDiameterInput; set => SetProperty(ref _pipeDiameterInput, value); }

    // ===== エラー表示（パネル）=====
    private bool _isWarnErrVisible;
    public bool IsWarnErrVisible { get => _isWarnErrVisible; set => SetProperty(ref _isWarnErrVisible, value); }

    private string _errorNo = "";
    public string ErrorNo { get => _errorNo; set => SetProperty(ref _errorNo, value); }

    private string _errorTitle = "";
    public string ErrorTitle { get => _errorTitle; set => SetProperty(ref _errorTitle, value); }

    private string _errorCause = "";
    public string ErrorCause { get => _errorCause; set => SetProperty(ref _errorCause, value); }

    // ERR=赤 / CAUTION=オレンジ
    private Color _errorAccentColor = Colors.Transparent;
    public Color ErrorAccentColor { get => _errorAccentColor; set => SetProperty(ref _errorAccentColor, value); }

    // 点滅（Opacity）
    private double _warnErrOpacity = 1.0;
    public double WarnErrOpacity { get => _warnErrOpacity; set => SetProperty(ref _warnErrOpacity, value); }

    // ===== メトリクス表示（初期ダミー）=====
    private string _volumeText = "0.00";
    public string VolumeText { get => _volumeText; set => SetProperty(ref _volumeText, value); }

    private string _velocityText = "0.00";
    public string VelocityText { get => _velocityText; set => SetProperty(ref _velocityText, value); }

    private string _speedText = "0";
    public string SpeedText { get => _speedText; set => SetProperty(ref _speedText, value); }

    private string _tempText = "0.0";
    public string TempText { get => _tempText; set => SetProperty(ref _tempText, value); }

    private string _outsideText = "0.00";
    public string OutsideText { get => _outsideText; set => SetProperty(ref _outsideText, value); }

    private string _suctionText = "0.00";
    public string SuctionText { get => _suctionText; set => SetProperty(ref _suctionText, value); }

    private string _differentialText = "0.00";
    public string DifferentialText { get => _differentialText; set => SetProperty(ref _differentialText, value); }

    private string _exhaustText = "0.00";
    public string ExhaustText { get => _exhaustText; set => SetProperty(ref _exhaustText, value); }
    // ===== メトリクス強調（背景）=====
    private Brush? _speedCardBg;
    public Brush? SpeedCardBg { get => _speedCardBg; set => SetProperty(ref _speedCardBg, value); }

    private Brush? _tempCardBg;
    public Brush? TempCardBg { get => _tempCardBg; set => SetProperty(ref _tempCardBg, value); }

    private Brush? _suctionCardBg;
    public Brush? SuctionCardBg { get => _suctionCardBg; set => SetProperty(ref _suctionCardBg, value); }

    private Brush? _exhaustCardBg;
    public Brush? ExhaustCardBg { get => _exhaustCardBg; set => SetProperty(ref _exhaustCardBg, value); }

    // =========================
    // Live Chart (S4 realtime)
    // =========================
    public ICommand ToggleQCommand { get; }
    public ICommand ToggleVCommand { get; }
    public ICommand ToggleOPCommand { get; }
    public ICommand ToggleSPCommand { get; }
    public ICommand ToggleDPCommand { get; }
    public ICommand ToggleEPCommand { get; }
    public ICommand ToggleTempCommand { get; }
    public ICommand ToggleRpmCommand { get; }

    public LiveChartsCore.SkiaSharpView.Painting.SolidColorPaint LegendTextPaint { get; private set; } = null!;
    public LiveChartsCore.SkiaSharpView.Painting.SolidColorPaint FramePaint { get; private set; } = null!;
    public LiveChartsCore.SkiaSharpView.Painting.SolidColorPaint TooltipTextPaint { get; private set; } = null!;
    public LiveChartsCore.SkiaSharpView.Painting.SolidColorPaint TooltipBgPaint { get; private set; } = null!;
    // ===== Chart theme paints (from ResourceDictionary) =====
    private SolidColorPaint _axisTextPaint = null!;
    private SolidColorPaint _axisNamePaint = null!;
    private SolidColorPaint _gridPaint = null!;
    private SolidColorPaint _sectionBaselinePaint = null!;

    // series paints
    private SolidColorPaint _qPaint = null!;
    private SolidColorPaint _vPaint = null!;
    private SolidColorPaint _opPaint = null!;
    private SolidColorPaint _spPaint = null!;
    private SolidColorPaint _dpPaint = null!;
    private SolidColorPaint _epPaint = null!;
    private SolidColorPaint _tempPaint = null!;
    private SolidColorPaint _rpmPaint = null!;

    // === realtime chart window ===
    private const double ChartWindowSeconds = 60.0;
    private const double SampleIntervalSeconds = 0.5; // 500ms

    // 0..60 を 0.5秒刻みで持つ（0=最新, 60=最古）
    private const int ChartPointCount = (int)(ChartWindowSeconds / SampleIntervalSeconds) + 1; // 121

    // 固定長バッファ（系列ごと）
    private readonly double[] _bufQ = new double[ChartPointCount];
    private readonly double[] _bufV = new double[ChartPointCount];
    private readonly double[] _bufOP = new double[ChartPointCount];
    private readonly double[] _bufSP = new double[ChartPointCount];
    private readonly double[] _bufDP = new double[ChartPointCount];
    private readonly double[] _bufEP = new double[ChartPointCount];
    private readonly double[] _bufTemp = new double[ChartPointCount];
    private readonly double[] _bufRpmK = new double[ChartPointCount];

    // 60秒分だけ保持（+αは好み）
    private const int ChartMaxPoints = (int)(ChartWindowSeconds / SampleIntervalSeconds) + 5;

    // 値配列（ObservablePoint: X,Y）
    private readonly ObservableCollection<ObservablePoint> _q = new();
    private readonly ObservableCollection<ObservablePoint> _v = new();
    private readonly ObservableCollection<ObservablePoint> _op = new();
    private readonly ObservableCollection<ObservablePoint> _sp = new();
    private readonly ObservableCollection<ObservablePoint> _dp = new();
    private readonly ObservableCollection<ObservablePoint> _ep = new();
    private readonly ObservableCollection<ObservablePoint> _temp = new();
    private readonly ObservableCollection<ObservablePoint> _rpmK = new(); // rpm/1000

    // ---- Axis update throttle ----
    private DateTimeOffset _lastAxisUpdateAt = DateTimeOffset.MinValue;
    private double _lastLeftLimit = double.NaN;
    private double _lastRightLimit = double.NaN;
    private double _lastLeftStep = double.NaN;
    private double _lastRightStep = double.NaN;
    private long _axisUpdateTick;

    // Chart binding（XAMLで CartesianChart にバインドする）
    public ObservableCollection<ISeries> ChartSeries { get; } = new();
    public ObservableCollection<Axis> ChartXAxes { get; } = new();
    public ObservableCollection<Axis> ChartYAxes { get; } = new();
    public ObservableCollection<RectangularSection> ChartSections { get; } = new();

    // ---- Series 表示ON/OFF（UIでチェックボックスにバインド）----
    private bool _showQ = true;
    public bool ShowQ { get => _showQ; set { if (SetProperty(ref _showQ, value)) RebuildChartSeries(); } }

    private bool _showV = false;
    public bool ShowV { get => _showV; set { if (SetProperty(ref _showV, value)) RebuildChartSeries(); } }

    private bool _showOP = true;
    public bool ShowOP { get => _showOP; set { if (SetProperty(ref _showOP, value)) RebuildChartSeries(); } }

    private bool _showSP = true;
    public bool ShowSP { get => _showSP; set { if (SetProperty(ref _showSP, value)) RebuildChartSeries(); } }

    private bool _showDP = true;
    public bool ShowDP { get => _showDP; set { if (SetProperty(ref _showDP, value)) RebuildChartSeries(); } }

    private bool _showEP = true;
    public bool ShowEP { get => _showEP; set { if (SetProperty(ref _showEP, value)) RebuildChartSeries(); } }

    private bool _showTemp = true;
    public bool ShowTemp { get => _showTemp; set { if (SetProperty(ref _showTemp, value)) RebuildChartSeries(); } }

    private bool _showRpm = true;
    public bool ShowRpm { get => _showRpm; set { if (SetProperty(ref _showRpm, value)) RebuildChartSeries(); } }

    private bool _isShakingVisible;
    public bool IsShakingVisible
    {
        get => _isShakingVisible;
        set => SetProperty(ref _isShakingVisible, value);
    }

    private bool _isPulseEnabled;
    public bool IsPulseEnabled
    {
        get => _isPulseEnabled;
        set => SetProperty(ref _isPulseEnabled, value);
    }


    // ---- 基準風量（初期風量）水平線：Q[m3/min] ----
    private double? _baselineFlow;
    public double? BaselineFlow
    {
        get => _baselineFlow;
        set
        {
            if (SetProperty(ref _baselineFlow, value))
                UpdateBaselineSection();
        }
    }

    private static void Push(double[] buf, double v)
    {
        // 最新を buf[0] に入れ、既存を後ろへ（古い方へ）ずらす
        for (int i = buf.Length - 1; i >= 1; i--)
            buf[i] = buf[i - 1];

        buf[0] = v;
    }

    // 内部状態
    private bool _isFirstErrorDisplay = true;
    private CancellationTokenSource? _warnErrBlinkCts;

    // ===== Lv（★スライダー変更で送信：debounce付）=====
    private int _level = 15;
    public int Level
    {
        get => _level;
        set
        {
            if (!SetProperty(ref _level, value)) return;

            // ★接続後の初回S4反映中は送信しない（ループ防止）
            if (_suppressLevelSend) return;

            // ★接続中で、初回Lv反映済みになってから送信
            if (!IsConnected || _tp is null) return;
            if (!_levelInitialized) return;

            _ = SendLevelDebouncedAsync(_level);
        }
    }

    // ===== 接続方式（トグル）=====
    private bool _isBluetoothSelected;
    public bool IsBluetoothSelected { get => _isBluetoothSelected; set => SetProperty(ref _isBluetoothSelected, value); }

    private bool _isBleSelected;
    public bool IsBleSelected { get => _isBleSelected; set => SetProperty(ref _isBleSelected, value); }

    private bool _isWifiSelected = true;
    public bool IsWifiSelected { get => _isWifiSelected; set => SetProperty(ref _isWifiSelected, value); }

    // ===== 選択デバイス =====
    private DeviceItem? _selectedDevice;
    public DeviceItem? SelectedDevice
    {
        get => _selectedDevice;
        set
        {
            if (SetProperty(ref _selectedDevice, value))
                OnPropertyChanged(nameof(HasSelectedDevice));
        }
    }
    public bool HasSelectedDevice => SelectedDevice != null;

    // ===== スキャン状態 =====
    private bool _isScanning;
    public bool IsScanning { get => _isScanning; set => SetProperty(ref _isScanning, value); }

    public enum OverlayMode
    {
        None,
        ScanBluetooth,
        ScanBle,
        ScanWifi,
        ConnectBluetooth,
        ConnectBle,
        ConnectWifi
    }

    private OverlayMode _overlayState = OverlayMode.None;
    public OverlayMode OverlayState
    {
        get => _overlayState;
        set
        {
            if (SetProperty(ref _overlayState, value))
            {
                OnPropertyChanged(nameof(IsOverlayVisible));
                OnPropertyChanged(nameof(OverlayText));
                UpdateOverlayLottieSource();
            }
        }
    }

    public bool IsOverlayVisible => OverlayState != OverlayMode.None;

    public string OverlayText => OverlayState switch
    {
        OverlayMode.ScanBluetooth => "Searching (Bluetooth)...",
        OverlayMode.ScanBle => "Searching (BLE)...",
        OverlayMode.ScanWifi => "Searching (Wi-Fi)...",
        OverlayMode.ConnectBluetooth => "Connecting (Bluetooth)...",
        OverlayMode.ConnectBle => "Connecting (BLE)...",
        OverlayMode.ConnectWifi => "Connecting (Wi-Fi)...",
        _ => ""
    };

    // ★Sourceごと差し替える（確実）
    private SKLottieImageSource? _overlayLottieSource;
    public SKLottieImageSource? OverlayLottieSource
    {
        get => _overlayLottieSource;
        private set => SetProperty(ref _overlayLottieSource, value);
    }

    private void UpdateOverlayLottieSource()
    {
        var file = OverlayState switch
        {
            OverlayMode.ScanBluetooth => "bluetooth.json",
            OverlayMode.ScanBle => "bluetooth.json",
            OverlayMode.ScanWifi => "wifi_connect.json",
            OverlayMode.ConnectBluetooth => "connecting.json",
            OverlayMode.ConnectBle => "connecting.json",
            OverlayMode.ConnectWifi => "connecting.json",
            _ => "connecting.json"
        };

        OverlayLottieSource = new SKFileLottieImageSource { File = file };
    }

    private CancellationTokenSource? _scanCts;

    // ===== UI Enable =====
    private bool _isUiEnabled = true;
    public bool IsUiEnabled { get => _isUiEnabled; set => SetProperty(ref _isUiEnabled, value); }

    private bool _isControlsOpen;
    public bool IsControlsOpen
    {
        get => _isControlsOpen;
        set
        {
            if (_isControlsOpen == value) return;
            _isControlsOpen = value;
            OnPropertyChanged();
        }
    }

    public event Action? ControlsOpened;
    public event Action? ControlsClosed;

    private bool _isControlsSheetMode = true;
    public bool IsControlsSheetMode
    {
        get => _isControlsSheetMode;
        set
        {
            if (_isControlsSheetMode == value) return;
            _isControlsSheetMode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ControlsModeText));
        }
    }

    private bool _isAdminAuthOpening;

    private bool _isMenuOpen;
    public bool IsMenuOpen
    {
        get => _isMenuOpen;
        set
        {
            if (_isMenuOpen == value) return;
            _isMenuOpen = value;
            OnPropertyChanged();
        }
    }

    public string ControlsModeText => IsControlsSheetMode ? "SHEET" : "DIALOG";

    public ICommand OpenControlsCommand { get; }
    public ICommand CloseControlsCommand { get; }
    public ICommand ToggleControlsModeCommand { get; }

    public ICommand CloseMenuCommand { get; }

    public ICommand OpenGeneralSettingsCommand { get; }
    public ICommand OpenErrorHistoryCommand { get; }
    public ICommand OpenOperationHistoryCommand { get; }
    public ICommand OpenAdminSettingsCommand { get; }

    // ===== Commands =====
    public ICommand ScanCommand { get; }
    public ICommand ConnectCommand { get; }

    // ★MAUI Command の CanExecute 更新に備え、実体を保持
    private readonly Command _disconnectCommand;
    public ICommand DisconnectCommand => _disconnectCommand;

    public ICommand RunCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand ErrorClearCommand { get; }
    public ICommand ShakingCommand { get; }

    // （Level用：UIで使うならXAMLでバインド）
    public ICommand LevelUpCommand { get; }
    public ICommand LevelDownCommand { get; }

    public ICommand SetInitialFlowCommand { get; }
    public ICommand OpenMenuCommand { get; }

    public ICommand SelectBluetoothCommand { get; }
    public ICommand SelectBleCommand { get; }
    public ICommand SelectWifiCommand { get; }
    public ICommand SetPipeDiameterCommand { get; }
    
    // ===== 連打防止 =====
    private readonly SemaphoreSlim _connLock = new(1, 1);

    // ===== 接続保持 =====
    private IAsyncDisposable? _activeLink;       // BT/BLE link
    private ChikoStreamClient? _activeClient;    // TCP/BLE/BT 共通 client
    private IAsyncDisposable? _activeWifiLease;  // Android Wi-Fi lease

    // ===== 操作/監視 =====
    private ChikoTpSession? _tp;                 // 接続後に共有

    // ★監視CTS（Connect用CTSと分離して「切断まで生きる」）
    private CancellationTokenSource? _monitorCts;

    private CancellationTokenSource? _s4Cts;     // S4ポーリング用（monitorとリンク）
    private PeriodicTimer? _s4Timer;             // 500msタイマ
    private bool _levelInitialized;              // Lvは接続時1回だけ反映

    // ★Lv送信 debounce / 抑制
    private CancellationTokenSource? _levelSendCts;
    private bool _suppressLevelSend;

    private bool _isConnected;
    public bool IsConnected
    {
        get => _isConnected;
        private set
        {
            if (SetProperty(ref _isConnected, value))
            {
                OnPropertyChanged(nameof(CanDisconnect));
                _disconnectCommand.ChangeCanExecute();
            }
        }
    }

    public bool CanDisconnect => IsConnected;

    private static void Log(string msg)
    {
        Debug.WriteLine(msg);
#if ANDROID
        Android.Util.Log.Debug("CHIKO", msg);
#endif
    }

    // ===== ctor =====
    public MainPageViewModel(
        IWifiApScanner? wifiApScanner = null,
        IClassicBtScanner? classicBtScanner = null,
        IBleScanner? bleScanner = null,
        IWifiSsidConnector? wifiSsidConnector = null,
        IClassicBtConnector? classicBtConnector = null,
        IBleConnector? bleConnector = null)
    {
        _wifiApScanner = wifiApScanner;
        _classicBtScanner = classicBtScanner;
        _bleScanner = bleScanner;
        _wifiSsidConnector = wifiSsidConnector;
        _classicBtConnector = classicBtConnector;
        _bleConnector = bleConnector;

        ScanCommand = new Command(async () => await ScanAsync());
        ConnectCommand = new Command<DeviceItem?>(async d => await ConnectAsync(d));
        _disconnectCommand = new Command(async () => await DisconnectAsync(), () => CanDisconnect);

        RunCommand = new Command(async () => await RunAsync());
        StopCommand = new Command(async () => await StopAsync());
        ErrorClearCommand = new Command(async () => await ErrorClearAsync());
        ShakingCommand = new Command(async () => await ShakingAsync());
        SetPipeDiameterCommand = new Command(async () => await SetPipeDiameterAsync());

        LevelUpCommand = new Command(async () => await ChangeLevelAsync(+1));
        LevelDownCommand = new Command(async () => await ChangeLevelAsync(-1));

        SetInitialFlowCommand = new Command(async () => await SetInitialFlowAsync());

        OpenMenuCommand = new Command(() =>
        {
            IsMenuOpen = true;
            IsUiEnabled = false;
        });

        CloseMenuCommand = new Command(() =>
        {
            IsMenuOpen = false;
            IsUiEnabled = true;
        });

        OpenGeneralSettingsCommand = new Command(async () => await OpenGeneralSettingsAsync());
        OpenErrorHistoryCommand = new Command(async () => await OpenErrorHistoryAsync());
        OpenOperationHistoryCommand = new Command(async () => await OpenOperationHistoryAsync());
        OpenAdminSettingsCommand = new Command(async () => await OpenAdminSettingsAsync());

        OpenControlsCommand = new Command(() =>
        {
            Log("[CTRL] OpenControlsCommand");
            IsControlsOpen = true;
            IsUiEnabled = false;
            ControlsOpened?.Invoke();
        });

        CloseControlsCommand = new Command(() =>
        {
            Log("[CTRL] CloseControlsCommand");
            ControlsClosed?.Invoke();
            IsControlsOpen = false;
            IsUiEnabled = true;
        });

        ToggleControlsModeCommand = new Command(() =>
        {
            Log("[CTRL] ToggleControlsModeCommand");
            IsControlsSheetMode = !IsControlsSheetMode;
        });

        SelectBluetoothCommand = new Command(() =>
        {
            IsBluetoothSelected = true;
            IsBleSelected = false;
            IsWifiSelected = false;
        });

        SelectBleCommand = new Command(() =>
        {
            IsBluetoothSelected = false;
            IsBleSelected = true;
            IsWifiSelected = false;
        });

        SelectWifiCommand = new Command(() =>
        {
            IsBluetoothSelected = false;
            IsBleSelected = false;
            IsWifiSelected = true;
        });

        ToggleQCommand = new Command(() => ShowQ = !ShowQ);
        ToggleVCommand = new Command(() => ShowV = !ShowV);
        ToggleOPCommand = new Command(() => ShowOP = !ShowOP);
        ToggleSPCommand = new Command(() => ShowSP = !ShowSP);
        ToggleDPCommand = new Command(() => ShowDP = !ShowDP);
        ToggleEPCommand = new Command(() => ShowEP = !ShowEP);
        ToggleTempCommand = new Command(() => ShowTemp = !ShowTemp);
        ToggleRpmCommand = new Command(() => ShowRpm = !ShowRpm);

        InitChart();
    }

    // =========================
    // Chart init / series build
    // =========================

    // ★追加：文字用Paint（不透明＋にじみ軽減）
    private static SolidColorPaint ResTextPaint(string key, byte alpha = 255, SKColor? fallback = null)
    {
        var fb = fallback ?? SKColors.White;
        var sk = GetResSkColor(key, fb).WithAlpha(alpha);

        // ★LiveChartsのバージョン差を吸収するため、引数は色だけにしてプロパティで設定
        var p = new SolidColorPaint(sk);

        // 文字用：ストローク幅は不要（0扱い）
        p.StrokeThickness = 0;

        // ★文字の“にじみ”を抑える（Windowsで効きやすい）
        p.IsAntialias = true;

        return p;
    }
    // ResourceDictionary の Color を SKColor に変換
    private static SKColor GetResSkColor(string key, SKColor fallback)
    {
        try
        {
            if (Application.Current?.Resources.TryGetValue(key, out var v) == true && v is Color c)
                return ToSkColor(c);
        }
        catch { }
        return fallback;
    }

    // MAUI Color -> SkiaSharp SKColor（拡張に依存しない）
    private static SKColor ToSkColor(Microsoft.Maui.Graphics.Color c)
    {
        byte r = (byte)Math.Clamp((int)(c.Red * 255), 0, 255);
        byte g = (byte)Math.Clamp((int)(c.Green * 255), 0, 255);
        byte b = (byte)Math.Clamp((int)(c.Blue * 255), 0, 255);
        byte a = (byte)Math.Clamp((int)(c.Alpha * 255), 0, 255);
        return new SKColor(r, g, b, a);
    }

    // 便利：SolidColorPaint を Resource から生成（厚み指定も）
    private static SolidColorPaint ResPaint(string key, float strokeThickness = 1, byte? alpha = null, SKColor? fallback = null)
    {
        var fb = fallback ?? SKColors.White;
        var sk = GetResSkColor(key, fb);

        if (alpha.HasValue)
            sk = sk.WithAlpha(alpha.Value);

        var p = new SolidColorPaint(sk);
        p.StrokeThickness = strokeThickness;  // ← ここも同様に、無ければ削除でOK
        return p;
    }


    // Chart のテーマ（色）を ResourceDictionary から構築
    private void BuildChartThemeFromResources()
    {
        // ---- texts ----
        // ★TextSecondary は薄くてにじみやすいので TextPrimary を推奨
        _axisTextPaint = ResTextPaint("TextPrimary", 255);
        _axisNamePaint = ResTextPaint("TextPrimary", 255);


        // ---- grid/separators（薄めに） ----
        _gridPaint = ResPaint("Border", 1, alpha: 90);

        // ---- frame（少し濃く） ----
        FramePaint = ResPaint("Border", 1, alpha: 160);

        // ---- legend/tooltips ----
        LegendTextPaint = ResPaint("TextPrimary", strokeThickness: 0);
        TooltipTextPaint = ResPaint("TextPrimary", strokeThickness: 0);
        TooltipBgPaint = ResPaint("Surface2", 1, alpha: 240);

        // ---- baseline section ----
        _sectionBaselinePaint = ResPaint("TextPrimary", 2, alpha: 200);

        // ---- series colors（CHIKOテーマに寄せる）----
        // Q: ChikoBlue をそのまま
        _qPaint = ResPaint("ChikoBlue", 2);

        // V / 各圧力 / Temp / RPM は「合う色」をここで管理
        // ※ここは後で好みに合わせて調整しやすいように固定値でOK
        _vPaint = new SolidColorPaint(SKColor.Parse("#35D0C6"), 2);

        _opPaint = new SolidColorPaint(SKColor.Parse("#FF5A5A"), 2);
        _spPaint = new SolidColorPaint(SKColor.Parse("#6FD14C"), 2);
        _dpPaint = new SolidColorPaint(SKColor.Parse("#4FB3FF"), 2);
        _epPaint = new SolidColorPaint(SKColor.Parse("#7A63FF"), 2);

        _tempPaint = ResPaint("ActionOrange", 2);
        _rpmPaint = new SolidColorPaint(SKColor.Parse("#2BE3A0"), 2);
    }
  
    private Axis BuildLeftAxis(double maxLimit, double step)
    {
        bool useDecimal = step < 1;

        return new Axis
        {
            Name = null,
            TextSize = 10,
            NameTextSize = 10,
            Position = AxisPosition.Start,

            MinLimit = 0,
            MaxLimit = maxLimit,

            LabelsPaint = _axisTextPaint,
            NamePaint = _axisNamePaint,
            SeparatorsPaint = _gridPaint,

            // ★ラベル増殖対策：刻みを固定
            MinStep = step,
            ForceStepToMin = true,

            // ★桁の増殖を止める（小刻みだけ小数1桁）
            Labeler = v => v.ToString(useDecimal ? "0.0" : "0"),
        };
    }

    private Axis BuildRightAxis(double maxLimit, double step)
    {
        bool useDecimal = step < 1;

        return new Axis
        {
            Name = null,
            TextSize = 10,
            NameTextSize = 10,
            Position = AxisPosition.End,

            MinLimit = 0,
            MaxLimit = maxLimit,

            LabelsPaint = _axisTextPaint,
            NamePaint = _axisNamePaint,
            SeparatorsPaint = _gridPaint,

            // ★ラベル増殖対策：刻みを固定
            MinStep = step,
            ForceStepToMin = true,

            Labeler = v => v.ToString(useDecimal ? "0.0" : "0"),
        };
    }


    private void InitChart()
    {
        // ★ ResourceDictionary から Paint を構築（最重要）
        BuildChartThemeFromResources();

        ChartXAxes.Clear();
        ChartYAxes.Clear();
        ChartSections.Clear();
        ChartSeries.Clear();

        ChartXAxes.Add(new Axis
        {
            Name = null,
            TextSize = 10,
            NameTextSize = 10,

            LabelsPaint = _axisTextPaint,
            NamePaint = _axisNamePaint,

            SeparatorsPaint = null,
            TicksPaint = null,
            SubticksPaint = null,

            MinLimit = 0,
            MaxLimit = 60,
            MinStep = 10,

            CustomSeparators = new double[] { 0, 10, 20, 30, 40, 50, 60 },

            // ★WinFormsの AxisX.IsReversed = true 相当（0秒を右端に）
            IsInverted = true,

            // ★表示はそのまま “秒”
            Labeler = v => $"{v:0}s",
        });

        //// 左軸：Q/V/P
        //ChartYAxes.Add(new Axis
        //{
        //    Name = null,
        //    TextSize = 10,
        //    NameTextSize = 10,
        //    Position = AxisPosition.Start,

        //    MinLimit = 0,
        //    MaxLimit = 100,

        //    LabelsPaint = _axisTextPaint,
        //    NamePaint = _axisNamePaint,
        //    SeparatorsPaint = _gridPaint,

        //    // ★追加：刻み制御（最重要）
        //    MinStep = 10,              // 初期値（AutoScaleで後で更新）
        //    ForceStepToMin = true,     // MinStepを強制
        //    Labeler = v => v.ToString("0"),  // 桁を増やさない
        //});

        //// 右軸：Temp / RPM
        //ChartYAxes.Add(new Axis
        //{
        //    Name = null,
        //    TextSize = 10,
        //    NameTextSize = 10,
        //    Position = AxisPosition.End,

        //    MinLimit = 0,
        //    MaxLimit = 100,

        //    LabelsPaint = _axisTextPaint,
        //    NamePaint = _axisNamePaint,
        //    SeparatorsPaint = _gridPaint,

        //    // ★追加：刻み制御（最重要）
        //    MinStep = 10,
        //    ForceStepToMin = true,
        //    Labeler = v => v.ToString("0"),
        //});

        ChartYAxes.Add(BuildLeftAxis(100, 10));
        ChartYAxes.Add(BuildRightAxis(100, 10));

        EnsurePointsInitialized(_q);
        EnsurePointsInitialized(_v);
        EnsurePointsInitialized(_op);
        EnsurePointsInitialized(_sp);
        EnsurePointsInitialized(_dp);
        EnsurePointsInitialized(_ep);
        EnsurePointsInitialized(_temp);
        EnsurePointsInitialized(_rpmK);

        RebuildChartSeries();
        UpdateBaselineSection();

        // ★ XAML 側が LegendTextPaint/FramePaint/Tooltip... を参照してるなら通知
        OnPropertyChanged(nameof(LegendTextPaint));
        OnPropertyChanged(nameof(FramePaint));
        OnPropertyChanged(nameof(TooltipTextPaint));
        OnPropertyChanged(nameof(TooltipBgPaint));
    }

    private void RebuildChartSeries()
    {
        ChartSeries.Clear();

        if (ShowQ) ChartSeries.Add(MakeLine("Q", _q, yAt: 0));
        if (ShowV) ChartSeries.Add(MakeLine("V", _v, yAt: 0));
        if (ShowOP) ChartSeries.Add(MakeLine("OP", _op, yAt: 0));
        if (ShowSP) ChartSeries.Add(MakeLine("SP", _sp, yAt: 0));
        if (ShowDP) ChartSeries.Add(MakeLine("DP", _dp, yAt: 0));
        if (ShowEP) ChartSeries.Add(MakeLine("EP", _ep, yAt: 0));
        if (ShowTemp) ChartSeries.Add(MakeLine("Temp(÷10)", _temp, yAt: 1));
        if (ShowRpm) ChartSeries.Add(MakeLine("RPM(×1000)", _rpmK, yAt: 1));

        foreach (var s in ChartSeries.OfType<LineSeries<ObservablePoint>>())
        {
            s.Fill = null;
            s.GeometryFill = null;
            s.GeometryStroke = null;
        }

        // ★ここでは points 再構築しない。バッファから反映するだけ。
        ApplyBufferToPoints(_q, _bufQ);
        ApplyBufferToPoints(_v, _bufV);
        ApplyBufferToPoints(_op, _bufOP);
        ApplyBufferToPoints(_sp, _bufSP);
        ApplyBufferToPoints(_dp, _bufDP);
        ApplyBufferToPoints(_ep, _bufEP);
        ApplyBufferToPoints(_temp, _bufTemp);
        ApplyBufferToPoints(_rpmK, _bufRpmK);

        UpdateYAxisAutoScale();
    }

    private void UpdateYAxisAutoScale()
    {
        if (ChartYAxes.Count < 2) return;

        // ★ 1秒に1回まで（500msポーリングなら十分）
        var now = DateTimeOffset.Now;
        if ((now - _lastAxisUpdateAt).TotalMilliseconds < 1000)
            return;

        _lastAxisUpdateAt = now;

        // ---- 左軸（Q/V/P）最大値 ----
        double leftMax = 0;
        if (ShowQ) leftMax = Math.Max(leftMax, _bufQ.Max());
        if (ShowV) leftMax = Math.Max(leftMax, _bufV.Max());
        if (ShowOP) leftMax = Math.Max(leftMax, _bufOP.Max());
        if (ShowSP) leftMax = Math.Max(leftMax, _bufSP.Max());
        if (ShowDP) leftMax = Math.Max(leftMax, _bufDP.Max());
        if (ShowEP) leftMax = Math.Max(leftMax, _bufEP.Max());

        // ---- 右軸（Temp / RPMk）最大値 ----
        double rightMax = 0;
        if (ShowTemp) rightMax = Math.Max(rightMax, _bufTemp.Max());
        if (ShowRpm) rightMax = Math.Max(rightMax, _bufRpmK.Max());

        double leftLimit = CalcNiceMax(leftMax);
        double rightLimit = CalcNiceMax(rightMax);
        rightLimit = Math.Max(rightLimit, 20); // ★最低でも 0..20（= 20000rpm相当）

        double leftStep = CalcNiceStep(leftLimit);
        double rightStep = CalcNiceStep(rightLimit);
        rightStep = Math.Max(rightStep, 5); // ★2刻み以上（0,2,4...）

        // ★ 差分が小さいなら更新しない（無駄な再レイアウトを止める）
        bool leftChanged =
            !AlmostEquals(leftLimit, _lastLeftLimit) ||
            !AlmostEquals(leftStep, _lastLeftStep);

        bool rightChanged =
            !AlmostEquals(rightLimit, _lastRightLimit) ||
            !AlmostEquals(rightStep, _lastRightStep);

        if (!leftChanged && !rightChanged)
            return;

        _lastLeftLimit = leftLimit;
        _lastRightLimit = rightLimit;
        _lastLeftStep = leftStep;
        _lastRightStep = rightStep;

        var left = ChartYAxes[0];
        var right = ChartYAxes[1];

        if (leftChanged)
        {
            left.MinLimit = 0;
            left.MaxLimit = leftLimit;
            left.MinStep = leftStep;
            left.ForceStepToMin = true;
            left.Labeler = v => v.ToString(leftStep < 1 ? "0.0" : "0");
        }

        if (rightChanged)
        {
            right.MinLimit = 0;
            right.MaxLimit = rightLimit;
            right.MinStep = rightStep;
            right.ForceStepToMin = true;
            right.Labeler = v => v.ToString(rightStep < 1 ? "0.0" : "0");
        }

        Log($"[AXIS] leftLimit={leftLimit} step={leftStep} rightLimit={rightLimit} step={rightStep}");
    }

    private static bool AlmostEquals(double a, double b, double eps = 1e-9)
    {
        if (double.IsNaN(a) && double.IsNaN(b)) return true;
        if (double.IsNaN(a) || double.IsNaN(b)) return false;
        return Math.Abs(a - b) <= eps;
    }


    // ★「ラベル増殖」対策のための刻み決定
    private static double CalcNiceStep(double maxLimit)
    {
        if (maxLimit <= 0) return 1;

        // 目盛りを最大でも 6〜7個程度に抑える
        // 例: max=10 -> step=2
        //     max=50 -> step=10
        //     max=5  -> step=1
        double raw = maxLimit / 5.0;

        // “キリの良い刻み” に丸める
        double pow = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        double n = raw / pow;

        double nice =
            n <= 1 ? 1 :
            n <= 2 ? 2 :
            n <= 5 ? 5 :
            10;

        double step = nice * pow;

        // 0.1未満の刻みを禁止（ここが白帯の最大原因になりやすい）
        if (step < 0.1) step = 0.1;

        return step;
    }

    private static double CalcNiceMax(double max)
    {
        if (max <= 0) return 10;

        // 10% 余白
        var target = max * 1.1;

        // “キリの良い”目盛りに丸める（ざっくり）
        // 例: 37 -> 50, 103 -> 200, 5.2 -> 10
        double pow = Math.Pow(10, Math.Floor(Math.Log10(target)));
        double n = target / pow;

        double nice =
            n <= 1 ? 1 :
            n <= 2 ? 2 :
            n <= 5 ? 5 :
            10;

        return nice * pow;
    }

    private void ResetChartForNewConnection()
    {
        Array.Clear(_bufQ);
        Array.Clear(_bufV);
        Array.Clear(_bufOP);
        Array.Clear(_bufSP);
        Array.Clear(_bufDP);
        Array.Clear(_bufEP);
        Array.Clear(_bufTemp);
        Array.Clear(_bufRpmK);

        BaselineFlow = null;

        // pointsは消さない。Yを0に戻すだけ（固定長で維持）
        ApplyBufferToPoints(_q, _bufQ);
        ApplyBufferToPoints(_v, _bufV);
        ApplyBufferToPoints(_op, _bufOP);
        ApplyBufferToPoints(_sp, _bufSP);
        ApplyBufferToPoints(_dp, _bufDP);
        ApplyBufferToPoints(_ep, _bufEP);
        ApplyBufferToPoints(_temp, _bufTemp);
        ApplyBufferToPoints(_rpmK, _bufRpmK);

        ResetYAxisToDefault();
        RebuildChartSeries();
    }

    private void ResetYAxisToDefault()
    {
        if (ChartYAxes.Count < 2) return;

        _lastLeftLimit = -1;
        _lastRightLimit = -1;
        _lastLeftStep = -1;
        _lastRightStep = -1;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            ChartYAxes[0] = BuildLeftAxis(100, 10);
            ChartYAxes[1] = BuildRightAxis(100, 10);
        });
    }

    private SolidColorPaint GetSeriesStroke(string name)
    {
        // name は RebuildChartSeries で渡しているものと一致させる
        return name switch
        {
            "Q" => _qPaint,
            "V" => _vPaint,
            "OP" => _opPaint,
            "SP" => _spPaint,
            "DP" => _dpPaint,
            "EP" => _epPaint,
            "Temp(÷10)" => _tempPaint,
            "RPM(×1000)" => _rpmPaint,
            _ => _qPaint
        };
    }

    private ISeries MakeLine(string name, ObservableCollection<ObservablePoint> values, int yAt)
=> new LineSeries<ObservablePoint>
{
    Name = name,
    Values = values,

    GeometrySize = 0,
    GeometryFill = null,
    GeometryStroke = null,
    Fill = null,

    ScalesYAt = yAt,
    LineSmoothness = 0,
    Stroke = GetSeriesStroke(name),

    AnimationsSpeed = TimeSpan.Zero,
    EasingFunction = null,

    // ★追加：ホバー/ツールチップ/ラベルを殺す
    IsHoverable = false,
    DataLabelsPaint = null,
    DataLabelsSize = 0,
};


    private void UpdateBaselineSection()
    {
        ChartSections.Clear();

        if (BaselineFlow is null) return;

        ChartSections.Add(new RectangularSection
        {
            Label = "Baseline Q",
            Yi = BaselineFlow.Value,
            Yj = BaselineFlow.Value,
            Stroke = _sectionBaselinePaint,
            IsVisible = true
        });
    }

    // =========================
    // SCAN
    // =========================
    private async Task ScanAsync()
    {
        Log("[SCAN] tapped");

        if (IsScanning) { Log("[SCAN] already scanning"); return; }

        _scanCts?.Cancel();
        _scanCts = new CancellationTokenSource();
        var ct = _scanCts.Token;

        try
        {
            IsScanning = true;

            OverlayState =
                IsWifiSelected ? OverlayMode.ScanWifi :
                IsBluetoothSelected ? OverlayMode.ScanBluetooth :
                OverlayMode.ScanBle;

            Devices.Clear();

            Log($"[SCAN] mode wifi={IsWifiSelected} ble={IsBleSelected} bt={IsBluetoothSelected}");

            if (IsWifiSelected)
            {
#if ANDROID
                var st = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
                Log("[PERM] LocationWhenInUse = " + st);
                if (st != PermissionStatus.Granted)
                    return;
#endif
#if IOS
                var st = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
                Log("[PERM] iOS LocationWhenInUse = " + st);
                if (st != PermissionStatus.Granted)
                {
                    if (ShowAlertRequested != null)
                        await ShowAlertRequested("権限が必要", "iOSでSSID取得には位置情報(使用中)の許可が必要です。設定で許可してください。");
                    return;
                }
#endif
                if (_wifiApScanner is null)
                {
                    if (ShowAlertRequested != null)
                        await ShowAlertRequested("未対応", "このOSではWi-Fi検索が未対応です。");
                    return;
                }

                var ssids = await _wifiApScanner.ScanSsidsAsync(ct);

                if (ssids.Count == 0)
                {
                    if (ShowAlertRequested != null)
                        await ShowAlertRequested(
                            "Wi-Fi未接続",
                            "iOS/Windowsでは周囲Wi-Fiの一覧取得ができません。\n設定アプリで「CHIKO-型式-製造番号-AP」に接続してからSCANしてください。");
                    return;
                }

                var shown = 0;
                foreach (var s in ssids.Where(x =>
                    x.StartsWith("CHIKO-", StringComparison.OrdinalIgnoreCase) &&
                    x.EndsWith("-AP", StringComparison.OrdinalIgnoreCase)))
                {
                    Devices.Add(new DeviceItem(s, "Wi-Fi / AP"));
                    shown++;
                }

                if (shown == 0 && ShowAlertRequested != null)
                {
                    await ShowAlertRequested(
                        "接続先が違います",
                        $"現在のWi-Fi: {ssids[0]}\n「CHIKO-型式-製造番号-AP」に接続してください。");
                }

                return;
            }

            if (IsBluetoothSelected)
            {
                if (_classicBtScanner is null)
                {
                    if (ShowAlertRequested != null)
                        await ShowAlertRequested("未対応", "このOSではClassic Bluetoothスキャンが未対応です。");
                    return;
                }

                Log("[BT] scan start");
                var list = await _classicBtScanner.ScanAsync(ct);
                Log($"[BT] found={list.Count}");

                var shown = 0;
                foreach (var d in list.Where(x => x.Name.StartsWith("CHIKO-", StringComparison.OrdinalIgnoreCase)))
                {
                    Devices.Add(new DeviceItem(d.Name, $"BT / {d.Address}"));
                    shown++;
                }

                Log($"[BT] shown={shown}");
                if (shown == 0 && ShowAlertRequested != null)
                    await ShowAlertRequested("見つかりません", "CHIKO- で始まるBluetooth機器が見つかりませんでした。");

                return;
            }

            // BLE
            if (_bleScanner is null)
            {
                if (ShowAlertRequested != null)
                    await ShowAlertRequested("未対応", "このOSではBLEスキャンが未対応です。");
                return;
            }

            Log("[BLE] scan start");
            var ble = await _bleScanner.ScanAsync(ct);
            Log($"[BLE] found={ble.Count}");

            var bleShown = 0;
            foreach (var d in ble.Where(x => x.Name.StartsWith("CHIKO-", StringComparison.OrdinalIgnoreCase)))
            {
                Devices.Add(new DeviceItem(d.Name, $"BLE / {d.Id}"));
                bleShown++;
            }

            if (bleShown == 0 && ShowAlertRequested != null)
                await ShowAlertRequested("見つかりません", "CHIKO- で始まるBLE機器が見つかりませんでした。");
        }
        catch (OperationCanceledException)
        {
            Log("[SCAN] canceled");
        }
        catch (Exception ex)
        {
            Log("[SCAN] error: " + ex);
        }
        finally
        {
            IsScanning = false;
            OverlayState = OverlayMode.None;
            Log("[SCAN] done");
        }
    }

    // =========================
    // CONNECT
    // =========================
    private async Task ConnectAsync(DeviceItem? device)
    {
#if ANDROID
        var st = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
        if (st != PermissionStatus.Granted)
        {
            if (ShowAlertRequested != null)
                await ShowAlertRequested("権限が必要", "Wi-Fi接続には位置情報(使用中)の許可が必要です。");
            return;
        }
#endif
        device ??= SelectedDevice;
        if (device == null) return;

        if (!await _connLock.WaitAsync(0))
            return;

        try
        {
            OverlayState =
                IsWifiSelected ? OverlayMode.ConnectWifi :
                IsBluetoothSelected ? OverlayMode.ConnectBluetooth :
                OverlayMode.ConnectBle;

            IsUiEnabled = false;

            // ★前回の監視停止（念のため）
            StopMonitoring();

            // ===== Wi-Fi(TCP) =====
            if (IsWifiSelected)
            {
                string ssid = device.DisplayName;

#if ANDROID
                var pass = await PromptAsync(
                    "Wi-Fi Password",
                    $"SSID: {ssid}\nパスワードを入力してください。",
                    "password",
                    isPassword: true);

                if (string.IsNullOrWhiteSpace(pass))
                    return;

                if (_wifiSsidConnector is null)
                    throw new InvalidOperationException("Wi-Fi接続(SSID指定)が未対応です。");

                using (var leaseCts = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
                {
                    if (_activeWifiLease != null)
                    {
                        try { await _activeWifiLease.DisposeAsync(); } catch { }
                        _activeWifiLease = null;
                    }

                    _activeWifiLease = await _wifiSsidConnector.ConnectAsync(ssid, pass, leaseCts.Token);
                }
#endif
                string host = "192.168.100.1";
                int port = 10001;

                // ★接続＋初期化は短いCTSでOK
                using var connectCts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                var tcp = await ChikoStreamClient.ConnectTcpAsync(host, port, connectCts.Token);

                var (model, serial, program, pipeMm, s5) = await InitAfterConnectedAsync(tcp, connectCts.Token);

                ModelName = model;
                SerialNumber = serial;
                if (pipeMm.HasValue) PipeDiameterInput = pipeMm.Value.ToString();

                // ★S5反映（後述のプロパティへ）
                ApplyS5ToUi(s5);

                _activeClient = tcp;
                IsConnected = true;

                // ★ここを追加
                ResetChartForNewConnection();

                // ★監視は「切断まで」生きる別CTSで開始（重要）
                StartMonitoring();

                return;
            }

            // ===== Classic BT =====
            if (IsBluetoothSelected)
            {
                if (_classicBtConnector is null)
                {
                    if (ShowAlertRequested != null)
                        await ShowAlertRequested("未対応", "このOSではClassic BT接続が未対応です。");
                    return;
                }

                var addr = device.Detail.Replace("BT /", "", StringComparison.OrdinalIgnoreCase).Trim();

                using var connectCts =
#if WINDOWS
                    new CancellationTokenSource(TimeSpan.FromSeconds(45));
#else
                    new CancellationTokenSource(TimeSpan.FromSeconds(12));
#endif
                var link = await _classicBtConnector.ConnectAsync(addr, connectCts.Token);
                var client = ChikoStreamClient.FromStream(link.Stream);

                var (model, serial, program, pipeMm, s5) = await InitAfterConnectedAsync(client, connectCts.Token);
                ModelName = model;
                SerialNumber = serial;
                if (pipeMm.HasValue) PipeDiameterInput = pipeMm.Value.ToString();

                // ★S5反映（後述のプロパティへ）
                ApplyS5ToUi(s5);


                _activeLink = link;
                _activeClient = client;
                IsConnected = true;

                // ★ここを追加
                ResetChartForNewConnection();

                StartMonitoring();
                return;
            }

            // ===== BLE =====
            if (IsBleSelected)
            {
                if (_bleConnector is null)
                {
                    if (ShowAlertRequested != null)
                        await ShowAlertRequested("未対応", "このOSではBLE接続が未対応です。");
                    return;
                }

                var id = device.Detail.Replace("BLE /", "", StringComparison.OrdinalIgnoreCase).Trim();

                using var connectCts =
#if WINDOWS
                    new CancellationTokenSource(TimeSpan.FromSeconds(45));
#else
                    new CancellationTokenSource(TimeSpan.FromSeconds(15));
#endif
                var link = await _bleConnector.ConnectAsync(id, connectCts.Token);
                var client = ChikoStreamClient.FromStream(link.Stream);

                var (model, serial, program, pipeMm, s5) = await InitAfterConnectedAsync(client, connectCts.Token);
                ModelName = model;
                SerialNumber = serial;
                if (pipeMm.HasValue) PipeDiameterInput = pipeMm.Value.ToString();

                // ★S5反映（後述のプロパティへ）
                ApplyS5ToUi(s5);


                _activeLink = link;
                _activeClient = client;
                IsConnected = true;

                // ★ここを追加
                ResetChartForNewConnection();

                StartMonitoring();
                return;
            }

            if (ShowAlertRequested != null)
                await ShowAlertRequested("未実装", "接続方式が選択されていません。");
        }
        catch (Exception ex)
        {
            Log("[CONNECT] error: " + ex);
            if (ShowAlertRequested != null)
                await ShowAlertRequested("接続エラー", ex.Message);
        }
        finally
        {
            IsUiEnabled = true;
            OverlayState = OverlayMode.None;
            _connLock.Release();
        }
    }

    private static async Task<(string model, string serial, string program, int? pipeMm, ChikoTpSession.S5Settings? s5)>
    InitAfterConnectedAsync(ChikoStreamClient client, CancellationToken ct)
    {
        var session = new ChikoTpSession(client);

        await session.SendControlEnableAsync(ct);  // W10 0100
        await session.SendWriteFlagAsync(ct);      // W81 0100

        var (model, serial, program) = await session.ReadModelSerialAsync(ct);

        ChikoTpSession.S5Settings? s5 = null;
        try
        {
            s5 = await session.ReadS5Async(ct);   // ★追加：R S5 0000
        }
        catch { /* S5失敗でも接続は続行 */ }

        int? pipe = null;

        // ★S5の配管径が有効ならそれを採用
        if (s5 != null && s5.PipeDiameterMm > 0)
            pipe = s5.PipeDiameterMm;

        // ★取れない場合だけ R0F で補完
        if (!pipe.HasValue)
        {
            try { pipe = await session.ReadPipeDiameterAsync(ct); } catch { }
        }

        return (model, serial, program, pipe, s5);
    }

    private void ApplyS5ToUi(ChikoTpSession.S5Settings? s5)
    {
        if (s5 == null)
        {
            IsShakingVisible = false;
            IsPulseEnabled = false;
            return;
        }

        // 表示条件はまず単純に：搭載/許可がtrueなら表示
        IsShakingVisible = s5.ShakingEnabled;

        IsPulseEnabled = s5.PulseEnabled;

        // 配管径はInitAfterConnectedAsyncで既に入れてるが、
        // ここで入れる設計にするなら統一してもOK
        if (s5.PipeDiameterMm > 0)
            PipeDiameterInput = s5.PipeDiameterMm.ToString();
    }


    // =========================
    // 監視（初回S4→Lv反映 → 500ms S4）
    // =========================
    private void StartMonitoring()
    {
        if (_activeClient is null) return;

        StopMonitoring();

        _tp = new ChikoTpSession(_activeClient);
        _monitorCts = new CancellationTokenSource();
        var ct = _monitorCts.Token;

        _ = Task.Run(async () =>
        {
            await FirstS4AndApplyAsync(ct);

            // 初回に失敗してもポーリングは開始（復旧狙い）
            StartS4Polling(ct);
        }, ct);
    }

    private async Task FirstS4AndApplyAsync(CancellationToken ct)
    {
        _levelInitialized = false;

        try
        {
            Log("[S4] first read...");

            using var one = CancellationTokenSource.CreateLinkedTokenSource(ct);
            one.CancelAfter(TimeSpan.FromSeconds(3));

            var first = await _tp!.ReadS4Async(one.Token);

            MainThread.BeginInvokeOnMainThread(() =>
            {
                // ★初回Lv反映は送信抑制
                _suppressLevelSend = true;
                try
                {
                    ApplyS4ToUi(first, updateLevel: true);
                    BaselineFlow ??= (double)first.Volume_m3min; // 初回のみ基準線を置く
                    _levelInitialized = true;
                }
                finally
                {
                    _suppressLevelSend = false;
                }
            });

            Log("[S4] first applied");
        }
        catch (Exception ex)
        {
            Log("[S4] first read error: " + ex);
        }
    }

    private long _s4Tick;
    private DateTimeOffset _lastS4OkAt;

    private void StartS4Polling(CancellationToken monitorCt)
    {
        StopS4Polling();

        if (_tp is null) return;

        _s4Cts = CancellationTokenSource.CreateLinkedTokenSource(monitorCt);
        var ct = _s4Cts.Token;

        _s4Timer = new PeriodicTimer(TimeSpan.FromMilliseconds(500));

        _ = Task.Run(async () =>
        {
            Log("[S4] polling start 500ms");

            try
            {
                while (_s4Timer != null && await _s4Timer.WaitForNextTickAsync(ct))
                {
                    if (_tp is null) continue;

                    var tick = Interlocked.Increment(ref _s4Tick);

                    try
                    {
                        using var one = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        one.CancelAfter(TimeSpan.FromSeconds(2));

                        var s4 = await _tp.ReadS4Async(one.Token);

                        _lastS4OkAt = DateTimeOffset.Now;

                        MainThread.BeginInvokeOnMainThread(() =>
                        {
                            ApplyS4ToUi(s4, updateLevel: false);
                            AppendChartPoint(s4); // ★追加：運転ON時のみ中で判定
                        });

                        // ★成功ログ（重いので10回に1回だけ）
                        if (tick % 10 == 0)
                        {
                            Log($"[S4] ok tick={tick} lv={s4.Level} Q={s4.Volume_m3min:0.00} OP={s4.Outside_kPa:0.00}");
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // 2秒タイムアウト or Disconnect によるキャンセル
                        if (ct.IsCancellationRequested)
                            Log("[S4] tick canceled by stop");
                        else
                            Log("[S4] tick timeout (2s)");
                    }
                    catch (Exception ex)
                    {
                        Log("[S4] poll error: " + ex);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                Log("[S4] polling canceled");
            }
            finally
            {
                Log("[S4] polling end");
            }
        }, ct);
    }


    private void StopMonitoring()
    {
        try { _monitorCts?.Cancel(); } catch { }
        try { _monitorCts?.Dispose(); } catch { }
        _monitorCts = null;

        StopS4Polling();

        _tp = null;
        _levelInitialized = false;
        _suppressLevelSend = false;

        try { _levelSendCts?.Cancel(); } catch { }
        try { _levelSendCts?.Dispose(); } catch { }
        _levelSendCts = null;
    }

    private void StopS4Polling()
    {
        try { _s4Cts?.Cancel(); } catch { }

        try { _s4Timer?.Dispose(); } catch { }
        try { _s4Cts?.Dispose(); } catch { }

        _s4Cts = null;
        _s4Timer = null;
    }

    private void ApplyS4ToUi(ChikoTpSession.S4Status s4, bool updateLevel)
    {
        VolumeText = s4.Volume_m3min.ToString("0.00");
        VelocityText = s4.DuctVelocity_ms.ToString("0.00");
        SpeedText = s4.Speed_rpm.ToString("0");
        TempText = s4.BlowerTemp_C.ToString("0.0");

        OutsideText = s4.Outside_kPa.ToString("0.00");
        SuctionText = s4.Suction_kPa.ToString("0.00");
        DifferentialText = s4.Diff_kPa.ToString("0.00");
        ExhaustText = s4.Exhaust_kPa.ToString("0.00");

        // ★追加：エラー表示更新（C/C++と同じ挙動）
        UpdateWarnErrUi(s4.ErrorValue);

        // ★初回のみLvを反映（以後はスライダーで送信）
        if (updateLevel && !_levelInitialized)
            Level = s4.Level;
        // ★運転状態（RUN/STOP）を反映
        try
        {
            IsRunning = s4.IsRun;   // ← ここは S4Status のプロパティ名に合わせる
        }
        catch { }
    }

    private long _chartTick;

    private void AppendChartPoint(ChikoTpSession.S4Status s4)
    {
        var t = Interlocked.Increment(ref _chartTick);
        if (t % 10 == 0)
            Log($"[CHART] run={(s4.IsRun ? 1 : 0)} Q={s4.Volume_m3min:0.00} V={s4.DuctVelocity_ms:0.00}");

        if (!s4.IsRun) return;

        // ★WinFormsと同じ：最新をbuf[0]へ、後ろへシフト
        Push(_bufQ, (double)s4.Volume_m3min);
        Push(_bufV, (double)s4.DuctVelocity_ms);

        Push(_bufOP, (double)s4.Outside_kPa);
        Push(_bufSP, (double)s4.Suction_kPa);
        Push(_bufDP, (double)s4.Diff_kPa);
        Push(_bufEP, (double)s4.Exhaust_kPa);

        Push(_bufTemp, (double)s4.BlowerTemp_C / 10.0); // ★Tempを1/10で描画
        Push(_bufRpmK, s4.Speed_rpm / 1000.0);

        // ★pointsは固定長、Yだけ上書き
        ApplyBufferToPoints(_q, _bufQ);
        ApplyBufferToPoints(_v, _bufV);
        ApplyBufferToPoints(_op, _bufOP);
        ApplyBufferToPoints(_sp, _bufSP);
        ApplyBufferToPoints(_dp, _bufDP);
        ApplyBufferToPoints(_ep, _bufEP);
        ApplyBufferToPoints(_temp, _bufTemp);
        ApplyBufferToPoints(_rpmK, _bufRpmK);

        UpdateYAxisAutoScale();
    }

    private static void EnsurePointsInitialized(ObservableCollection<ObservablePoint> dst)
    {
        if (dst.Count == ChartPointCount) return;

        dst.Clear();
        for (int i = 0; i < ChartPointCount; i++)
        {
            double x = i * SampleIntervalSeconds; // 0..60
            dst.Add(new ObservablePoint(x, 0));
        }
    }

    private static void ApplyBufferToPoints(ObservableCollection<ObservablePoint> dst, double[] buf)
    {
        // dstは固定長(121)の前提
        for (int i = 0; i < buf.Length; i++)
            dst[i].Y = buf[i];
    }


    private static void RebuildPoints(ObservableCollection<ObservablePoint> dst, double[] buf)
    {
        dst.Clear();
        for (int i = 0; i < buf.Length; i++)
        {
            // X=秒前（0が最新、60が古い）
            double x = i * SampleIntervalSeconds;
            dst.Add(new ObservablePoint(x, buf[i]));
        }
    }

    private static void AddPoint(ObservableCollection<ObservablePoint> list, double x, double y)
    {
        list.Add(new ObservablePoint(x, y));
        while (list.Count > ChartMaxPoints)
            list.RemoveAt(0);
    }


    // =========================
    // Lv送信（debounce）
    // =========================
    private async Task SendLevelDebouncedAsync(int level)
    {
        try
        {
            _levelSendCts?.Cancel();
            _levelSendCts?.Dispose();
            _levelSendCts = new CancellationTokenSource();
            var ct = _levelSendCts.Token;

            await Task.Delay(200, ct);

            // monitorCt ではなく、送信単体の短いtimeout
            using var one = CancellationTokenSource.CreateLinkedTokenSource(ct);
            one.CancelAfter(TimeSpan.FromSeconds(3));

            if (!IsConnected || _tp is null) return;
            if (!_levelInitialized) return;

            Log("[LEVEL] send " + level);
            await _tp.SetLevelAsync(level, one.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log("[LEVEL] error: " + ex);
        }
    }

    // =========================
    // 操作（RUN / STOP / ERR CLR / LEVEL +/-）
    // =========================
    private async Task RunAsync()
    {
        try
        {
            if (!IsConnected || _tp is null) return;
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await _tp.SetRunAsync(true, cts.Token);
        }
        catch (Exception ex)
        {
            Log("[RUN] error: " + ex);
        }
    }

    private async Task StopAsync()
    {
        try
        {
            if (!IsConnected || _tp is null) return;
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await _tp.SetRunAsync(false, cts.Token);
        }
        catch (Exception ex)
        {
            Log("[STOP] error: " + ex);
        }
    }

    private async Task ErrorClearAsync()
    {
        try
        {
            if (!IsConnected || _tp is null) return;
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await _tp.ClearErrorAsync(cts.Token);
        }
        catch (Exception ex)
        {
            Log("[ERRCLR] error: " + ex);
        }
    }

    private async Task ShakingAsync()
    {
        try
        {
            if (!IsConnected || _tp is null) return;
            if (!IsShakingVisible) return;

            // ★STOP時のみ許可
            if (IsRunning)
            {
                Log("[SHAKING] blocked: machine is RUNNING");

                if (ShowAlertRequested != null)
                    await ShowAlertRequested("操作不可", "手動シェーキングは停止中(STOP)のみ実行できます。");

                return;
            }

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await _tp.SetShakingAsync(cts.Token);

            Log("[SHAKING] sent");
        }
        catch (Exception ex)
        {
            Log("[SHAKING] error: " + ex);

            if (ShowAlertRequested != null)
                await ShowAlertRequested("シェーキングエラー", ex.Message);
        }
    }

    private async Task SetInitialFlowAsync()
    {
        try
        {
            if (!IsConnected || _tp is null) return;

            // ★S4ポーリングと競合させない（重要）
            // StopS4Polling() で timer を止め、送信後に再開する。
            // （monitorCt は _monitorCts とリンクさせる）
            StopS4Polling();

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await _tp.SetInitialFlowAsync(cts.Token);

            Log("[INITFLOW] sent W03 0100");

            // ★確認（任意推奨）：直後にS4を1回読んで d4 を見たい場合
            // → 今のS4Statusには d4 を持っていないので、下の「3)」で追加する
            // using var cts2 = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            // var s4 = await _tp.ReadS4Async(cts2.Token);
            // Log($"[INITFLOW] after S4 flag={s4.InitialFlowFlag}");
        }
        catch (Exception ex)
        {
            Log("[INITFLOW] error: " + ex);
            if (ShowAlertRequested != null)
                await ShowAlertRequested("初期風量登録エラー", ex.Message);
        }
        finally
        {
            // ★ポーリング再開（監視が生きている場合のみ）
            if (_monitorCts != null && !_monitorCts.IsCancellationRequested)
                StartS4Polling(_monitorCts.Token);
        }
    }

    private async Task SetPipeDiameterAsync()
    {
        try
        {
            if (!IsConnected || _tp is null) return;

            if (!int.TryParse(PipeDiameterInput?.Trim(), out var mm))
            {
                if (ShowAlertRequested != null)
                    await ShowAlertRequested("入力エラー", "配管径(mm)は数値で入力してください。");
                return;
            }

            // 仕様：0 または 20..180
            if (!(mm == 0 || (mm >= 20 && mm <= 180)))
            {
                if (ShowAlertRequested != null)
                    await ShowAlertRequested("入力範囲", "配管径(mm)は 0 または 20〜180 の範囲です。");
                return;
            }

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await _tp.SetPipeDiameterAsync(mm, cts.Token);

            Log("[PIPE] set " + mm);

            // 反映確認（任意だが、入れた方が分かりやすい）
            using var cts2 = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var readBack = await _tp.ReadPipeDiameterAsync(cts2.Token);
            if (readBack.HasValue)
                PipeDiameterInput = readBack.Value.ToString();
        }
        catch (Exception ex)
        {
            Log("[PIPE] error: " + ex);
            if (ShowAlertRequested != null)
                await ShowAlertRequested("配管径設定エラー", ex.Message);
        }
    }

    private async Task ChangeLevelAsync(int delta)
    {
        try
        {
            if (!IsConnected || _tp is null) return;

            var next = Math.Clamp(Level + delta, 1, 15);

            // UIは即反映（S4でLv更新しない設計）
            Level = next;

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await _tp.SetLevelAsync(next, cts.Token);

            Log("[LEVEL] set " + next);
        }
        catch (Exception ex)
        {
            Log("[LEVEL] error: " + ex);
        }
    }

    // =========================
    // UI reset (after disconnect)
    // =========================
    private void ResetUiAfterDisconnect()
    {
        // ---- 機器情報 ----
        ModelName = "";
        SerialNumber = "";

        // ---- 運転状態 ----
        IsRunning = false;
        _levelInitialized = false;
        _suppressLevelSend = false;

        // Lv は「未接続の見た目」を決めて固定（例：15でも1でもOK）
        Level = 15;

        // ---- 配管径入力（残したいならコメントアウト）----
        PipeDiameterInput = "";

        // ---- エラー/CAUTION表示 ----
        IsWarnErrVisible = false;
        ErrorNo = "";
        ErrorTitle = "";
        ErrorCause = "";
        ErrorAccentColor = Colors.Transparent;
        WarnErrOpacity = 1.0;

        SpeedCardBg = null;
        TempCardBg = null;
        SuctionCardBg = null;
        ExhaustCardBg = null;

        StopWarnErrBlink();
        _isFirstErrorDisplay = true;

        // ---- メトリクス ----
        VolumeText = "0.00";
        VelocityText = "0.00";
        SpeedText = "0";
        TempText = "0.0";
        OutsideText = "0.00";
        SuctionText = "0.00";
        DifferentialText = "0.00";
        ExhaustText = "0.00";

        // ---- グラフ ----
        ResetChartForNewConnection(); // 既にあなたが実装済み。これでゼロに戻る

        // ---- Overlay/Scan 表示（念のため）----
        OverlayState = OverlayMode.None;
        IsScanning = false;

        // ---- 選択デバイス（残したいならコメントアウト）----
        SelectedDevice = null;
    }

    // =========================
    // DISCONNECT
    // =========================
    private async Task DisconnectAsync()
    {
        if (!await _connLock.WaitAsync(0))
            return;

        try
        {
            if (!IsConnected)
                return;

            IsUiEnabled = false;

            // ★最優先：監視停止（Dispose中に走るのを防ぐ）
            StopMonitoring();

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await DisableControlAndDisposeAsync(cts.Token);

            if (_activeWifiLease != null)
            {
                try { await _activeWifiLease.DisposeAsync(); } catch { }
                _activeWifiLease = null;
            }
        }
        finally
        {
            // ★ここで必ず画面表示を初期化（例外でも実行）
            MainThread.BeginInvokeOnMainThread(() =>
            {
                ResetUiAfterDisconnect();
            });

            IsUiEnabled = true;
            OverlayState = OverlayMode.None;
            _connLock.Release();
        }
    }

    // =========================
    // Hamburger Menu Navigation
    // =========================
    private async Task OpenGeneralSettingsAsync()
    {
        try
        {
            IsMenuOpen = false;
            IsUiEnabled = true;

            await Shell.Current.Navigation.PushAsync(new GeneralSettingsPage());
        }
        catch (Exception ex)
        {
            Log("[MENU] OpenGeneralSettings error: " + ex);

            if (ShowAlertRequested != null)
                await ShowAlertRequested("画面遷移エラー", $"一般設定画面を開けませんでした。\n{ex.Message}");
        }
    }

    private async Task OpenErrorHistoryAsync()
    {
        try
        {
            IsMenuOpen = false;
            IsUiEnabled = true;

            await Shell.Current.Navigation.PushAsync(new ErrorHistoryPage());
        }
        catch (Exception ex)
        {
            Log("[MENU] OpenErrorHistory error: " + ex);

            if (ShowAlertRequested != null)
                await ShowAlertRequested("画面遷移エラー", $"エラー発生履歴画面を開けませんでした。\n{ex.Message}");
        }
    }

    private async Task OpenOperationHistoryAsync()
    {
        try
        {
            IsMenuOpen = false;
            IsUiEnabled = true;

            await Shell.Current.Navigation.PushAsync(new OperationHistoryPage());
        }
        catch (Exception ex)
        {
            Log("[MENU] OpenOperationHistory error: " + ex);

            if (ShowAlertRequested != null)
                await ShowAlertRequested("画面遷移エラー", $"稼働履歴画面を開けませんでした。\n{ex.Message}");
        }
    }

    private async Task OpenAdminSettingsAsync()
    {
        if (_isAdminAuthOpening) return;

        _isAdminAuthOpening = true;

        try
        {
            IsMenuOpen = false;
            IsUiEnabled = true;

            const string adminPassword = "1234";

            var page = Application.Current?.Windows.FirstOrDefault()?.Page;
            if (page is null)
            {
                if (ShowAlertRequested != null)
                    await ShowAlertRequested("画面エラー", "現在の画面を取得できませんでした。");
                return;
            }

            var input = await MainThread.InvokeOnMainThreadAsync(() =>
                PasswordPromptPage.ShowAsync(
                    page,
                    "管理者認証",
                    "管理者設定を開くにはパスワードを入力してください。",
                    "Password"));

            if (string.IsNullOrWhiteSpace(input))
                return;

            if (input != adminPassword)
            {
                if (ShowAlertRequested != null)
                    await ShowAlertRequested("認証エラー", "パスワードが正しくありません。");
                return;
            }

            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                await Shell.Current.Navigation.PushAsync(new AdminSettingsPage(), false);
            });
        }
        catch (Exception ex)
        {
            Log("[MENU] OpenAdminSettings error: " + ex);

            if (ShowAlertRequested != null)
                await ShowAlertRequested("画面遷移エラー", $"管理者設定画面を開けませんでした。\n{ex.Message}");
        }
        finally
        {
            _isAdminAuthOpening = false;
        }
    }

    // ★必ず CONTROL_FLG=0000 を送ってから切断する
    private async Task DisableControlAndDisposeAsync(CancellationToken ct)
    {
        try
        {
            if (_activeClient != null)
            {
                Log("[DISC] CONTROL_FLG OFF (0000)");
                var session = new ChikoTpSession(_activeClient);
                await session.SendControlDisableAsync(ct);
            }
        }
        catch (Exception ex)
        {
            Log("[DISC] disable error: " + ex);
        }
        finally
        {
            if (_activeClient != null)
            {
                try { await _activeClient.DisposeAsync(); } catch { }
                _activeClient = null;
            }

            if (_activeLink != null)
            {
                try { await _activeLink.DisposeAsync(); } catch { }
                _activeLink = null;
            }

            IsConnected = false;
        }
    }
    private enum ErrKind { None, Err, Caution }

    private sealed record ErrUi(
        ErrKind Kind,
        string No,
        string Title,
        string Cause,
        bool HiRpm,
        bool HiTemp,
        bool HiSp,
        bool HiEp
    );

    private static ErrUi ResolveErrUi(ushort errorValue)
    {
        if (errorValue == 0)
            return new ErrUi(ErrKind.None, "", "", "", false, false, false, false);

        // ---- ERR bits 0..7（ERR01が最優先）----
        for (int bit = 0; bit <= 7; bit++)
        {
            if ((errorValue & (1u << bit)) == 0) continue;

            int no = bit + 1; // ERR01..ERR08

            return no switch
            {
                2 => new ErrUi(ErrKind.Err, "ERR02", "INV error detected",
                    "Detecting the abnormality signal from inverter",
                    HiRpm: false, HiTemp: false, HiSp: false, HiEp: false),

                3 => new ErrUi(ErrKind.Err, "ERR03", "RPM fault",
                    "- The blower RPM is low\n- The blower is not running",
                    HiRpm: true, HiTemp: false, HiSp: false, HiEp: false),

                4 => new ErrUi(ErrKind.Err, "ERR04", "Internal temperature fault",
                    "The temperature around the motor is too high",
                    HiRpm: false, HiTemp: true, HiSp: false, HiEp: false),

                6 => new ErrUi(ErrKind.Err, "ERR06", "Pressure fault",
                    "The operation continued for a certain time at insufficient pressure",
                    HiRpm: false, HiTemp: false, HiSp: true, HiEp: false),

                8 => new ErrUi(ErrKind.Err, "ERR08", "Run button ground fault",
                    "Ground fault detected on the run button line",
                    HiRpm: false, HiTemp: false, HiSp: false, HiEp: false),

                _ => new ErrUi(ErrKind.Err, $"ERR{no:00}", "", "", false, false, false, false),
            };
        }

        // ---- CAUTION bits 8..15（CAUTION01が上、CAUTION08が最下）----
        for (int bit = 8; bit <= 15; bit++)
        {
            if ((errorValue & (1u << bit)) == 0) continue;

            int no = bit - 7; // CAUTION01..08

            return no switch
            {
                1 => new ErrUi(ErrKind.Caution, "CAUTION01", "Internal temperature rise",
                    "The blower ambient temperature is close to the fault threshold",
                    HiRpm: false, HiTemp: true, HiSp: false, HiEp: false),

                3 => new ErrUi(ErrKind.Caution, "CAUTION03", "Insufficient pressure (suction)",
                    "The suction pressure is low",
                    HiRpm: false, HiTemp: false, HiSp: true, HiEp: false),

                4 => new ErrUi(ErrKind.Caution, "CAUTION04", "Insufficient airflow",
                    "Airflow is low due to a clogged filter",
                    HiRpm: false, HiTemp: false, HiSp: false, HiEp: false),

                5 => new ErrUi(ErrKind.Caution, "CAUTION05", "Exhaust pressure fault",
                    "- The airflow rate is low due to a clogged filter\n- The exhaust port may be blocked",
                    HiRpm: false, HiTemp: false, HiSp: false, HiEp: true),

                8 => new ErrUi(ErrKind.Caution, "CAUTION08", "Remote Lock Warning",
                    "The remote function locks for safety under certain conditions",
                    HiRpm: false, HiTemp: false, HiSp: false, HiEp: false),

                _ => new ErrUi(ErrKind.Caution, $"CAUTION{no:00}", "", "", false, false, false, false),
            };
        }

        return new ErrUi(ErrKind.Caution, "CAUTION??", "", "", false, false, false, false);
    }


    private void UpdateWarnErrUi(ushort errorValue)
    {
        var ui = ResolveErrUi(errorValue);

        // 0ならクリア（Cコード同様）
        if (ui.Kind == ErrKind.None)
        {
            IsWarnErrVisible = false;
            ErrorNo = "";
            ErrorTitle = "";
            ErrorCause = "";
            ErrorAccentColor = Colors.Transparent;

            SpeedCardBg = null;
            TempCardBg = null;
            SuctionCardBg = null;
            ExhaustCardBg = null;

            StopWarnErrBlink();
            _isFirstErrorDisplay = true;
            return;
        }

        // 表示内容
        ErrorNo = ui.No;
        ErrorTitle = ui.Title;
        ErrorCause = ui.Cause;

        var accent = (ui.Kind == ErrKind.Err) ? Color.FromArgb("#FF0000") : Color.FromArgb("#FFB000");
        ErrorAccentColor = accent;

        // 強調（LVGLは背景色だったので、こちらも背景で強調）
        var hiBrush = new SolidColorBrush(accent);

        SpeedCardBg = ui.HiRpm ? hiBrush : null;
        TempCardBg = ui.HiTemp ? hiBrush : null;
        SuctionCardBg = ui.HiSp ? hiBrush : null;
        ExhaustCardBg = ui.HiEp ? hiBrush : null;

        // 初回は即表示＋点滅開始（Cコード同様）
        if (_isFirstErrorDisplay)
        {
            IsWarnErrVisible = true;
            _isFirstErrorDisplay = false;
            StartWarnErrBlink();
            return;
        }

        // 2回目以降はタイマーが無ければ開始
        IsWarnErrVisible = true;
        StartWarnErrBlink();
    }

    private void StartWarnErrBlink()
    {
        if (_warnErrBlinkCts != null) return;

        _warnErrBlinkCts = new CancellationTokenSource();
        var ct = _warnErrBlinkCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    await Task.Delay(2000, ct); // 2000ms（Cコードと同じ）
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        // 1.0 ↔ 0.35
                        WarnErrOpacity = (WarnErrOpacity > 0.7) ? 0.35 : 1.0;
                    });
                }
            }
            catch (OperationCanceledException) { }
        }, ct);
    }

    private void StopWarnErrBlink()
    {
        try { _warnErrBlinkCts?.Cancel(); } catch { }
        try { _warnErrBlinkCts?.Dispose(); } catch { }
        _warnErrBlinkCts = null;

        WarnErrOpacity = 1.0;
    }



    public sealed record DeviceItem(string DisplayName, string Detail);
}
