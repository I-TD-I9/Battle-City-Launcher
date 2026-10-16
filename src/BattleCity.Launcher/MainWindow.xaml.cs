using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
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
        // The title bar in the window's own paper and ink, where Windows lets it be coloured
        // (11 and later); elsewhere it keeps the system's.
        var window = new WindowInteropHelper(this).Handle;
        int paper = ColorRef(0xFB, 0xF8, 0xF1), ink = ColorRef(0x1C, 0x22, 0x30);
        _ = DwmSetWindowAttribute(window, DwmwaCaptionColor, ref paper, sizeof(int));
        _ = DwmSetWindowAttribute(window, DwmwaTextColor, ref ink, sizeof(int));
    }

    async void Play_Click(object sender, RoutedEventArgs e) => await model.PlayAsync();

    async void Refresh_Click(object sender, RoutedEventArgs e) => await model.CheckServerAsync();

    void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Choose the Yu-Gi-Oh! ONLINE folder, the one that holds online_pc.dat" };
        if (model.Game is { } game && System.IO.Directory.Exists(game.Path))
            dialog.InitialDirectory = game.Path;
        if (dialog.ShowDialog(this) == true)
            model.ChooseFolder(dialog.FolderName);
    }

    void NewAccount_Click(object sender, RoutedEventArgs e)
    {
        model.OpenForm();
        // The box only takes focus once the form is laid out.
        Dispatcher.BeginInvoke(() => UserIdBox.Focus(), DispatcherPriority.Input);
    }

    void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Password.Clear();
        Confirm.Clear();
        model.CloseForm();
    }

    async void SignUp_Click(object sender, RoutedEventArgs e)
    {
        if (await model.SignUpAsync(Password.Password, Confirm.Password))
        {
            Password.Clear();
            Confirm.Clear();
        }
    }

    void Update_Click(object sender, RoutedEventArgs e) => MainViewModel.Open(model.UpdatePage);

    void Project_Click(object sender, RoutedEventArgs e) => MainViewModel.Open(Releases.ProjectPage);

    const int DwmwaCaptionColor = 35;
    const int DwmwaTextColor = 36;

    static int ColorRef(byte red, byte green, byte blue) => red | green << 8 | blue << 16;

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
}
