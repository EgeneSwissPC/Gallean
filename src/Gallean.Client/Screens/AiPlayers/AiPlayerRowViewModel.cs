using CommunityToolkit.Mvvm.ComponentModel;

namespace Gallean.Client;

// Gallean Upgrade: one computer player in the AI players panel. An empty string means
// "Settings choice" (no own AI); otherwise an AiBrainRegistry ID.
internal sealed partial class AiPlayerRowViewModel : ObservableObject
{
    public const string SettingsChoice = "";

    public AiPlayerRowViewModel(object key, string name, string? worldAi, string? battleAi)
    {
        Key = key;
        Name = name;
        WorldAi = worldAi ?? SettingsChoice;
        BattleAi = battleAi ?? SettingsChoice;
    }

    public object Key { get; }
    public string Name { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOwnAi))]
    public partial string WorldAi { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOwnAi))]
    public partial string BattleAi { get; set; }

    public bool HasOwnAi => WorldAi != SettingsChoice || BattleAi != SettingsChoice;

    public WorldAiOverride? ToOverride()
    {
        WorldAiOverride result = new(
            WorldAi == SettingsChoice ? null : WorldAi,
            BattleAi == SettingsChoice ? null : BattleAi);

        return result.IsEmpty ? null : result;
    }
}
