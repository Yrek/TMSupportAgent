using Microsoft.EntityFrameworkCore;
using ThreatModelingAgent.Domain.Entities;
using ThreatModelingAgent.Domain.Interfaces;
using ThreatModelingAgent.Domain.ValueObjects;

namespace ThreatModelingAgent.Infrastructure.Persistence.Repositories;

/// <summary>
/// User repository.
///
/// Note: Users are not org-scoped — they exist at the platform level.
/// RLS does not apply to the users table; queries filter by UserId only.
/// </summary>
internal sealed class UserRepository(AppDbContext db) : IUserRepository
{
    public Task<User?> GetByIdAsync(UserId id, CancellationToken ct = default)
        => db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);

    public Task<User?> GetByWorkOsUserIdAsync(string workOsUserId, CancellationToken ct = default)
        => db.Users.FirstOrDefaultAsync(u => u.WorkOsUserId == workOsUserId, ct);

    public Task SaveChangesAsync(CancellationToken ct = default)
        => db.SaveChangesAsync(ct);
}
