using AgriSage.Application.Common.Interfaces;
using AgriSage.Domain.Features.Debt.Entities;
using AgriSage.Infrastructure.Persistence;
using AgriSage.IntegrationTests.Infrastructure.Debt;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AgriSage.IntegrationTests.Infrastructure.Persistence;

// F3.20: the store lock is "SELECT ... FOR NO KEY UPDATE" on the store row. With FOR UPDATE it also blocked every transaction that
// inserted a row referencing the store (the foreign key takes FOR KEY SHARE on it, such as an audit entry), and a transaction
// holding the Farmer lock that wrote an audit entry then deadlocked with one that held the store lock and waited for the same
// Farmer lock (rollover vs adjust, cancel or dispute of the same customer). The lock itself must keep excluding others.
public sealed class StoreLockDatabaseTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private sealed class Tx(CreditTestDatabase e) : IAsyncDisposable
    {
        private readonly IServiceScope scope = e.Scope();
        private Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? tx;
        public AgriSageDbContext Db => scope.ServiceProvider.GetRequiredService<AgriSageDbContext>();
        public IRowLockService Locks => scope.ServiceProvider.GetRequiredService<IRowLockService>();

        public async Task<Tx> BeginAsync() { tx = await Db.Database.BeginTransactionAsync(Token); return this; }
        public Task CommitAsync() => tx!.CommitAsync(Token);

        public async ValueTask DisposeAsync()
        {
            if (tx is not null) await tx.DisposeAsync();
            scope.Dispose();
        }
    }

    // The exact cycle of the V2 failure, ordered by hand. A holds the store lock and then asks for the Farmer lock (a rollover).
    // B holds the Farmer lock and writes a row that references the store (the audit entry of an adjust). B must not wait for A.
    [CreditDbFact]
    public async Task A_writer_holding_the_farmer_lock_does_not_deadlock_with_a_store_lock_holder_asking_for_the_farmer()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);
        await using var a = await new Tx(e).BeginAsync();
        await using var b = await new Tx(e).BeginAsync();

        await a.Locks.LockStoreAsync(e.StoreId, Token);                                   // A: store lock
        await b.Locks.LockFarmerProfileAsync(e.FarmerId, Token);                          // B: Farmer lock

        var bWrites = Task.Run(async () =>
        {
            b.Db.StoreDebtPolicies.Add(new StoreDebtPolicy(e.StoreId));                   // a row with a foreign key to the store
            await b.Db.SaveChangesAsync(Token);
            await b.CommitAsync();
        }, Token);
        await Task.Delay(TimeSpan.FromSeconds(1.5), Token);
        var writeWaited = !bWrites.IsCompleted;                                           // under FOR UPDATE it waits here
        if (writeWaited)
        {
            await a.CommitAsync();                                                        // release A so the failure leaves nothing hanging
            await bWrites.WaitAsync(TimeSpan.FromSeconds(30), Token);
        }

        Assert.False(writeWaited, "a row that references the store waited for the store lock");

        var aFarmer = Task.Run(async () =>
        {
            await a.Locks.LockFarmerProfileAsync(e.FarmerId, Token);                      // A waits for B's Farmer lock until B commits
            await a.CommitAsync();
        }, Token);

        await Task.WhenAll(bWrites, aFarmer).WaitAsync(TimeSpan.FromSeconds(30), Token);  // a deadlock would fail one of them
        using var s = e.Scope();
        Assert.Equal(1, await s.ServiceProvider.GetRequiredService<AgriSageDbContext>().StoreDebtPolicies.CountAsync(Token));
    }

    // The business protection stays: the store lock still excludes other store lockers, and the Farmer lock other Farmer lockers.
    [CreditDbFact]
    public async Task The_store_lock_and_the_farmer_lock_still_exclude_other_lockers()
    {
        await using var e = await CreditTestDatabase.StartAsync(Token);
        await using var holder = await new Tx(e).BeginAsync();
        await holder.Locks.LockStoreAsync(e.StoreId, Token);
        await holder.Locks.LockFarmerProfileAsync(e.FarmerId, Token);

        await using var other = await new Tx(e).BeginAsync();
        var store = Task.Run(() => other.Locks.LockStoreAsync(e.StoreId, Token), Token);
        await using var third = await new Tx(e).BeginAsync();
        var farmer = Task.Run(() => third.Locks.LockFarmerProfileAsync(e.FarmerId, Token), Token);
        await Task.Delay(TimeSpan.FromSeconds(2), Token);
        Assert.False(store.IsCompleted, "a second store lock did not wait");
        Assert.False(farmer.IsCompleted, "a second Farmer lock did not wait");

        await holder.CommitAsync();
        await Task.WhenAll(store, farmer).WaitAsync(TimeSpan.FromSeconds(30), Token);
    }
}
