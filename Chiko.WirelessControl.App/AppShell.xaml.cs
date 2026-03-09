namespace Chiko.WirelessControl.App
{
    public partial class AppShell : Shell
    {
        public AppShell(MainPage mainPage)
        {
            InitializeComponent();

            // ★ Shell のルートを MainPage にする（new しない）
            Items.Clear();
            Items.Add(new ShellContent
            {
                Content = mainPage,
                Route = nameof(MainPage)
            });
        }
    }
}
