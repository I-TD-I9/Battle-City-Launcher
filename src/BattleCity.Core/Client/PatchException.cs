namespace BattleCity.Client;

/// <summary>A client the launcher will not patch, and why.</summary>
public sealed class PatchException(string message) : Exception(message);
