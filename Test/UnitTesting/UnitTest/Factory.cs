using Microsoft.Extensions.DependencyInjection;
using Net.Leksi.RACWebApp.Common;

namespace Net.Leksi.Rac.UnitTesting;

public class Factory : IFactory
{
    private IServiceProvider _services;
    private Dictionary<Type, List<object>> cache = new();

    internal int MaxObjectsOfType { get; set; } = 0;
    internal Random Random { get; set; } = null!;

    public Factory(IServiceProvider services)
    {
        _services = services;
    }

    public object? GetValue(Type type)
    {
        if(MaxObjectsOfType <= 0)
        {
            return null;
        }
        object result;
        if(!cache.ContainsKey(type))
        {
            cache.Add(type, new List<object>());
        }
        if (cache[type].Count < MaxObjectsOfType) 
        {
            if (typeof(IMagicable).IsAssignableFrom(type))
            {
                result = _services.GetRequiredService(type);
            }
            else
            {
                result = Tests.MakeMagicWord(Random);
            }
            cache[type].Add(result);
        }
        else
        {
            result = cache[type][Random.Next(cache[type].Count)];
        }
        return result;
    }

}
