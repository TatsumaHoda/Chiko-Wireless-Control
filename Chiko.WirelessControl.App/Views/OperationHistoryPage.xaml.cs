using Chiko.WirelessControl.App.ViewModels;

namespace Chiko.WirelessControl.App.Views;

public partial class OperationHistoryPage : ContentPage
{
    public OperationHistoryPage(MainPageViewModel source)
    {
        InitializeComponent();
        BindingContext = new OperationHistoryViewModel(source);
    }
}

