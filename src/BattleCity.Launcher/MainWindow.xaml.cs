using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;

namespace BattleCity.Launcher;

public partial class MainWindow : Window
{
    readonly MainViewModel model = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = model;
        Loaded += async (_, _) => await model.StartAsync();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        // A dark title bar over the dark window, where Windows offers one (10 20H1 and later).
        int dark = 1;
        _ = DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, DwmwaUseImmersiveDarkMode, ref dark, sizeof(int));
    }

    async void Play_Click(object sender, RoutedEventArgs e) => await model.PlayAsync();

    async void Refresh_Click(object sender, RoutedEventArgs e) => await model.CheckServerAsync();

    void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Choose the folder Yu-Gi-Oh! ONLINE is installed in" };
        if (model.Game is { } game && System.IO.Directory.Exists(game.Path))
            dialog.InitialDirectory = game.Path;
        if (dialog.ShowDialog(this) == true)
            model.ChooseFolder(dialog.FolderName);
    }

    async void SignUp_Click(object sender, RoutedEventArgs e)
    {
        if (await model.SignUpAsync(Password.Password, Confirm.Password))
        {
            Password.Clear();
            Confirm.Clear();
        }
    }

    void Update_Click(object sender, RoutedEventArgs e)
    {
        if (model.Update is { } update)
            MainViewModel.Open(update.Page);
    }

    void Project_Click(object sender, RoutedEventArgs e) => MainViewModel.Open(Releases.ProjectPage);

    const int DwmwaUseImmersiveDarkMode = 20;

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
}
