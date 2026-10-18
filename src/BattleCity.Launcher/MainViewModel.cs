using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using BattleCity.Accounts;
using BattleCity.Client;
using BattleCity.Display;
using BattleCity.Game;
using BattleCity.Pictures;

namespace BattleCity.Launcher;

public enum ServerState
{
    Checking,
    Online,

    /// <summary>The sign-up API answers but the gate cannot be reached: a network blocking the game's ports.</summary>
    Blocked,

    Offline,
}

/// <summary>What the account page shows.</summary>
public enum AccountPage
{
    /// <summary>The last account made or given a new password here, and a button for another.</summary>
    LastAccount,

    /// <summary>UserID, email address and password for a new account.</summary>
    SignUp,

    /// <summary>The code mailed for the new account.</summary>
    SignUpCode,

    /// <summary>The email address of an account whose password is forgotten.</summary>
    Forgot,

    /// <summary>The code mailed for it, and the new password.</summary>
    ForgotCode,
}

/// <summary>Everything the launcher's window shows and does.</summary>
public sealed class MainViewModel : ObservableObject
{
    readonly LauncherSettings settings = LauncherSettings.Load(LauncherSettings.DefaultPath);
    readonly ServerEndpoints server;
    readonly HttpClient http;
    readonly SignUpClient signUp;
    readonly CardPictures pictures;
    // The window's thread, where a game that exits on its own is reported back.
    readonly SynchronizationContext ui = SynchronizationContext.Current ?? new SynchronizationContext();

    readonly bool elevated = Elevation.ByRequest();

    SignUpRules rules = SignUpRules.Default;
    bool rulesKnown;

    public MainViewModel()
    {
        server = settings.Server();
        // Every reply the launcher reads whole is a few kilobytes, and nothing bigger is held in
        // memory: the card pictures are read as they come (CardPictures).
        http = new HttpClient { Timeout = TimeSpan.FromSeconds(20), MaxResponseContentBufferSize = 1 << 20 };
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("BattleCityLauncher", Releases.Current.ToString()));
        signUp = new SignUpClient(http, server.Api);
        pictures = new CardPictures(http, server.Api);
        page = Start;
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
        GameStatus.Unsupported => $"This copy of the game is another version. Battle City needs the February 2006 client, {SupportedClient.Version}.",
        GameStatus.DataMissing => "The game's data is missing from this folder: it needs patch.txt and merge.dat beside online_pc.dat. Unpack the whole folder again.",
        GameStatus.NoClient => "This folder has no online_pc.dat. Choose the Yu-Gi-Oh! ONLINE folder.",
        GameStatus.NotFound => "This folder no longer exists. Choose the Yu-Gi-Oh! ONLINE folder.",
        _ => "The game was not found. Choose the Yu-Gi-Oh! ONLINE folder.",
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

    public bool CanPlay => GameReady && !starting && !running && !elevated && !outdated;

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
        SaveSettings();
    }

    void SaveSettings()
    {
        try
        {
            settings.Save(LauncherSettings.DefaultPath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Nothing breaks: the launcher just won't remember the folder, the last account or
            // the resolution next time.
        }
    }

    /// <summary>
    /// Makes the patched copy and starts it. <paramref name="window"/> is the launcher's own
    /// window: a bigger game window goes on its monitor.
    /// </summary>
    public async Task PlayAsync(IntPtr window)
    {
        if (game is not { Status: GameStatus.Ready } current || !CanPlay)
            return;
        PlayError = null;
        PicturesText = null;
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

            // A folder of the launcher's that the game can't be given keeps it in its own window.
            string? home = CncDdraw.Folder(LauncherSettings.Folder);
            var resolution = home is null ? Resolution.Default : settings.Resolution;
            try
            {
                WindowPlacement? placement = null;
                if (resolution.IsWindow)
                {
                    // The window that fits this monitor, which may not be the one chosen on another.
                    var area = Measure(window);
                    resolution = area.Effective(resolution);
                    placement = resolution.IsWindow ? area.Place(resolution) : null;
                }
                await Task.Run(() => CncDdraw.Prepare(resolution, LauncherSettings.Folder, placement));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // As with the copy, most often the game is still open and holds the DLL.
                PlayError = $"The launcher could not set the game up {Described(resolution)}. If the game is open, close it and try again, or choose another resolution. ({e.Message})";
                return;
            }

            string start = await PreparePicturesAsync(current);

            try
            {
                ClientKey.Prepare(ClientKey.SaveFolder, current.Path);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                PlayError = $"The launcher could not make the folder the game keeps its saves in. ({e.Message})";
                return;
            }

            try
            {
                var process = GameStarter.Start(client, start, resolution, home);
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

    static string Described(Resolution resolution) =>
        resolution.IsFullScreen ? "in full screen"
        : resolution.IsWindow ? $"in a {resolution.Width} x {resolution.Height} window"
        : "in its own window";

    // --- The card pictures -------------------------------------------------------------------

    string? picturesText;

    /// <summary>A line on how the card pictures stand, or null when the server hands out none.</summary>
    public string? PicturesText
    {
        get => picturesText;
        private set => Set(ref picturesText, value);
    }

    const string PicturesChecking = "Checking the card pictures…";
    const string PicturesUpToDate = "Card pictures up to date.";
    const string PicturesNotUpdated = "The card pictures could not be updated just now, so the game has the ones it had before.";
    const string PicturesLater =
        "Not all the card pictures came in time, so the game has the ones it had before. The rest come at the next Play.";
    const string PicturesUnwritten =
        "The card pictures could not all be written to the launcher's folder, so some may be old or missing.";
    const string PicturesNotSetUp = "The launcher could not set the card pictures up, so the game starts without them.";
    const string NoPicturesGameFolder =
        "No card pictures from the server with this game folder: its path is one the game can't take. A plain one, such as C:\\Games\\Yu-Gi-Oh, works.";
    const string NoPicturesLauncherFolder =
        "No card pictures from the server: the launcher's folder, under your Windows user name, has letters the game can't read.";

    // Writes the working folder's patch.txt and brings its pictures up to date, and returns the
    // folder to start the game in. The pictures never keep the game from starting for long
    // (CardPictures.Budget): whatever goes wrong, it starts with the ones it has, or with none,
    // from the game's own folder if need be.
    async Task<string> PreparePicturesAsync(GameFolder current)
    {
        string start;
        NoPictures? reason;
        try
        {
            (start, reason) = await Task.Run(() => WorkingFolder.Prepare(current, WorkingFolder.Path));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            PicturesText = PicturesNotSetUp;
            return WorkingFolder.Fallback(current);
        }
        if (reason is { } why)
        {
            PicturesText = why == NoPictures.LauncherFolder ? NoPicturesLauncherFolder : NoPicturesGameFolder;
            return start;
        }

        bool done = false;
        var progress = new Progress<PicturesProgress>(step =>
        {
            if (!done)
                PicturesText = step.Total == 0 ? PicturesChecking : $"Updating card pictures, {step.Done} of {step.Total}";
        });
        PicturesText = PicturesChecking;
        var outcome = await pictures.UpdateAsync(Path.Combine(WorkingFolder.Path, WorkingFolder.Overrides), progress);
        done = true;
        PicturesText = outcome switch
        {
            PicturesOutcome.None => null,
            PicturesOutcome.UpToDate => PicturesUpToDate,
            PicturesOutcome.Unfinished => PicturesLater,
            PicturesOutcome.Unwritable => PicturesUnwritten,
            _ => PicturesNotUpdated,
        };
        return start;
    }

    // --- The settings ------------------------------------------------------------------------

    WorkArea? workArea;
    bool scaled = true;
    bool listing;
    IReadOnlyList<ResolutionChoice> resolutions = [];

    /// <summary>
    /// The resolutions the Settings page's drop-down offers: the default, the bigger windows that
    /// fit the launcher's monitor, and full screen.
    /// </summary>
    public IReadOnlyList<ResolutionChoice> Resolutions
    {
        get => resolutions;
        private set
        {
            if (Set(ref resolutions, value))
                Raise(nameof(SharpOffered));
        }
    }

    /// <summary>
    /// The resolution the drop-down shows: what Play would use here. A window chosen for a
    /// bigger monitor than this one shows as the largest that fits, and stays chosen as it was
    /// until the player picks another. Picking one keeps it at once; the game takes it up at the
    /// next Play.
    /// </summary>
    public ResolutionChoice? ChosenResolution
    {
        get
        {
            if (listing)
                return null;
            var shown = workArea is { } area ? area.Effective(settings.Resolution, scaled) : settings.Resolution;
            return resolutions.FirstOrDefault(choice => choice.Resolution == shown);
        }
        set
        {
            // The drop-down sets none while its list is replaced.
            if (value is null)
                return;
            if (settings.Resolution != value.Resolution)
            {
                settings.Resolution = value.Resolution;
                SaveSettings();
            }
            Raise(nameof(ChosenResolution));
        }
    }

    /// <summary>Whether a window as sharp as can be is on offer, for the note on the star that marks it.</summary>
    public bool SharpOffered => resolutions.Any(choice => choice.Sharp);

    /// <summary>
    /// Whether the resolution can be changed at all: not when the launcher's folder is one the
    /// game can't be given (CncDdraw.Folder), and the game keeps its own window.
    /// </summary>
    public bool ResolutionChangeable => scaled;

    public bool ResolutionFixed => !scaled;

    /// <summary>Lists what fits the monitor <paramref name="window"/> is on, as the Settings page comes up.</summary>
    public void ShowResolutions(IntPtr window) => ShowResolutions(Measure(window));

    /// <summary>Lists what fits <paramref name="area"/>.</summary>
    public void ShowResolutions(WorkArea area)
    {
        workArea = area;
        scaled = CncDdraw.Folder(LauncherSettings.Folder) is not null;
        Raise(nameof(ResolutionChangeable), nameof(ResolutionFixed));
        // The drop-down lets go of its choice while its list is replaced, and is given the one to
        // show once the new list is in.
        listing = true;
        Resolutions = [.. area.Choices(scaled).Select(resolution => new ResolutionChoice(resolution))];
        listing = false;
        Raise(nameof(ChosenResolution));
    }

    // The monitor the launcher is on; when Windows can't say, none, where no bigger window fits.
    static WorkArea Measure(IntPtr window)
    {
        try
        {
            return WorkArea.Of(window);
        }
        catch (Win32Exception)
        {
            return default;
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
            var fetching = signUp.GetInfoAsync();
            var probing = ServerProbe.GateOpensAsync(server, TimeSpan.FromSeconds(6));
            var fetched = await fetching;
            bool gate = await probing;
            if (fetched is not null)
            {
                rules = fetched.Rules;
                rulesKnown = true;
                Raise(nameof(SignUpStateText));
                Outdated = fetched.TooOld(Releases.Current);
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

    // --- The account page ------------------------------------------------------------------

    AccountPage page;
    string userId = "";
    string email = "";
    string code = "";
    bool busy;
    string? signUpMessage;
    bool signUpSucceeded;
    // The account a mailed code is for, kept for a new code and for the confirmation.
    (string Name, string Password, string Email)? pendingSignUp;
    // The address a mailed code will set a new password for.
    string? pendingReset;
    // The UserID whose confirmation last got no answer: the server may have made it all the same.
    string? unanswered;
    // The same for a new password: the server may have set it all the same.
    string? unansweredReset;

    public AccountPage Page
    {
        get => page;
        private set
        {
            if (Set(ref page, value))
                Raise(nameof(ShowingLastAccount), nameof(ShowingSignUp), nameof(ShowingSignUpCode),
                      nameof(ShowingForgot), nameof(ShowingForgotCode));
        }
    }

    public bool ShowingLastAccount => page == AccountPage.LastAccount;
    public bool ShowingSignUp => page == AccountPage.SignUp;
    public bool ShowingSignUpCode => page == AccountPage.SignUpCode;
    public bool ShowingForgot => page == AccountPage.Forgot;
    public bool ShowingForgotCode => page == AccountPage.ForgotCode;

    public string UserId
    {
        get => userId;
        set => Set(ref userId, value);
    }

    public string Email
    {
        get => email;
        set => Set(ref email, value);
    }

    public string Code
    {
        get => code;
        set => Set(ref code, value);
    }

    /// <summary>The address the last code went to.</summary>
    public string? CodeSentTo => page == AccountPage.ForgotCode ? pendingReset : pendingSignUp?.Email;

    /// <summary>The UserID the code on the page will make.</summary>
    public string? PendingName => pendingSignUp?.Name;

    /// <summary>The UserID of the last account made or given a new password here, or null before the first.</summary>
    public string? LastAccount => settings.LastAccount;

    public bool HasLastAccount => !string.IsNullOrWhiteSpace(settings.LastAccount);

    public bool NoLastAccount => !HasLastAccount;

    /// <summary>A request to the server is on its way, and the page's buttons wait for it.</summary>
    public bool Busy
    {
        get => busy;
        private set
        {
            if (Set(ref busy, value))
                Raise(nameof(CanSubmit));
        }
    }

    public bool CanSubmit => !busy;

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

    // Once an account has been made here, the page shows it, and the form only comes up when the
    // player asks for another.
    AccountPage Start => HasLastAccount ? AccountPage.LastAccount : AccountPage.SignUp;

    // While a request is on its way the page stays where it is, so that its answer lands on the
    // page that asked.
    public void OpenForm()
    {
        if (!busy)
            Show(AccountPage.SignUp);
    }

    public void CloseForm()
    {
        if (busy)
            return;
        pendingSignUp = null;
        pendingReset = null;
        Show(Start);
    }

    public void OpenForgot()
    {
        if (!busy)
            Show(AccountPage.Forgot);
    }

    /// <summary>From a code back to the form that asked for it, and from a form to the start.</summary>
    public void Back()
    {
        if (busy)
            return;
        if (page == AccountPage.SignUpCode)
            Show(AccountPage.SignUp);
        else if (page == AccountPage.ForgotCode)
            Show(AccountPage.Forgot);
        else
            CloseForm();
    }

    void Show(AccountPage next)
    {
        Code = "";
        SignUpMessage = null;
        Page = next;
        Raise(nameof(CodeSentTo), nameof(PendingName));
    }

    string? Unusable =>
        outdated ? "This launcher is out of date. Download the new one to go on." : null;

    const string CodeShape = "The code is the 6 digits in the email.";
    const string Mismatch = "The two passwords are not the same.";
    const string Ready = "Your account is ready. Press Play and log in with this UserID and your password.";
    const string MadeAnyway = " If your last try got no answer, the account may have been made all the same: try logging in with it.";

    /// <summary>Asks the server for the account, which mails a code to its address; true once it has.</summary>
    public async Task<bool> SignUpAsync(string password, string confirm)
    {
        // Spaces are never part of a UserID or an address, so stray ones around them are dropped.
        string account = userId.Trim();
        string address = email.Trim();
        string? problem = Unusable
                          ?? rules.NameProblem(account)
                          ?? SignUpRules.EmailProblem(address)
                          ?? rules.PasswordProblem(password, account)
                          ?? (password == confirm ? null : Mismatch);
        if (problem is null && rulesKnown && !rules.Open)
            problem = "Sign-up is closed for now.";
        if (problem is not null)
        {
            Tell(problem, ok: false);
            return false;
        }
        if (await RequestAsync(() => signUp.RegisterAsync(account, password, address)) is not { } result)
            return false;
        if (!result.Ok)
        {
            if (result.Error == "email_taken")
                ToForgot(address);
            else
                Tell(result.Message, ok: false);
            return false;
        }
        // A server from before codes makes the account at once, and says so with 201.
        if (result.Status == 201)
        {
            Made(result.Name ?? account);
            return true;
        }
        pendingSignUp = (account, password, address);
        Show(AccountPage.SignUpCode);
        return true;
    }

    void Made(string name)
    {
        pendingSignUp = null;
        unanswered = null;
        UserId = "";
        Email = "";
        Remember(name);
        Tell(Ready, ok: true);
    }

    // The address already holds an account: the page that sets its password, the address filled in.
    void ToForgot(string address)
    {
        pendingSignUp = null;
        Email = address;
        Show(AccountPage.Forgot);
        Tell("That email address already has an account. If it's yours, ask for a code here to set a new password.", ok: false);
    }

    /// <summary>Sends the code the player typed; true once the server has made the account.</summary>
    public async Task<bool> ConfirmSignUpAsync()
    {
        if (pendingSignUp is not { } pending)
        {
            Show(AccountPage.SignUp);
            return false;
        }
        if (SignUpRules.Code(code) is not { } typed)
        {
            Tell(CodeShape, ok: false);
            return false;
        }
        if (await RequestAsync(() => signUp.ConfirmAsync(pending.Name, typed, pending.Password)) is not { } result)
            return false;
        if (result.Ok)
        {
            Made(result.Name ?? pending.Name);
            return true;
        }
        string message = Refusal(result);
        if (result.Error is "code_expired" or "name_taken" && unanswered == pending.Name)
            message += MadeAnyway;
        unanswered = result.Error == SignUpClient.Unreachable ? pending.Name : null;
        if (result.Error == "email_taken")
        {
            ToForgot(pending.Email);
            return false;
        }
        // Someone else made an account of that name first: back to the form to pick another.
        if (result.Error == "name_taken")
        {
            pendingSignUp = null;
            Show(AccountPage.SignUp);
        }
        Tell(message, ok: false);
        return false;
    }

    /// <summary>Asks the server to mail the account's code again, which makes the last one useless.</summary>
    public async Task SendSignUpCodeAgainAsync()
    {
        if (pendingSignUp is not { } pending
            || await RequestAsync(() => signUp.RegisterAsync(pending.Name, pending.Password, pending.Email)) is not { } result)
            return;
        Code = "";
        if (result.Ok)
            Tell("A new code is on its way.", ok: true);
        else
            Tell(result.Message + (result.Error == "name_taken" && unanswered == pending.Name ? MadeAnyway : ""), ok: false);
    }

    /// <summary>Asks for a code to set a new password; true once the server has taken the request.</summary>
    public async Task<bool> RequestResetAsync()
    {
        string address = email.Trim();
        if ((Unusable ?? SignUpRules.EmailProblem(address)) is { } problem)
        {
            Tell(problem, ok: false);
            return false;
        }
        if (await RequestAsync(() => signUp.RequestResetAsync(address)) is not { } result)
            return false;
        if (!result.Ok)
        {
            Tell(result.Message, ok: false);
            return false;
        }
        pendingReset = address;
        Show(AccountPage.ForgotCode);
        return true;
    }

    /// <summary>Asks the server to mail the reset code again.</summary>
    public async Task SendResetCodeAgainAsync()
    {
        if (pendingReset is not { } address
            || await RequestAsync(() => signUp.RequestResetAsync(address)) is not { } result)
            return;
        Code = "";
        if (result.Ok)
            Tell("If that address holds an account, a new code is on its way to it.", ok: true);
        else
            Tell(result.Message, ok: false);
    }

    /// <summary>Sets the new password with the mailed code; true once the server has.</summary>
    public async Task<bool> ResetPasswordAsync(string password, string confirm)
    {
        if (pendingReset is not { } address)
        {
            Show(AccountPage.Forgot);
            return false;
        }
        string? typed = SignUpRules.Code(code);
        string? problem = Unusable
                          ?? (typed is null ? CodeShape : null)
                          ?? rules.PasswordProblem(password, null)
                          ?? (password == confirm ? null : Mismatch);
        if (problem is not null)
        {
            Tell(problem, ok: false);
            return false;
        }
        if (await RequestAsync(() => signUp.ResetAsync(address, typed!, password)) is not { } result)
            return false;
        if (!result.Ok)
        {
            string message = Refusal(result);
            if (result.Error == "code_expired" && unansweredReset == address)
                message += " If your last try got no answer, your new password may be set all the same: try logging in with it.";
            unansweredReset = result.Error == SignUpClient.Unreachable ? address : null;
            Tell(message, ok: false);
            return false;
        }
        unansweredReset = null;
        pendingReset = null;
        Email = "";
        if (result.Name is { } name)
        {
            Remember(name);
            Tell($"Your password is changed. Press Play and log in as {name} with it.", ok: true);
        }
        else
        {
            Show(Start);
            Tell("Your password is changed.", ok: true);
        }
        return true;
    }

    // The account the player now holds, shown on the page from here on.
    void Remember(string name)
    {
        settings.LastAccount = name;
        SaveSettings();
        Raise(nameof(LastAccount), nameof(HasLastAccount), nameof(NoLastAccount));
        Show(AccountPage.LastAccount);
    }

    // A wrong code says how many tries it has left.
    static string Refusal(SignUpResult result) =>
        result.Error == "code_wrong" && result.TriesLeft is int left
            ? result.Message + (left == 1 ? " One try left." : $" {left} tries left.")
            : result.Message;

    async Task<SignUpResult?> RequestAsync(Func<Task<SignUpResult>> request)
    {
        if (busy)
            return null;
        Busy = true;
        SignUpMessage = null;
        try
        {
            return await request();
        }
        finally
        {
            Busy = false;
        }
    }

    void Tell(string message, bool ok)
    {
        SignUpSucceeded = ok;
        SignUpMessage = message;
    }

    // --- The launcher's own updates ----------------------------------------------------------

    LauncherRelease? update;
    bool outdated;

    public LauncherRelease? Update
    {
        get => update;
        private set
        {
            if (Set(ref update, value))
                Raise(nameof(ShowUpdate), nameof(UpdateText));
        }
    }

    /// <summary>
    /// The server takes no launcher this old any more (its GET /api/info says which it does), so
    /// the game isn't started from it and no account is made with it.
    /// </summary>
    public bool Outdated
    {
        get => outdated;
        private set
        {
            if (Set(ref outdated, value))
                Raise(nameof(CanPlay), nameof(ShowUpdate), nameof(UpdateText));
        }
    }

    public bool ShowUpdate => update is not null || outdated;

    public string UpdateText => outdated ? "This launcher is out of date."
                              : update is null ? "" : $"Version {update.Version} of the launcher is out.";

    /// <summary>Where to download the new launcher: the newer release found, else the latest one.</summary>
    public Uri UpdatePage => update?.Page ?? Releases.LatestPage;

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
