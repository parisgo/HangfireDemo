namespace HangfireDemo.Core.Commandes;

public sealed record CommandeImportRequest(
    string BatchId,
    string RequestedBy,
    string? JobId);
