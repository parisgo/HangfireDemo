namespace ImportCommandes.Plugin.Commandes;

public sealed record CommandeImportRequest(
    string BatchId,
    string RequestedBy,
    string? JobId);
