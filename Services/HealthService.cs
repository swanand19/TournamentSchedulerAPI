using TournamentScheduler.Api.Data;

namespace TournamentScheduler.Api.Services;

public interface IHealthService
{
    /// <summary>True when the database answers. Never throws: an unreachable database is an answer too.</summary>
    Task<bool> DatabaseReachableAsync();
}

public class HealthService : IHealthService
{
    private readonly IUnitOfWork _unitOfWork;

    public HealthService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<bool> DatabaseReachableAsync()
    {
        try
        {
            return await _unitOfWork.CanConnectAsync();
        }
        catch
        {
            return false;
        }
    }
}
