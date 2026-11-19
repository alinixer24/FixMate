namespace FixMate.Core.Interfaces;

public interface IApplicationDataService
{
    Task ClearUserDataAsync(CancellationToken cancellationToken = default);
}
