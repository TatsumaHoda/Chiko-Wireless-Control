using Chiko.WirelessControl.App.ViewModels;
using Chiko.WirelessControl.App.Views;
using Microsoft.Maui.ApplicationModel;

namespace Chiko.WirelessControl.App;

public partial class MainPage : ContentPage
{
    private MainPageViewModel? _vm;
    private Frame? _controlsDialog;

    public MainPage(MainPageViewModel vm)
    {
        InitializeComponent();

        _vm = vm;
        BindingContext = vm;

        _controlsDialog = this.FindByName<Frame>("ControlsDialog");

        // ★イベント購読
        vm.ShowAlertRequested += OnShowAlertRequestedAsync;
        vm.PromptRequested += OnPromptRequestedAsync;
        vm.ControlsOpened += OnControlsOpened;
        vm.ControlsClosed += OnControlsClosed;

        Unloaded += OnUnloaded;
    }

    private Task OnShowAlertRequestedAsync(string title, string message)
        => MainThread.InvokeOnMainThreadAsync(() => DisplayAlert(title, message, "OK"));

    private void OnUnloaded(object? sender, EventArgs e)
    {
        if (_vm != null)
        {
            // ★解除漏れがないよう全部解除
            _vm.ShowAlertRequested -= OnShowAlertRequestedAsync;
            _vm.ControlsOpened -= OnControlsOpened;
            _vm.ControlsClosed -= OnControlsClosed;
            _vm.PromptRequested -= OnPromptRequestedAsync;
        }

        Unloaded -= OnUnloaded;

        _vm = null;
        _controlsDialog = null;
    }

    // ★DisplayPromptAsync に isPassword が無いので自前ページを使う
    private Task<string?> OnPromptRequestedAsync(string title, string message, string placeholder, bool isPassword)
    {
        return MainThread.InvokeOnMainThreadAsync(async () =>
        {
            if (isPassword)
            {
                return await PasswordPromptPage.ShowAsync(
                    this,
                    title,
                    message,
                    placeholder);
            }

            return await DisplayPromptAsync(
                title,
                message,
                accept: "OK",
                cancel: "Cancel",
                placeholder: placeholder,
                maxLength: 64,
                keyboard: Keyboard.Default,
                initialValue: "");
        });
    }

    private async void OnControlsOpened()
    {
        try
        {
            var dlg = _controlsDialog ??= this.FindByName<Frame>("ControlsDialog");
            if (dlg == null) return;

            dlg.Opacity = 0;
            dlg.Scale = 0.96;

            await Task.WhenAll(
                dlg.FadeTo(1, 140, Easing.CubicOut),
                dlg.ScaleTo(1.0, 160, Easing.CubicOut));
        }
        catch { }
    }

    private async void OnControlsClosed()
    {
        try
        {
            var dlg = _controlsDialog ??= this.FindByName<Frame>("ControlsDialog");
            if (dlg == null) return;

            await Task.WhenAll(
                dlg.FadeTo(0, 90, Easing.CubicIn),
                dlg.ScaleTo(0.98, 90, Easing.CubicIn));
        }
        catch { }
    }
}
