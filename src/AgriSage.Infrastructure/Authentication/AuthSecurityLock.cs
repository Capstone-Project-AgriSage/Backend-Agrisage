using AgriSage.Application.Features.Auth.Interfaces;
using AgriSage.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AgriSage.Infrastructure.Authentication;

public sealed class AuthSecurityLock(AgriSageDbContext context) : IAuthSecurityLock
{
    public async Task LockUserAsync(Guid id, CancellationToken token) =>
        await context.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM users WHERE id = {id} FOR UPDATE", token);
    public async Task LockSessionAsync(Guid id, CancellationToken token) =>
        await context.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM auth_sessions WHERE id = {id} FOR UPDATE", token);
}
