using System.Windows;
using System.Windows.Threading;

namespace BattleCity.Launcher;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += ShowAndCarryOn;
        base.OnStartup(e);
    }

    // A failure nothing else caught is reported, and the launcher stays open: closing it would
    // take the player's half-typed sign-up with it. Before its window is up there is nothing to
    // stay open for.
    void ShowAndCarryOn(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show($"Something went wrong: {e.Exception.GetBaseException().Message}", "Battle City Launcher",
                        MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
        if (MainWindow is not { IsLoaded: true })
            Shutdown(1);
    }
}
