using Gallean.Engine;

namespace Gallean.Client;

// Gallean Upgrade (docs/cities-overview.md): one row of the cities list.
internal sealed record CityRowViewModel(
    WorldFortificationId Id,
    string Name,
    bool IsCapital,
    int Level,
    AssetSpriteKey? ImageKey,
    string LevelText,
    int? Gold,
    int Land,
    string GarrisonText,
    IReadOnlyList<CityGarrisonFaceViewModel> GarrisonFaces,
    string? VisitorName,
    AssetSpriteKey.UnitFace? VisitorFaceKey,
    string? UpgradeText,
    ResourceSetViewModel? UpgradeCost,
    bool CanUpgrade,
    IReadOnlyList<CityStatusViewModel> Statuses,
    int? EnemyDistance,
    WorldPosition Center)
{
    public string? GoldText => Gold is { } gold ? $"+{gold}" : null;
    public string LandText => $"+{Land}";
    public bool HasVisitor => VisitorName is not null;
    public bool HasUpgradeCost => UpgradeCost is not null;

    public bool Equals(CityRowViewModel? other)
    {
        return other is not null &&
               Id == other.Id &&
               Name == other.Name &&
               IsCapital == other.IsCapital &&
               Level == other.Level &&
               Equals(ImageKey, other.ImageKey) &&
               LevelText == other.LevelText &&
               Gold == other.Gold &&
               Land == other.Land &&
               GarrisonText == other.GarrisonText &&
               GarrisonFaces.SequenceEqual(other.GarrisonFaces) &&
               VisitorName == other.VisitorName &&
               Equals(VisitorFaceKey, other.VisitorFaceKey) &&
               UpgradeText == other.UpgradeText &&
               Equals(UpgradeCost, other.UpgradeCost) &&
               CanUpgrade == other.CanUpgrade &&
               Statuses.SequenceEqual(other.Statuses) &&
               EnemyDistance == other.EnemyDistance &&
               Center == other.Center;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Id, Name, Level, Gold, Land, GarrisonText, UpgradeText, EnemyDistance);
    }
}

internal sealed record CityGarrisonFaceViewModel(AssetSpriteKey.UnitFace FaceKey, bool IsDead, bool IsWounded);

// A short line in the status column; IsWarning shows it in the warning colour.
internal sealed record CityStatusViewModel(string Text, bool IsWarning);
