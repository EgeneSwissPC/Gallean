namespace Gallean.Engine;

// Event chains (docs/map-editor-events.md, phase 7): which event switches which on or off, and which events can ever
// fire. An event can fire when it starts switched on, or when an event that can fire switches it on. Layers place an
// event one column right of the first event that switches it on, so a quest reads left to right.
public sealed partial class CustomMap
{
    public CustomMapEventChains GetEventChains()
    {
        List<CustomMapEventLink> links = [];

        foreach (CustomMapEvent ev in events)
        {
            foreach (CustomMapSwitchEventEffect effect in ev.Effects.OfType<CustomMapSwitchEventEffect>())
            {
                if (GetEvent(effect.EventId) is not null && !links.Contains(new CustomMapEventLink(ev.Id, effect.EventId, effect.On)))
                {
                    links.Add(new CustomMapEventLink(ev.Id, effect.EventId, effect.On));
                }
            }
        }

        // Breadth first from the events switched on at the start, along "switch on" links.
        Dictionary<int, int> layers = [];
        Queue<int> queue = new();

        foreach (CustomMapEvent ev in events.Where(ev => ev.Enabled))
        {
            layers[ev.Id] = 0;
            queue.Enqueue(ev.Id);
        }

        while (queue.TryDequeue(out int id))
        {
            foreach (CustomMapEventLink link in links.Where(link => link.From == id && link.On && !layers.ContainsKey(link.To)))
            {
                layers[link.To] = layers[id] + 1;
                queue.Enqueue(link.To);
            }
        }

        HashSet<int> reachable = [.. layers.Keys];

        // Events that can never fire: one column right of whatever (unreachable) switches them on, else column 0.
        foreach (CustomMapEvent ev in events.Where(ev => !reachable.Contains(ev.Id)).OrderBy(ev => ev.Id))
        {
            layers[ev.Id] = links.Where(link => link.To == ev.Id && link.On && layers.ContainsKey(link.From)).Select(link => layers[link.From] + 1).DefaultIfEmpty(0).Max();
        }

        return new CustomMapEventChains(links, layers, [.. events.Where(ev => !reachable.Contains(ev.Id)).Select(ev => ev.Id)]);
    }
}

public sealed record CustomMapEventLink(int From, int To, bool On);

// Layers: column per event ID. NeverFires: events that start switched off and that no event that can fire switches on.
public sealed record CustomMapEventChains(IReadOnlyList<CustomMapEventLink> Links, IReadOnlyDictionary<int, int> Layers, IReadOnlyList<int> NeverFires);
