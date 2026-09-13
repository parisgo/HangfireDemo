namespace HangfireDemo.Core.Commandes;

public sealed record CommandeImportResult(
    string BatchId,
    bool Imported,
    string Status);
