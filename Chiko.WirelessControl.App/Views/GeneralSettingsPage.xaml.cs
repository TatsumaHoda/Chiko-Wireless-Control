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

        BindingContext = _vm;
    }

    protected override void OnDisappearing()
    {
        _vm.LogRequested -= OnLogRequested;
        _vm.Dispose();
        base.OnDisappearing();
    }

    private static void OnLogRequested(string msg)
    {
        System.Diagnostics.Debug.WriteLine(msg);
    }
}
