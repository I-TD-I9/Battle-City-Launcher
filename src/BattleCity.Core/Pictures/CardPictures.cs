using System.Net;
using System.Security.Cryptography;

namespace BattleCity.Pictures;

/// <summary>How bringing the card pictures up to date went.</summary>
public enum PicturesOutcome
{
    /// <summary>The server hands out no pictures, so bc was emptied.</summary>
    None,

    /// <summary>bc holds the server's pictures and nothing else.</summary>
    UpToDate,

    /// <summary>
    /// The time for it ran out before every file had come: bc is as it was, and the files that
    /// did come wait beside it for the next update.
    /// </summary>
    Unfinished,

    /// <summary>The server could not be reached, or was in no state to answer: bc is as it was.</summary>
    Unreachable,

    /// <summary>What the server gave, its list or a file on it, did not check out: bc is as it was.</summary>
    Refused,

    /// <summary>The files in bc could not all be read or written.</summary>
    Unwritable,
}

/// <summary>How far an update has got: the files fetched of those to fetch, both 0 while the ones there are checked.</summary>
public readonly record struct PicturesProgress(int Done, int Total);

/// <summary>
/// Keeps the launcher's folder of card pictures, bc in WorkingFolder, the same as the server's:
/// pictures for the cards the game itself has none for, and the two card lists that point at
/// them, data/card/list_card.txt and data/mini/list_card.txt, which the client reads (0x57eae8)
/// and looks for each card's picture beside (0x4a3937). It runs before the game starts and never
/// while it runs, since the client keeps what it has read.
/// </summary>
/// <remarks>
/// A file whose copy in bc has the size and hash the manifest gives stays; a missing or different
/// one is fetched, a few at a time, into a folder beside bc and checked there; and only once every
/// file has come do they go into place and whatever else is in bc go. So when the network or the
/// server fails halfway, or the time for it runs out, bc keeps the pictures it had, all of a
/// piece, and the next Play only fetches what it is still missing. A file the server fails to
/// give is asked for twice more before the update gives up on it, and a whole update gets no
/// more than Budget, so the game never waits long for its pictures.
/// </remarks>
public sealed class CardPictures(HttpClient http, Uri api, TimeProvider? clock = null)
{
    /// <summary>How long an update may take before the game starts without the files still to come.</summary>
    public static readonly TimeSpan Budget = TimeSpan.FromSeconds(90);

    const int AtOnce = 4;

    // How many times more a file is asked for when the server fails to give it, after a pause of
    // a second, and then of two.
    const int Retries = 2;

    // A manifest of 6000 files comes to under 1 MB.
    const int LargestManifest = 2 * 1024 * 1024;

    // HttpClient's time-out covers a reply's headers only; this covers its body as well.
    static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

    readonly TimeProvider clock = clock ?? TimeProvider.System;

    /// <summary>Brings <paramref name="folder"/>, bc, up to date with the server, within Budget.</summary>
    public async Task<PicturesOutcome> UpdateAsync(string folder, IProgress<PicturesProgress>? progress = null,
                                                   CancellationToken cancel = default)
    {
        using var budget = new CancellationTokenSource(Budget, clock);
        using var time = CancellationTokenSource.CreateLinkedTokenSource(cancel, budget.Token);
        HttpStatusCode status;
        byte[]? body;
        try
        {
            (status, body) = await ManifestAsync(time.Token);
        }
        catch (Exception e) when (Unanswered(e, cancel))
        {
            return PicturesOutcome.Unreachable;
        }
        try
        {
            // Not found: the server has no pictures. Any other refusal is the server in trouble,
            // or Cloudflare in front of it; and an answer that is no manifest, a page from
            // something in between or a list cut short, is no reason to throw pictures away.
            if (status == HttpStatusCode.NotFound)
                return await Task.Run(() => Clear(folder));
            if (status != HttpStatusCode.OK)
                return PicturesOutcome.Unreachable;
            if (body is null || ArtManifest.Read(body).Manifest is not { } manifest)
                return PicturesOutcome.Refused;

            progress?.Report(new PicturesProgress(0, 0));
            string staging = folder + ".partial";
            List<ArtFile> missing;
            try
            {
                missing = await Task.Run(() => Missing(folder, staging, manifest), time.Token);
                var fetched = await FetchAllAsync([.. missing.DistinctBy(file => file.StoredName)], staging, progress, time.Token);
                if (fetched != PicturesOutcome.UpToDate)
                    return fetched;
            }
            catch (Exception) when (budget.IsCancellationRequested && !cancel.IsCancellationRequested)
            {
                return PicturesOutcome.Unfinished;
            }
            // Never called off once it has begun.
            await Task.Run(() => Place(folder, staging, manifest, missing));
            return PicturesOutcome.UpToDate;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return PicturesOutcome.Unwritable;
        }
    }

    async Task<(HttpStatusCode Status, byte[]? Body)> ManifestAsync(CancellationToken cancel)
    {
        using var patience = new CancellationTokenSource(Patience, clock);
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancel, patience.Token);
        using var response = await http.GetAsync(new Uri(api, "art/manifest.json"), HttpCompletionOption.ResponseHeadersRead, limit.Token);
        if (response.StatusCode != HttpStatusCode.OK)
            return (response.StatusCode, null);
        // As bytes, whatever charset the reply claims (SignUpClient.Outcome says why).
        await using var stream = await response.Content.ReadAsStreamAsync(limit.Token);
        using var body = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(buffer, limit.Token)) > 0)
        {
            if (body.Length + read > LargestManifest)
                return (HttpStatusCode.OK, null);
            body.Write(buffer, 0, read);
        }
        return (HttpStatusCode.OK, body.ToArray());
    }

    // What bc lacks of the manifest's files, once every link in bc and beside it is gone.
    static List<ArtFile> Missing(string folder, string staging, ArtManifest manifest)
    {
        RemoveLinks(folder);
        RemoveLinks(staging);
        return [.. manifest.Files.Where(file => !Holds(Local(folder, file), file))];
    }

    // Each file into the staging folder, unless it is there already from an update that did not
    // finish. Stops at the first that fails for good.
    async Task<PicturesOutcome> FetchAllAsync(List<ArtFile> files, string staging, IProgress<PicturesProgress>? progress,
                                              CancellationToken cancel)
    {
        if (files.Count == 0)
            return PicturesOutcome.UpToDate;
        Directory.CreateDirectory(staging);
        progress?.Report(new PicturesProgress(0, files.Count));
        int done = 0;
        var outcome = PicturesOutcome.UpToDate;
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        try
        {
            await Parallel.ForEachAsync(files, new ParallelOptions { MaxDegreeOfParallelism = AtOnce, CancellationToken = stop.Token },
                async (file, token) =>
                {
                    string staged = Path.Combine(staging, file.StoredName);
                    var fetched = Holds(staged, file) ? PicturesOutcome.UpToDate : await FetchAsync(file, staged, token);
                    if (fetched != PicturesOutcome.UpToDate)
                    {
                        outcome = fetched;
                        await stop.CancelAsync();
                        return;
                    }
                    progress?.Report(new PicturesProgress(Interlocked.Increment(ref done), files.Count));
                });
        }
        catch (Exception) when (outcome != PicturesOutcome.UpToDate)
        {
            // Called off for the file that failed, with others still on their way.
        }
        return outcome;
    }

    // The file, asked for again after a pause when the server fails to give it, but not when it
    // says it has no such file, or gives another.
    async Task<PicturesOutcome> FetchAsync(ArtFile file, string staged, CancellationToken cancel)
    {
        for (int retry = 0; ; retry++)
        {
            PicturesOutcome fetched;
            try
            {
                fetched = await TryFetchAsync(file, staged, cancel);
            }
            catch (Exception e) when (Unanswered(e, cancel))
            {
                fetched = PicturesOutcome.Unreachable;
            }
            if (fetched != PicturesOutcome.Unreachable || retry == Retries)
                return fetched;
            await Task.Delay(TimeSpan.FromSeconds(retry + 1), clock, cancel);
        }
    }

    // The file from art/files/, its size and hash checked as it comes.
    async Task<PicturesOutcome> TryFetchAsync(ArtFile file, string staged, CancellationToken cancel)
    {
        using var patience = new CancellationTokenSource(Patience, clock);
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancel, patience.Token);
        using var response = await http.GetAsync(new Uri(api, "art/files/" + file.StoredName), HttpCompletionOption.ResponseHeadersRead, limit.Token);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return PicturesOutcome.Refused;
        if (response.StatusCode != HttpStatusCode.OK)
            return PicturesOutcome.Unreachable;
        string partial = staged + ".part";
        bool whole;
        using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
        await using (var input = await response.Content.ReadAsStreamAsync(limit.Token))
        await using (var output = File.Create(partial))
        {
            var buffer = new byte[81920];
            long size = 0;
            int read;
            while ((read = await input.ReadAsync(buffer, limit.Token)) > 0 && size + read <= file.Size)
            {
                size += read;
                hash.AppendData(buffer, 0, read);
                await output.WriteAsync(buffer.AsMemory(0, read), limit.Token);
            }
            whole = read == 0 && size == file.Size && Convert.ToHexStringLower(hash.GetHashAndReset()) == file.Sha256;
        }
        if (!whole)
        {
            File.Delete(partial);
            return PicturesOutcome.Refused;
        }
        File.Move(partial, staged, overwrite: true);
        return PicturesOutcome.UpToDate;
    }

    // Everything fetched goes into place, and only then does whatever the manifest does not list
    // go, so a copy that fails takes nothing away. A file in the way is deleted rather than
    // written over, which through a hard link would write outside bc.
    static void Place(string folder, string staging, ArtManifest manifest, List<ArtFile> missing)
    {
        Directory.CreateDirectory(folder);
        foreach (var file in missing)
        {
            string target = Local(folder, file);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Delete(target);
            File.Copy(Path.Combine(staging, file.StoredName), target);
        }
        var listed = manifest.Files.Select(file => Path.GetFullPath(Local(folder, file))).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Prune(new DirectoryInfo(folder), listed);
        if (Directory.Exists(staging))
        {
            Prune(new DirectoryInfo(staging), []);
            Directory.Delete(staging);
        }
    }

    static PicturesOutcome Clear(string folder)
    {
        string staging = folder + ".partial";
        RemoveLinks(folder);
        RemoveLinks(staging);
        if (Directory.Exists(folder))
            Prune(new DirectoryInfo(folder), []);
        if (Directory.Exists(staging))
        {
            Prune(new DirectoryInfo(staging), []);
            Directory.Delete(staging);
        }
        return PicturesOutcome.None;
    }

    // Takes away whatever under folder is not listed, and every link without following it: one
    // made after the update looked for them is still never gone into.
    internal static void Prune(DirectoryInfo folder, HashSet<string> listed)
    {
        foreach (var entry in folder.EnumerateFileSystemInfos())
        {
            bool link = entry.Attributes.HasFlag(FileAttributes.ReparsePoint);
            if (entry is DirectoryInfo sub && !link)
            {
                Prune(sub, listed);
                if (!sub.EnumerateFileSystemInfos().Any())
                    sub.Delete();
            }
            else if (link || !listed.Contains(entry.FullName))
            {
                entry.Delete();
            }
        }
    }

    // Takes away the link at path, or every link under the folder there, the link only and never
    // what it leads to, before anything is read or written through one: the launcher makes none,
    // and one could lead a check, a copy or a deletion out of bc.
    static void RemoveLinks(string path)
    {
        var folder = new DirectoryInfo(path);
        if (!folder.Exists)
            return;
        if (folder.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            folder.Delete();
            return;
        }
        foreach (var entry in folder.EnumerateFileSystemInfos())
        {
            if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
                entry.Delete();
            else if (entry is DirectoryInfo)
                RemoveLinks(entry.FullName);
        }
    }

    static string Local(string folder, ArtFile file) => Path.Combine(folder, file.Path.Replace('/', Path.DirectorySeparatorChar));

    // Whether the file at path is the one listed, by its size and hash. A link never is.
    static bool Holds(string path, ArtFile file)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length != file.Size || info.Attributes.HasFlag(FileAttributes.ReparsePoint))
                return false;
            using var stream = info.OpenRead();
            return Convert.ToHexStringLower(SHA256.HashData(stream)) == file.Sha256;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    // A request that got no answer, or none in time, rather than one the caller called off.
    static bool Unanswered(Exception e, CancellationToken cancel) =>
        (e is HttpRequestException or IOException or OperationCanceledException) && !cancel.IsCancellationRequested;
}
