using AgriSage.Domain.Common;

namespace AgriSage.UnitTests.Domain.Common;

public class TimeOrderedIdTests
{
    [Fact]
    public void Ids_are_version_7_rfc_variant_and_unique()
    {
        var ids = Enumerable.Range(0, 20_000).Select(_ => TimeOrderedId.New()).ToList();

        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.All(ids, id =>
        {
            var text = id.ToString("D");
            Assert.Equal('7', text[14]);
            Assert.Contains(text[19], "89ab");
        });
    }

    [Fact]
    public void Ids_increase_in_creation_order_even_inside_one_millisecond()
    {
        var ids = Enumerable.Range(0, 20_000).Select(_ => TimeOrderedId.New()).ToList();

        // In memory (Guid comparison) and as text, which is PostgreSQL's byte order for uuid.
        Assert.Equal(ids, ids.Order().ToList());
        Assert.Equal(ids.Select(i => i.ToString("D")), ids.Select(i => i.ToString("D")).Order(StringComparer.Ordinal).ToList());
    }

    [Fact]
    public async Task Ids_created_by_several_threads_are_unique_and_each_thread_sees_them_increase()
    {
        var perThread = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(
            () => Enumerable.Range(0, 5_000).Select(_ => TimeOrderedId.New()).ToList(),
            TestContext.Current.CancellationToken)));

        Assert.Equal(40_000, perThread.SelectMany(x => x).Distinct().Count());
        Assert.All(perThread, ids => Assert.Equal(ids, ids.Order().ToList()));
    }

    [Fact]
    public void An_id_carries_the_current_time()
    {
        var before = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var text = TimeOrderedId.New().ToString("N");
        var after = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var milliseconds = Convert.ToInt64(text[..12], 16);

        Assert.InRange(milliseconds, before, after + 5_000);
    }
}
