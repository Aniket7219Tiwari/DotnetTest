using Microsoft.Extensions.Caching.Memory;

namespace Store.Api.Infrastructure;

public sealed class ProductListCache(IMemoryCache cache)
{
    private long _generation;

    public long Generation => Interlocked.Read(ref _generation);

    public bool TryGet<T>(string key, long generation, out T? value)
    {
        if (generation != Generation)
        {
            value = default;
            return false;
        }
        var found = cache.TryGetValue(CacheKey(key, generation), out value);
        return found && generation == Generation;
    }

    public void Set<T>(string key, T value, long generation)
    {
        if (generation == Generation)
            cache.Set(CacheKey(key, generation), value, TimeSpan.FromMinutes(2));
    }

    public void Invalidate() => Interlocked.Increment(ref _generation);

    private static string CacheKey(string key, long generation) => $"products:{generation}:{key}";
}
