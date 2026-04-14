using Microsoft.EntityFrameworkCore;
using PgTestify.EfCore;
using TUnit.Core;

namespace PgTestify.TUnit;

/// <summary>
/// Base class for TUnit database tests that use a <see cref="PgTestifyFixture{TContext}"/>.
///
/// <para>
/// Handles per-test database rental and disposal automatically.
/// Override <see cref="ArrangeAsync"/> to seed per-test data after the database is rented.
/// </para>
///
/// <para>Example:
/// <code>
/// public class ProductTests : PgTest&lt;MyFixture, AppDbContext&gt;
/// {
///     [Test]
///     public async Task Can_Insert_Product()
///     {
///         Context.Products.Add(new Product { Name = "Test" });
///         await Context.SaveChangesAsync();
///         await Assert.That(await NoTrackingContext.Products.CountAsync()).IsEqualTo(1);
///     }
/// }
/// </code>
/// </para>
/// </summary>
public abstract class PgTest<TFixture, TContext>
    where TFixture : PgTestifyFixture<TContext>
    where TContext : DbContext
{
    /// <summary>
    /// The shared fixture, injected by TUnit.
    /// </summary>
    public TFixture DbFixture { get; }

    protected PgTest(TFixture fixture)
    {
        DbFixture = fixture;
    }

    /// <summary>The per-test database handle. Available during and after <see cref="SetUpDatabase"/>.</summary>
    protected TestDatabase<TContext> Db { get; private set; } = null!;

    /// <summary>Shortcut to <see cref="TestDatabase{TContext}.Context"/>.</summary>
    protected TContext Context => Db.Context;

    /// <summary>Shortcut to <see cref="TestDatabase{TContext}.NoTrackingContext"/>.</summary>
    protected TContext NoTrackingContext => Db.NoTrackingContext;

    /// <summary>
    /// Override to seed per-test data after the database is rented from the pool.
    /// Runs before the test method body.
    /// </summary>
    protected virtual Task ArrangeAsync(TContext context, CancellationToken ct)
        => Task.CompletedTask;

    [Before(Test)]
    public async Task SetUpDatabase()
    {
        Db = await DbFixture.Fixture.RentAsync();
        await ArrangeAsync(Db.Context, default);
    }

    [After(Test)]
    public async Task TearDownDatabase()
    {
        if (Db is not null)
            await Db.DisposeAsync();
    }
}
