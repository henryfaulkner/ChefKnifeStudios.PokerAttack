using ChefKnifeStudios.PokerAttack.Shared.Enums;

namespace ChefKnifeStudios.PokerAttack.Shared;

public interface IFeatureFlagService
{
    bool IsEnabled(FeatureFlags flag);
}

public class FeatureFlagService : IFeatureFlagService
{
    private readonly Dictionary<FeatureFlags, bool> _featureFlags;

    public FeatureFlagService(Dictionary<FeatureFlags, bool> featureFlags)
    {
        _featureFlags = featureFlags;
    }

    public bool IsEnabled(FeatureFlags flag)
    {
        return _featureFlags.ContainsKey(flag) && _featureFlags[flag];
    }
}
