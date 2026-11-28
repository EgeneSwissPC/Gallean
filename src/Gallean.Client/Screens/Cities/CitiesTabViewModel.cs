using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Gallean.Engine;

namespace Gallean.Client;

internal enum CitySortOrder
{
    Name,
    Level,
    Gold,
    Danger
}

// Gallean Upgrade (docs/cities-overview.md): every city the player owns on one screen, with its status; a row opens
// the city, upgrades it, or shows it on the map.
internal sealed partial class CitiesTabViewModel : ManagementTabViewModel
{
    // An enemy army this close (tiles) counts as a danger: about one turn's walk on open ground for the usual leader
    // (leaders have 15-35 movement points, the median 25, and an open tile costs 2).
    public const int DangerDistance = 12;

    // Enemy armies farther than this (two such turns) aren't shown at all.
    public const int ShownEnemyDistance = 24;

    public CitiesTabViewModel(GameModel gameModel, GameViewModel gameViewModel, ManagementScreenViewModel screen)
        : base(gameModel, gameViewModel)
    {
        Screen = screen;
        SortOrder = LastSortOrder;

        RefreshView();

        RegisterSnapshotGatedCommands(UpgradeCityCommand, EnterCityCommand, ShowOnMapCommand);
    }

    // The order picked last (kept for the session); the city screen's previous / next follows it too.
    public static CitySortOrder LastSortOrder { get; set; } = CitySortOrder.Name;

    public override string TabHeader => Tr("Cities");

    public ManagementScreenViewModel Screen { get; }

    [ObservableProperty]
    public partial IReadOnlyList<CityRowViewModel> Rows { get; private set; } = [];

    [ObservableProperty]
    public partial bool HasTownIncome { get; private set; }

    [ObservableProperty]
    public partial string CountText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string LevelsText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string GoldText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string LandText { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNameOrder), nameof(IsLevelOrder), nameof(IsGoldOrder), nameof(IsDangerOrder))]
    public partial CitySortOrder SortOrder { get; private set; }

    public bool IsNameOrder => SortOrder == CitySortOrder.Name;
    public bool IsLevelOrder => SortOrder == CitySortOrder.Level;
    public bool IsGoldOrder => SortOrder == CitySortOrder.Gold;
    public bool IsDangerOrder => SortOrder == CitySortOrder.Danger;

    // Capital first, then by the order; ties by name.
    public static IReadOnlyList<CityRowViewModel> Sort(IEnumerable<CityRowViewModel> rows, CitySortOrder order)
    {
        IOrderedEnumerable<CityRowViewModel> sorted = rows.OrderByDescending(row => row.IsCapital);

        sorted = order switch
        {
            CitySortOrder.Name => sorted,
            CitySortOrder.Level => sorted.ThenByDescending(row => row.Level),
            CitySortOrder.Gold => sorted.ThenByDescending(row => row.Gold ?? 0).ThenByDescending(row => row.Land),
            CitySortOrder.Danger => sorted.ThenBy(row => row.EnemyDistance ?? int.MaxValue)
        };

        return sorted
            .ThenBy(row => row.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(row => row.Id.Value, StringComparer.Ordinal)
            .ToList();
    }

    protected override void OnSnapshotChanged(GameSnapshot current)
    {
        RefreshView();
    }

    [RelayCommand]
    private void SortBy(CitySortOrder order)
    {
        SortOrder = order;
        LastSortOrder = order;
        Rows = Sort(Rows, order);
    }

    [RelayCommand(CanExecute = nameof(CanUseCities))]
    private void EnterCity(CityRowViewModel row)
    {
        if (WorldState.Fortifications.ContainsKey(row.Id))
        {
            Game.OpenScreen(ManagementScreenViewModel.CreateForFortification(GameModel, Game, row.Id));
        }
    }

    [RelayCommand(CanExecute = nameof(CanUseCities))]
    private void ShowOnMap(CityRowViewModel row)
    {
        if (Game.RootScreen is WorldScreenViewModel world)
        {
            Screen.Close();
            world.ShowPosition(row.Center);
        }
    }

    [RelayCommand(CanExecute = nameof(CanUseCities))]
    private void UpgradeCity(CityRowViewModel row)
    {
        if (!row.CanUpgrade || WorldState.Fortifications.GetValueOrDefault(row.Id) is not WorldCity city)
        {
            return;
        }

        WorldCityDefinition definition = GameModel.Definitions.GetCity(city.DefinitionId);

        Game.OpenScreen(new ConfirmPromptViewModel(
            TrSpans("Upgrade {0} to {1:*}", row.Name, Tr("level {0}", definition.Level + 1)),
            TrSpans("This will increase garrison capacity, regeneration and armor bonus."),
            Tr("Upgrade"),
            row.UpgradeCost,
            ConfirmPromptType.Primary,
            async () =>
            {
                if (await TryDispatchWorldActionAsync(new WorldUpgradeCityCommand(city.Id)))
                {
                    Game.PlaySound(new AssetSoundKey.CityGrow());
                }

                RefreshView();
            }));
    }

    private bool CanUseCities()
    {
        return !IsBusy;
    }

    private void RefreshView()
    {
        ICommandResult<WorldCommandError, WorldEvent, WorldGetCitiesOverviewCommand.Result> result =
            GameModel.World.DryRunCommand(new WorldGetCitiesOverviewCommand());
        List<CityRowViewModel> rows = [];
        bool hasTownIncome = false;

        if (!result.Failed && WorldState.TurnInfo.CurrentPlayerId == ViewPlayerId)
        {
            WorldPlan plan = WorldPlan.For(WorldState);

            foreach (WorldGetCitiesOverviewCommand.City info in result.Data.Cities)
            {
                if (WorldState.Fortifications.GetValueOrDefault(info.FortificationId) is { } fortification)
                {
                    rows.Add(BuildRow(fortification, info, plan));
                    hasTownIncome |= info.TownIncome is not null;
                }
            }
        }

        HasTownIncome = hasTownIncome;
        Rows = Sort(rows, SortOrder);

        int levels = rows.Where(row => !row.IsCapital).Sum(row => row.Level);
        int cities = rows.Count(row => !row.IsCapital);

        CountText = cities == 1 ? Tr("1 city") : Tr("{0} cities", cities);
        LevelsText = Tr("Levels: {0}", levels);
        GoldText = Tr("Gold: +{0} per turn", rows.Sum(row => row.Gold ?? 0));
        LandText = Tr("Land: +{0} per turn", rows.Sum(row => row.Land));
    }

    private CityRowViewModel BuildRow(WorldFortification fortification, WorldGetCitiesOverviewCommand.City info, WorldPlan plan)
    {
        string name = GameModel.Texts.GetScenarioText(fortification.NameTextId);
        bool isCapital = fortification is WorldCapital;
        int level = fortification is WorldCity city ? city.DefinitionId.Level : 0;

        AssetSpriteKey? imageKey = fortification switch
        {
            WorldCapital capital => new AssetSpriteKey.CapitalImage(WorldState.Players[capital.OwnerId].Race),
            WorldCity town => new AssetSpriteKey.CityImage(town.DefinitionId.Level),
            _ => null
        };

        List<CityGarrisonFaceViewModel> faces = [];

        foreach ((WorldUnit unit, _) in fortification.Garrison.GetUnits())
        {
            bool isWounded = unit.IsAlive && unit.Hitpoints < GameModel.Stats.GetWorldUnitStats(unit).MaxHitpoints;

            faces.Add(new CityGarrisonFaceViewModel(new AssetSpriteKey.UnitFace(unit.DefinitionId), !unit.IsAlive, isWounded));
        }

        string? visitorName = null;
        AssetSpriteKey.UnitFace? visitorFace = null;

        if (plan.GetStackAt(fortification.Entrance) is { } stack && stack.OwnerId == ViewPlayerId)
        {
            (WorldUnit leader, _, _) = stack.GetLeader(GameModel.Definitions);

            visitorName = UnitNameResolver.Resolve(leader, GameModel.Definitions, GameModel.Texts);
            visitorFace = new AssetSpriteKey.UnitFace(leader.DefinitionId);
        }

        (string? upgradeText, ResourceSetViewModel? upgradeCost, bool canUpgrade) = FormatUpgrade(info.Growth, level);

        WorldPosition center = new(
            fortification.Footprint.TopLeft.X + fortification.Footprint.Width / 2,
            fortification.Footprint.TopLeft.Y + fortification.Footprint.Height / 2);

        return new CityRowViewModel(
            fortification.Id,
            name,
            isCapital,
            level,
            imageKey,
            isCapital ? Tr("Capital") : Tr("Level {0}", level),
            info.TownIncome,
            info.TerrainGrowth,
            $"{info.OccupiedCapacity} / {info.TotalCapacity}",
            faces,
            visitorName,
            visitorFace,
            upgradeText,
            upgradeCost,
            canUpgrade,
            BuildStatuses(fortification, info),
            info.NearestEnemyDistance,
            center);
    }

    private (string? Text, ResourceSetViewModel? Cost, bool CanUpgrade) FormatUpgrade(WorldGetCityInfoCommand.Growth? growth, int level)
    {
        if (growth is null)
        {
            return (null, null, false);
        }
        else if (growth.Error == WorldCommandError.CityFullyUpgraded)
        {
            return (Tr("Max level"), null, false);
        }
        else if (growth.Error == WorldCommandError.CityAlreadyGrownThisTurn)
        {
            return (Tr("Upgraded this turn"), null, false);
        }
        else
        {
            return (Tr("To level {0}", level + 1), new ResourceSetViewModel(growth.Cost, ResourceSetProjection.All), growth.Error is null);
        }
    }

    private static IReadOnlyList<CityStatusViewModel> BuildStatuses(WorldFortification fortification, WorldGetCitiesOverviewCommand.City info)
    {
        List<CityStatusViewModel> statuses = [];

        if (fortification is WorldCity { RiotTurns: > 0 } city)
        {
            statuses.Add(new CityStatusViewModel(
                city.RiotTurns == 1 ? Tr("Riot: 1 turn left") : Tr("Riot: {0} turns left", city.RiotTurns),
                IsWarning: true));
        }

        if (info.NearestEnemyDistance is { } distance && distance <= ShownEnemyDistance)
        {
            statuses.Add(new CityStatusViewModel(
                distance == 1 ? Tr("Enemy 1 tile away") : Tr("Enemy {0} tiles away", distance),
                IsWarning: distance <= DangerDistance));
        }

        if (info.DeadUnits > 0)
        {
            statuses.Add(new CityStatusViewModel(info.DeadUnits == 1 ? Tr("1 dead unit") : Tr("{0} dead units", info.DeadUnits), IsWarning: true));
        }

        if (info.WoundedUnits > 0)
        {
            statuses.Add(new CityStatusViewModel(info.WoundedUnits == 1 ? Tr("1 wounded unit") : Tr("{0} wounded units", info.WoundedUnits), IsWarning: false));
        }

        if (info.OccupiedCapacity == 0)
        {
            statuses.Add(new CityStatusViewModel(Tr("No garrison"), IsWarning: true));
        }

        int items = fortification.Inventory.Items.Length;

        if (items > 0)
        {
            statuses.Add(new CityStatusViewModel(items == 1 ? Tr("1 item stored") : Tr("{0} items stored", items), IsWarning: false));
        }

        return statuses;
    }
}
