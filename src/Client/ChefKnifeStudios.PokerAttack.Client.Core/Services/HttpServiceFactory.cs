namespace ChefKnifeStudios.PokerAttack.Client.Core.Services;

public interface IHttpServiceFactory
{
    IHttpService Create(string name);
}

public class HttpServiceFactory : IHttpServiceFactory
{
    private readonly Func<string, HttpClient> _httpClientResolver;

    // Pass in a delegate that returns HttpClient by name
    public HttpServiceFactory(Func<string, HttpClient> httpClientResolver)
    {
        _httpClientResolver = httpClientResolver;
    }

    public IHttpService Create(string name)
    {
        return new HttpService(() => _httpClientResolver(name));
    }
}
