using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using BattleCity.Accounts;
using BattleCity.Client;
using BattleCity.Game;

namespace BattleCity.Launcher;

public enum ServerState
{
    Checking,
    Online,

    /// <summary>The sign-up API answers but the gate cannot be reached: a network blocking the game's ports.</summary>
    Blocked,

    Offline,
}

/// <summary>Everything the launcher's window shows and does.</summary>
public sealed class MainViewModel : ObservableObject
{
    readonly LauncherSettings settings = LauncherSettings.Load(LauncherSettings.DefaultPath);
    readonly ServerEndpoints server;
    readonly HttpClient http;
    readonly SignUpClient signUp;
    // The window's thread, where a game that exits on its own is reported back.
    readonly SynchronizationContext ui = SynchronizationContext.Current ?? new SynchronizationContext();

    readonly bool elevated = Elevation.ByRequest();

    SignUpRules rules = SignUpRules.Default;
    bool rulesKnown;

    public MainViewModel()
    {
        server = settings.Server();
        // Every reply the launcher reads is a few kilobytes; nothing bigger is held in memory.
        http = new HttpClient { Timeout = TimeSpan.FromSeconds(20), MaxResponseContentBufferSize = 1 << 20 };
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("BattleCityLauncher", Releases.Current.ToString()));
        signUp = new SignUpClient(http, server.Api);
    }

    /// <summary>Finds the game, then asks after the server and the launcher's own updates.</summary>
    public async Task StartAsync()
    {
        if (elevated)
            PlayError = AsAdministrator;
        Game = await Task.Run(() => GameLocator.Find(settings.GameFolder));
        await Task.WhenAll(CheckServerAsync(), CheckForUpdateAsync());
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

    public bool CanPlay => GameReady && !starting && !running && !elevated;

    const string AsAdministrator =
        "The launcher is running as administrator. Close it and start it again normally: the game must not run with administrator rights.";

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
        PlayError = elevated ? AsAdministrator : null;
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

    // --- The server ------------------------------------------------------------------------

    // Each check opens a connection to the gate, and the server limits how many new connections
    // one address may open in a minute, so a check waits for the one before and comes no sooner
    // than this after it.
    static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(10);

    ServerState serverState;
    bool checking;
    DateTime lastCheck = DateTime.MinValue;

    public ServerState ServerState
    {
        get => serverState;
        private set
        {
            if (Set(ref serverState, value))
                Raise(nameof(ServerText));
        }
    }

    public string ServerText => serverState switch
    {
        ServerState.Online => "Server online",
        ServerState.Blocked => "Server unreachable from this network",
        ServerState.Offline => "Server offline",
        _ => "Checking the server",
    };

    public string SignUpStateText => !rulesKnown ? "" : rules.Open ? "Sign-up open" : "Sign-up closed";

    public bool CanCheck => !checking;

    public async Task CheckServerAsync()
    {
        if (checking || DateTime.UtcNow - lastCheck < CheckInterval)
            return;
        checking = true;
        lastCheck = DateTime.UtcNow;
        Raise(nameof(CanCheck));
        ServerState = ServerState.Checking;
        try
        {
            var fetching = signUp.GetRulesAsync();
            var probing = ServerProbe.GateOpensAsync(server, TimeSpan.FromSeconds(6));
            var fetched = await fetching;
            bool gate = await probing;
            if (fetched is not null)
            {
                rules = fetched;
                rulesKnown = true;
                Raise(nameof(SignUpStateText));
            }
            ServerState = gate ? ServerState.Online : fetched is not null ? ServerState.Blocked : ServerState.Offline;
        }
        catch (Exception)
        {
            // A status check never takes the launcher down: whatever went wrong, the server could
            // not be reached.
            ServerState = ServerState.Offline;
        }
        finally
        {
            checking = false;
            Raise(nameof(CanCheck));
        }
    }

    // --- Sign-up ---------------------------------------------------------------------------

    string userId = "";
    // The UserID of the last sign-up that got no answer: the server may have made it all the same.
    string? unanswered;
    bool signingUp;
    string? signUpMessage;
    bool signUpSucceeded;

    public string UserId
    {
        get => userId;
        set => Set(ref userId, value);
    }

    public bool SigningUp
    {
        get => signingUp;
        private set
        {
            if (Set(ref signingUp, value))
                Raise(nameof(CanSignUp), nameof(SignUpText));
        }
    }

    public bool CanSignUp => !signingUp;

    public string SignUpText => signingUp ? "Creating the account" : "Create account";

    public string? SignUpMessage
    {
        get => signUpMessage;
        private set => Set(ref signUpMessage, value);
    }

    public bool SignUpSucceeded
    {
        get => signUpSucceeded;
        private set => Set(ref signUpSucceeded, value);
    }

    /// <summary>Signs up the account; true once the server has made it.</summary>
    public async Task<bool> SignUpAsync(string password, string confirm)
    {
        // Spaces are never part of a UserID, so stray ones around it are dropped.
        string account = userId.Trim();
        string? problem = rules.NameProblem(account)
                          ?? rules.PasswordProblem(password, account)
                          ?? (password == confirm ? null : "The two passwords are not the same.");
        if (problem is null && rulesKnown && !rules.Open)
            problem = "Sign-up is closed for now.";
        if (problem is not null)
        {
            Tell(problem, ok: false);
            return false;
        }

        SigningUp = true;
        try
        {
            var result = await signUp.RegisterAsync(account, password);
            string message = result.Ok
                ? $"The account {result.Name} is ready. Press Play, and log in with exactly this UserID and password."
                : result.Message;
            if (result.Error == "name_taken" && unanswered == account)
                message += " If your last try got no answer, the account may have been made all the same: try logging in with it.";
            unanswered = result.Error == SignUpClient.Unreachable ? account : null;
            Tell(message, result.Ok);
            return result.Ok;
        }
        finally
        {
            SigningUp = false;
        }
    }

    void Tell(string message, bool ok)
    {
        SignUpSucceeded = ok;
        SignUpMessage = message;
    }

    // --- The launcher's own updates ----------------------------------------------------------

    LauncherRelease? update;

    public LauncherRelease? Update
    {
        get => update;
        private set
        {
            if (Set(ref update, value))
                Raise(nameof(HasUpdate), nameof(UpdateText));
        }
    }

    public bool HasUpdate => update is not null;

    public string UpdateText => update is null ? "" : $"Version {update.Version} of the launcher is out.";

    public async Task CheckForUpdateAsync()
    {
        try
        {
            var latest = await Releases.LatestAsync(http);
            if (latest is not null && latest.Version > Releases.Current)
                Update = latest;
        }
        catch (Exception)
        {
            // Like the server's status this is only news: a failure leaves the banner hidden.
        }
    }

    // --- The launcher itself -----------------------------------------------------------------

    public string VersionText => $"v{Releases.Current}";

    /// <summary>Opens a page in the player's browser.</summary>
    public static void Open(Uri page) =>
        Process.Start(new ProcessStartInfo(page.AbsoluteUri) { UseShellExecute = true })?.Dispose();
}
