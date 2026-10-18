namespace BattleCity;

/// <summary>Writing the files the launcher keeps in its own folder.</summary>
static class LauncherFiles
{
    /// <summary>
    /// Writes <paramref name="bytes"/> to <paramref name="path"/> unless the file holds them
    /// already. The bytes go to a file beside it first, so a failed write leaves the old file
    /// whole. A file in use cannot be replaced, as the client's copy and its DLL cannot while the
    /// game runs: that fails with an IOException or UnauthorizedAccessException.
    /// </summary>
    public static void Replace(string path, ReadOnlySpan<byte> bytes)
    {
        if (File.Exists(path) && File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes))
            return;
        string partial = path + ".new";
        try
        {
            File.WriteAllBytes(partial, bytes);
            File.Move(partial, path, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            TryDelete(partial);
            throw;
        }
    }

    public static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Overwritten by the next attempt.
        }
    }
}
