namespace ImportCommandes.Plugin.Commandes;

public sealed record CommandeImportResult(
    string BatchId,
    bool Imported,
    string Status);
