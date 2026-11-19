using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Gallean.Engine;

namespace Gallean.Client;

// The map's variables (docs/map-editor-events.md, phase 6), opened from the events list: a row per variable with its
// name and starting value (both edited in the row), how many events use it, and delete (asks first when used).
internal sealed partial class MapEditorVariablesScreenViewModel : AbstractScreenViewModel
{
    private readonly MapEditorScreenViewModel editor;

    public MapEditorVariablesScreenViewModel(GameViewModel game, MapEditorScreenViewModel editor)
    {
        Game = game;
        this.editor = editor;
        Refresh();
    }

    public GameViewModel Game { get; }

    public ObservableCollection<MapEditorVariableRowViewModel> Rows { get; } = [];

    public bool HasNoRows => Rows.Count == 0;

    public override bool BlurBackdrop => true;

    protected override bool TryClose()
    {
        CloseSelf();

        return true;
    }

    public void Refresh()
    {
        Rows.Clear();

        foreach (CustomMapVariable variable in editor.Map.Variables.OrderBy(variable => variable.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(variable => variable.Id))
        {
            int users = editor.Map.GetEventsUsing(new CustomMapEventTarget.Variable(variable.Id)).Count;
            Rows.Add(new MapEditorVariableRowViewModel(variable, users, editor));
        }

        OnPropertyChanged(nameof(HasNoRows));
    }

    [RelayCommand]
    private void NewVariable()
    {
        editor.NewVariable();
    }
}

internal sealed partial class MapEditorVariableRowViewModel : ObservableObject
{
    private readonly MapEditorScreenViewModel editor;
    private readonly bool loading;

    public MapEditorVariableRowViewModel(CustomMapVariable variable, int users, MapEditorScreenViewModel editor)
    {
        loading = true;
        Variable = variable;
        this.editor = editor;
        Name = variable.Name;
        InitialText = variable.Initial.ToString(CultureInfo.InvariantCulture);
        UsedBy = users == 0 ? Tr("not used yet") : users == 1 ? Tr("used by 1 event") : Tr("used by {0} events", users);
        loading = false;
    }

    public CustomMapVariable Variable { get; }

    public int Id => Variable.Id;

    public string UsedBy { get; }

    [ObservableProperty]
    public partial string Name { get; set; }

    // A whole number from -9999 to 9999; anything else puts the old value back.
    [ObservableProperty]
    public partial string InitialText { get; set; }

    partial void OnNameChanged(string value)
    {
        if (!loading && !string.IsNullOrWhiteSpace(value) && value.Trim() != Variable.Name)
        {
            editor.UpdateVariable(Variable with { Name = value.Trim() });
        }
    }

    partial void OnInitialTextChanged(string value)
    {
        if (loading)
        {
            return;
        }

        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number))
        {
            editor.UpdateVariable(Variable with { Initial = Math.Clamp(number, -9999, 9999) });
        }
        else
        {
            editor.UpdateVariable(Variable);
        }
    }

    [RelayCommand]
    private void Delete() => editor.DeleteVariable(Id);
}
