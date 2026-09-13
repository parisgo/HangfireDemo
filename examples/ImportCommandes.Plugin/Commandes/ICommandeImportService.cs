namespace ImportCommandes.Plugin.Commandes;

public interface ICommandeImportService
{
    Task<CommandeImportResult> ImportAsync(
        CommandeImportRequest request,
        CancellationToken cancellationToken);
}
