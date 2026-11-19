using CommunityToolkit.Mvvm.Input;
using Gallean.Engine;

namespace Gallean.Client;

// Army templates in the editor: the templates screen (from the events list), the army screen for their units, and
// the template changes as undo steps (events using a template follow: they turn red when it can't be saved).
internal sealed partial class MapEditorScreenViewModel
{
    public MapEditorEventContext EventContext()
    {
        return new MapEditorEventContext(map, editorGame, PickEventItem, PickEventPortrait, PickEventTargetOnMap, PickEventSpell, allLandmarks);
    }

    [RelayCommand]
    private void OpenTemplates()
    {
        Game.OpenScreen(new MapEditorTemplatesScreenViewModel(Game, this));
    }

    // A new neutral template; its army screen opens at once (a template needs a leader).
    public void NewTemplate()
    {
        CustomMapArmyTemplate? added = null;
        CommitTemplates(current =>
        {
            added = current.AddTemplate(new CustomMapArmyTemplate(0, Tr("Army template {0}", NextTemplateNumber(current)), WorldRace.Neutral, [.. new CustomMapUnit?[GridPosition.Count]], CustomMapArmyOrder.Normal));
            return true;
        });

        if (added is not null)
        {
            EditTemplateUnits(added.Id);
        }
    }

    public void UpdateTemplate(CustomMapArmyTemplate template)
    {
        if (!template.Equals(map.GetTemplate(template.Id)))
        {
            CommitTemplates(current => current.UpdateTemplate(template));
        }
    }

    public void DeleteTemplate(int id)
    {
        ConfirmRemoval(new CustomMapEventTarget.Template(id), map.GetTemplate(id)?.Name ?? string.Empty, () => CommitTemplates(current => current.RemoveTemplate(id)));
    }

    public void EditTemplateUnits(int id)
    {
        if (map.GetTemplate(id) is not { } template)
        {
            return;
        }

        Game.OpenScreen(new MapEditorArmyScreenViewModel(
            Game,
            editorGame,
            units,
            template.Owner,
            default,
            template.Slots,
            (owner, slots) =>
            {
                if (CustomMapStack.ValidateSlots(slots) is { } error)
                {
                    return MapEditorLabels.StackError(error, CustomMapStack.GetLeadership(slots));
                }

                UpdateTemplate(template with { Owner = owner, Slots = [.. slots] });
                return null;
            },
            onDelete: null,
            title: Tr("Army template: {0}", template.Name),
            confirmLabel: Tr("Save template")));
    }

    // "Orc leader · 3 units · Neutral" or what keeps the template from being used.
    public string TemplateDetails(CustomMapArmyTemplate template)
    {
        if (map.GetTemplateError(template) is { } error)
        {
            return MapEditorLabels.StackError(error, CustomMapStack.GetLeadership(template.Slots));
        }

        if (!map.IsTemplateSaveable(template))
        {
            return Tr("Its order points at something that is gone.");
        }

        CustomMapUnit leader = template.Slots.OfType<CustomMapUnit>().First(unit => unit.Leadership > 0);
        int count = template.Slots.OfType<CustomMapUnit>().Count();

        return count == 1
            ? Tr("{0} · 1 unit", MapEditorLibrary.GetUnitName(editorGame, leader.TypeId))
            : Tr("{0} · {1} units", MapEditorLibrary.GetUnitName(editorGame, leader.TypeId), count);
    }

    private void CommitTemplates(Func<CustomMap, bool> change)
    {
        CommitEvents(change);
        RefreshTemplatesScreen();
        RefreshVariablesScreen();
    }

    private void RefreshTemplatesScreen()
    {
        foreach (MapEditorTemplatesScreenViewModel screen in Game.Screens.OfType<MapEditorTemplatesScreenViewModel>())
        {
            screen.Refresh();
        }
    }

    [RelayCommand]
    private void OpenVariables()
    {
        Game.OpenScreen(new MapEditorVariablesScreenViewModel(Game, this));
    }

    public void NewVariable()
    {
        CommitTemplates(current =>
        {
            current.AddVariable(Tr("Variable {0}", current.Variables.Count == 0 ? 1 : current.Variables.Max(variable => variable.Id) + 1));
            return true;
        });
    }

    public void UpdateVariable(CustomMapVariable variable)
    {
        if (variable != map.GetVariable(variable.Id))
        {
            CommitTemplates(current => current.UpdateVariable(variable));
        }
        else
        {
            RefreshVariablesScreen();
        }
    }

    public void DeleteVariable(int id)
    {
        ConfirmRemoval(new CustomMapEventTarget.Variable(id), map.GetVariable(id)?.Name ?? string.Empty, () => CommitTemplates(current => current.RemoveVariable(id)));
    }

    private void RefreshVariablesScreen()
    {
        foreach (MapEditorVariablesScreenViewModel screen in Game.Screens.OfType<MapEditorVariablesScreenViewModel>())
        {
            screen.Refresh();
        }
    }

    private static int NextTemplateNumber(CustomMap current)
    {
        return current.Templates.Count == 0 ? 1 : current.Templates.Max(template => template.Id) + 1;
    }
}
