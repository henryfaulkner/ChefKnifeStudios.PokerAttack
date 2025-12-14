using ChefKnifeStudios.PokerAttack.Shared.Enums;

namespace ChefKnifeStudios.PokerAttack.Client.Core;

/// <summary>
/// Application Settings configured in appsettings.json
/// </summary>
public class AppSettings
{
    /// <summary>
    /// List of External Web APIs called by the client app
    /// </summary>
    public List<WebAPI> ExternalApis { get; set; } = [];

    /// <summary>
    /// Feature flags for toggling application features
    /// </summary>
    public Dictionary<FeatureFlags, bool> FeatureFlags { get; set; } = new();

    public string[] GetAllExternalUri(bool authenticatedOnly = false) => ExternalApis
        .Where(a => (!authenticatedOnly || a.AuthenticationRequired))
        .Select(x => x.BaseUri)
        .ToArray();

    public class WebAPI
    {
        /// <summary>
        /// Unique Name of the Web API - used to create HttpClient
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Base URI for the Web API
        /// </summary>
        public string BaseUri { get; set; } = string.Empty;

        /// <summary>
        /// Whether the Web API requires authentication
        /// </summary>
        public bool AuthenticationRequired { get; set; }

        /// <summary>
        /// Whether to add an HttpClient for this API
        /// </summary>
        public bool AddHttpClient { get; set; }
    }
}
