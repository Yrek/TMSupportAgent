using ThreatModelingAgent.Domain.Entities;
using ThreatModelingAgent.Domain.ValueObjects;

namespace ThreatModelingAgent.Domain.Interfaces;

public interface IUserRepository
{
    Task<User?> GetByIdAsync(UserId id, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
