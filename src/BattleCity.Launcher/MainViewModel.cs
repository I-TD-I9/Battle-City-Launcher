using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using BattleCity.Client;
using BattleCity.Game;

namespace BattleCity.Launcher;

/// <summary>Everything the launcher's window shows and does.</summary>
public sealed class MainViewModel : ObservableObject
{
    readonly LauncherSettings settings = LauncherSettings.Load(LauncherSettings.DefaultPath);
    readonly ServerEndpoints server;
    // The window's thread, where a game that exits on its own is reported back.
    readonly SynchronizationContext ui = SynchronizationContext.Current ?? new SynchronizationContext();

    public MainViewModel()
    {
        server = settings.Server();
    }

    /// <summary>Finds the game.</summary>
    public async Task StartAsync()
    {
        Game = await Task.Run(() => GameLocator.Find(settings.GameFolder));
    }

    // --- The game --------------------------------------------------------------------------

    GameFolder? game;
    bool starting;
    bool running;
    string? playError;

    public GameFolder? Game
    {
        get => game;
        private set
        {
            if (Set(ref game, value))
                Raise(nameof(GameFolderText), nameof(GameReady), nameof(GameStatusText), nameof(CanPlay));
        }
    }

    public string GameFolderText => game?.Path ?? "Not found";

    public bool GameReady => game?.Status == GameStatus.Ready;

    public string GameStatusText => game?.Status switch
    {
        GameStatus.Ready => "Ready to play.",
        GameStatus.Unsupported => "This copy of the game is another version. Battle City needs the last update, from June 2005.",
        GameStatus.NoClient => "This folder has no online_pc.dat. Choose the folder Yu-Gi-Oh! ONLINE is installed in.",
        GameStatus.NotFound => "This folder no longer exists. Choose the folder Yu-Gi-Oh! ONLINE is installed in.",
        _ => "The game was not found. Choose the folder Yu-Gi-Oh! ONLINE is installed in.",
    };

    public bool Starting
    {
        get => starting;
        private set
        {
            if (Set(ref starting, value))
                Raise(nameof(CanPlay), nameof(PlayText));
        }
    }

    public bool Running
    {
        get => running;
        private set
        {
            if (Set(ref running, value))
                Raise(nameof(CanPlay), nameof(PlayText));
        }
    }

    public bool CanPlay => GameReady && !starting && !running;

    public string PlayText => running ? "GAME RUNNING" : starting ? "STARTING" : "PLAY";

    public string? PlayError
    {
        get => playError;
        private set => Set(ref playError, value);
    }

    /// <summary>The player chose a folder: look at it, and remember it if the game is there.</summary>
    public void ChooseFolder(string path)
    {
        try
        {
            Game = GameFolder.Inspect(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Game = new GameFolder(path, GameStatus.NotFound, null);
            PlayError = $"The launcher could not read that folder: {e.Message}";
            return;
        }
        PlayError = null;
        if (Game.Status != GameStatus.Ready)
            return;
        settings.GameFolder = Game.Path;
        try
        {
            settings.Save(LauncherSettings.DefaultPath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Only the choice is lost: the next start asks for the folder again.
        }
    }

    public async Task PlayAsync()
    {
        if (game is not { Status: GameStatus.Ready } current || !CanPlay)
            return;
        PlayError = null;
        Starting = true;
        try
        {
            string client;
            try
            {
                client = await Task.Run(() => GameStarter.Prepare(current, server, LauncherSettings.Folder));
            }
            catch (PatchException e)
            {
                PlayError = $"The launcher could not prepare the game: {e.Message}.";
                return;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Most often the copy is still running from an earlier Play and cannot be replaced.
                PlayError = $"The launcher could not read the game or write its copy of it. If the game is open, close it and try again. ({e.Message})";
                return;
            }

            try
            {
                var process = GameStarter.Start(client, current);
                Running = true;
                process.Exited += (_, _) => ui.Post(_ => Running = false, null);
                process.EnableRaisingEvents = true;
                if (process.HasExited)
                    Running = false;
            }
            catch (Exception e) when (e is Win32Exception or InvalidOperationException)
            {
                PlayError = $"Windows could not start the game: {e.Message}";
            }
        }
        finally
        {
            Starting = false;
        }
    }

    // --- The launcher itself -----------------------------------------------------------------

    public string VersionText => $"v{Releases.Current}";

    /// <summary>Opens a page in the player's browser.</summary>
    public static void Open(Uri page) =>
        Process.Start(new ProcessStartInfo(page.AbsoluteUri) { UseShellExecute = true })?.Dispose();
}
