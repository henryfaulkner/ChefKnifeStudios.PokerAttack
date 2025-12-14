using ChefKnifeStudios.PokerAttack.Shared.Enums;
using Microsoft.Extensions.Configuration;

namespace ChefKnifeStudios.PokerAttack.Shared;

public interface IFeatureFlagService
{
    bool IsEnabled(FeatureFlags flag);
}

public class FeatureFlagService : IFeatureFlagService
{
    private readonly Dictionary<FeatureFlags, bool> _featureFlags;

    public FeatureFlagService(IConfiguration configuration)
    {
        var featureFlagOptions = new FeatureFlagOptions();
        configuration.Bind(featureFlagOptions);

        _featureFlags = featureFlagOptions.FeatureFlags ?? new Dictionary<FeatureFlags, bool>();
    }

    public bool IsEnabled(FeatureFlags flag)
    {
        return _featureFlags.ContainsKey(flag) && _featureFlags[flag];
    }

    public class FeatureFlagOptions
    {
        public Dictionary<FeatureFlags, bool>? FeatureFlags { get; set; }
    }
}
