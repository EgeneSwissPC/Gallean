using System.Collections.ObjectModel;
using Avalonia;
using CommunityToolkit.Mvvm.Input;
using Gallean.Engine;

namespace Gallean.Client;

// The chain screen (docs/map-editor-events.md, phase 7): every event as a box, columns left to right in the order
// events switch each other on, green arrows for "switches on", red for "switches off", boxes of events that can never
// fire in a warning colour. A click on a box opens the event's blocks; the screen follows every change.
internal sealed partial class MapEditorChainScreenViewModel : AbstractScreenViewModel
{
    public const double BoxWidth = 230;
    public const double BoxHeight = 50;
    private const double ColumnStep = 300;
    private const double RowStep = 74;
    private const double Margin = 16;

    private readonly MapEditorScreenViewModel editor;

    public MapEditorChainScreenViewModel(GameViewModel game, MapEditorScreenViewModel editor)
    {
        Game = game;
        this.editor = editor;
        Refresh();
    }

    public GameViewModel Game { get; }

    public ObservableCollection<MapEditorChainNodeViewModel> Nodes { get; } = [];

    public ObservableCollection<MapEditorChainLinkViewModel> Links { get; } = [];

    public double CanvasWidth { get; private set; }

    public double CanvasHeight { get; private set; }

    public string Summary { get; private set; } = string.Empty;

    public bool HasNoEvents => Nodes.Count == 0;

    public override bool BlurBackdrop => true;

    protected override bool TryClose()
    {
        CloseSelf();

        return true;
    }

    private static List<List<CustomMapEvent>> GetChains(CustomMap map, CustomMapEventChains chains)
    {
        Dictionary<int, List<int>> neighbours = map.Events.ToDictionary(ev => ev.Id, _ => new List<int>());

        foreach (CustomMapEventLink link in chains.Links.Where(link => link.From != link.To))
        {
            neighbours[link.From].Add(link.To);
            neighbours[link.To].Add(link.From);
        }

        HashSet<int> seen = [];
        List<List<CustomMapEvent>> result = [];

        foreach (CustomMapEvent start in map.Events.OrderBy(ev => ev.Id))
        {
            if (!seen.Add(start.Id))
            {
                continue;
            }

            List<CustomMapEvent> chain = [];
            Queue<int> queue = new([start.Id]);

            while (queue.TryDequeue(out int id))
            {
                chain.Add(map.GetEvent(id)!);

                foreach (int next in neighbours[id].Where(seen.Add))
                {
                    queue.Enqueue(next);
                }
            }

            result.Add(chain);
        }

        return [.. result.OrderBy(chain => chain.Count == 1).ThenBy(chain => chain.Count == 1 ? chain[0].Name : string.Empty, StringComparer.CurrentCultureIgnoreCase)];
    }

    public void Refresh()
    {
        CustomMap map = editor.Map;
        CustomMapEventChains chains = map.GetEventChains();
        Dictionary<int, Point> positions = [];
        Nodes.Clear();
        Links.Clear();

        // One band of rows per chain (events linked to each other), chains first, single events below; inside a band
        // the columns follow the order in which events switch each other on.
        int rowBase = 0;

        foreach (List<CustomMapEvent> chain in GetChains(map, chains))
        {
            int rows = 0;

            foreach (IGrouping<int, CustomMapEvent> column in chain.GroupBy(ev => chains.Layers[ev.Id]).OrderBy(group => group.Key))
            {
                int row = 0;

                foreach (CustomMapEvent ev in column.OrderBy(ev => ev.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(ev => ev.Id))
                {
                    Point at = new(Margin + (column.Key * ColumnStep), Margin + ((rowBase + row++) * RowStep));
                    positions[ev.Id] = at;
                    bool never = chains.NeverFires.Contains(ev.Id);
                    string state = never ? Tr("never fires") : ev.Enabled ? Tr("on at the start") : Tr("switched on later");

                    // A self link has no arrow: the box says it.
                    if (chains.Links.FirstOrDefault(link => link.From == ev.Id && link.To == ev.Id) is { } self)
                    {
                        state = self.On ? Tr("{0} · switches itself on", state) : Tr("{0} · switches itself off", state);
                    }

                    Nodes.Add(new MapEditorChainNodeViewModel(ev.Id, ev.Name, state, never, new Thickness(at.X, at.Y, 0, 0), editor));
                }

                rows = Math.Max(rows, row);
            }

            rowBase += rows;
        }

        foreach (CustomMapEventLink link in chains.Links.Where(link => link.From != link.To))
        {
            Point from = positions[link.From] + new Point(BoxWidth, BoxHeight / 2);
            Point to = positions[link.To] + new Point(0, BoxHeight / 2);

            if (to.X <= from.X)
            {
                // Back links (a later event switching an earlier one) run from the box's bottom to the target's bottom.
                from = positions[link.From] + new Point(BoxWidth / 2, BoxHeight);
                to = positions[link.To] + new Point(BoxWidth / 2 + 12, BoxHeight);
            }

            Links.Add(new MapEditorChainLinkViewModel(from, to, link.On));
        }

        CanvasWidth = positions.Count == 0 ? 0 : positions.Values.Max(point => point.X) + BoxWidth + Margin;
        CanvasHeight = positions.Count == 0 ? 0 : positions.Values.Max(point => point.Y) + BoxHeight + Margin + 20;
        string counts = chains.Links.Count == 1 ? Tr("{0} events, 1 link.", map.Events.Count) : Tr("{0} events, {1} links.", map.Events.Count, chains.Links.Count);
        Summary = chains.NeverFires.Count == 0
            ? Tr("{0} Every event can fire.", counts)
            : Tr("{0} {1} can never fire: they start switched off and nothing that can fire switches them on.", counts, chains.NeverFires.Count);
        OnPropertyChanged(nameof(CanvasWidth));
        OnPropertyChanged(nameof(CanvasHeight));
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(HasNoEvents));
    }
}

internal sealed partial class MapEditorChainNodeViewModel(int id, string name, string state, bool neverFires, Thickness margin, MapEditorScreenViewModel editor)
{
    public int Id { get; } = id;

    public string Name { get; } = name;

    public string State { get; } = state;

    public bool NeverFires { get; } = neverFires;

    public Thickness Margin { get; } = margin;

    [RelayCommand]
    private void Open() => editor.OpenEvent(Id);
}

// An arrow: the line plus a short head at the end.
internal sealed class MapEditorChainLinkViewModel(Point from, Point to, bool on)
{
    public Point From { get; } = from;

    public Point To { get; } = to;

    public bool On { get; } = on;

    public bool Off => !On;

    public Point HeadA => Head(0.45);

    public Point HeadB => Head(-0.45);

    public string Label => On ? Tr("on") : Tr("off");

    public Thickness LabelMargin => new(((From.X + To.X) / 2) - 8, ((From.Y + To.Y) / 2) - 18, 0, 0);

    private Point Head(double turn)
    {
        double angle = Math.Atan2(From.Y - To.Y, From.X - To.X) + turn;

        return new Point(To.X + (10 * Math.Cos(angle)), To.Y + (10 * Math.Sin(angle)));
    }
}
