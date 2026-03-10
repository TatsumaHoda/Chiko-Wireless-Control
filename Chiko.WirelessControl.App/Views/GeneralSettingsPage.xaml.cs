using Chiko.WirelessControl.App.ViewModels;

namespace Chiko.WirelessControl.App.Views;

public partial class GeneralSettingsPage : ContentPage
{
    private readonly GeneralSettingsViewModel _vm;

    public GeneralSettingsPage(MainPageViewModel? sourceVm = null)
    {
        InitializeComponent();

        _vm = new GeneralSettingsViewModel(sourceVm);
        _vm.LogRequested += OnLogRequested;
        _vm.ShowAlertRequested += OnShowAlertRequested;

        BindingContext = _vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.LoadOnOpenAsync();
    }

    protected override void OnDisappearing()
    {
        _vm.LogRequested -= OnLogRequested;
        _vm.ShowAlertRequested -= OnShowAlertRequested;
        _vm.Dispose();
        base.OnDisappearing();
    }

    private static void OnLogRequested(string msg)
    {
        System.Diagnostics.Debug.WriteLine(msg);
    }

    private Task OnShowAlertRequested(string title, string message)
        => DisplayAlert(title, message, "OK");

    private async void VolumeDownRateEntry_Completed(object? sender, EventArgs e)
        => await _vm.CommitVolumeDownRateAsync();

    private async void VolumeDownRateEntry_Unfocused(object? sender, FocusEventArgs e)
        => await _vm.CommitVolumeDownRateAsync();

    private async void RemoteOutputSignalPicker_SelectedIndexChanged(object? sender, EventArgs e)
        => await _vm.CommitRemoteOutputSignalAsync();

    private async void ShakingIntervalPicker_SelectedIndexChanged(object? sender, EventArgs e)
        => await _vm.CommitShakingIntervalAsync();

    private async void ShakingOperatingTimePicker_SelectedIndexChanged(object? sender, EventArgs e)
        => await _vm.CommitShakingOperatingTimeAsync();

    private async void PulseIntervalEntry_Completed(object? sender, EventArgs e)
        => await _vm.CommitPulseIntervalAsync();

    private async void PulseIntervalEntry_Unfocused(object? sender, FocusEventArgs e)
        => await _vm.CommitPulseIntervalAsync();

    private async void AutoPulseSwitch_Toggled(object? sender, ToggledEventArgs e)
        => await _vm.CommitAutoPulseAsync();
}
