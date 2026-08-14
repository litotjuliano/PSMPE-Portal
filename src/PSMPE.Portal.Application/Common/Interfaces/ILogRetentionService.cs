namespace PSMPE.Portal.Application.Common.Interfaces;

public interface ILogRetentionService
{
    Task PruneAsync(CancellationToken cancellationToken = default);
}
