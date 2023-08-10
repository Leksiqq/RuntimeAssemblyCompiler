namespace Net.Leksi.RACWebApp.Common;

public interface IFactory
{
    object? GetValue(Type type);
}
