using System.Diagnostics.CodeAnalysis;
using Gallean.Common;
using Gallean.Engine;

namespace Gallean.Client;

internal sealed partial class WorldEventQueue :
    IWorldEventVisitor<EngineStep<WorldState, WorldEvent>, Empty>,
    IDisposable
{
    private static readonly TimeSpan PauseDurationDefault = TimeSpan.FromSeconds(0.75);
    private static readonly TimeSpan PauseDurationScenarioPopup = TimeSpan.FromSeconds(0.25);

    private readonly IGameScenarioProvider scenario;
    private readonly IDisposable subscription;
    private readonly Lock locker = new();
    private readonly List<Entry> queue = [];
    private Entry? playingEntry;

    public WorldEventQueue(IGameScenarioProvider scenario, IObservable<EngineStep<WorldState, WorldEvent>> events)
    {
        this.scenario = scenario;

        subscription = events.Subscribe(EnqueueStep);
    }

    public Entry? PlayingEntry
    {
        get
        {
            lock (locker)
            {
                return playingEntry;
            }
        }
    }

    public bool IsIdle
    {
        get
        {
            lock (locker)
            {
                return playingEntry is null && queue.Count == 0;
            }
        }
    }

    public void Dispose()
    {
        subscription.Dispose();
    }

    private void EnqueueStep(EngineStep<WorldState, WorldEvent> step)
    {
        lock (locker)
        {
            if (queue.Count > 0 && queue[^1].TryExtend(step) is { } entry)
            {
                queue[^1] = entry;
            }
            else
            {
                step.Event.Accept(this, step);
            }
        }
    }

    public void EnqueueEntry(Entry entry)
    {
        lock (locker)
        {
            queue.Add(entry);
        }
    }

    public Entry? PlayNext()
    {
        lock (locker)
        {
            if (playingEntry is not null || queue.Count == 0)
            {
                return null;
            }

            playingEntry = queue[0];
            queue.RemoveAt(0);

            return playingEntry;
        }
    }

    public bool TryExtendPlaying([NotNullWhen(true)] out Entry? entry)
    {
        lock (locker)
        {
            if (playingEntry is null || queue.Count == 0)
            {
                entry = null;

                return false;
            }

            entry = playingEntry.TryExtend(queue[0]);

            if (entry is not null)
            {
                playingEntry = entry;
                queue.RemoveAt(0);
            }

            return entry is not null;
        }
    }

    // Gallean Upgrade: drops waiting entries (spell animations cut short by a new cast).
    public void RemoveQueued(Predicate<Entry> match)
    {
        lock (locker)
        {
            queue.RemoveAll(match);
        }
    }

    public int CountQueued(Predicate<Entry> match)
    {
        lock (locker)
        {
            return queue.Count(entry => match(entry));
        }
    }

    // Gallean Upgrade: auto explore (docs/auto-explore.md). Replaces the waiting moves of the given armies with one
    // group entry at the place of the first of them, so they play at the same time. Each army's moves are joined
    // into one walk. Returns the number of armies in the group.
    public int GroupStackMoves(IReadOnlySet<WorldStackId> stackIds)
    {
        lock (locker)
        {
            int first = queue.FindIndex(entry => entry is Entry.StackMove move && stackIds.Contains(move.StackId));

            if (first < 0)
            {
                return 0;
            }

            Dictionary<WorldStackId, Entry.StackMove> walks = [];
            List<WorldStackId> order = [];
            WorldState state = queue[first].State;
            int index = first;

            while (index < queue.Count)
            {
                if (queue[index] is Entry.StackMove move && stackIds.Contains(move.StackId))
                {
                    if (!walks.TryGetValue(move.StackId, out Entry.StackMove? walk))
                    {
                        walks[move.StackId] = move;
                        order.Add(move.StackId);
                    }
                    else if (walk.TryExtend(move) is Entry.StackMove joined)
                    {
                        walks[move.StackId] = joined;
                    }
                    else
                    {
                        // Not a continuation (should not happen): keep the later walk.
                        walks[move.StackId] = move;
                    }

                    state = move.State;
                    queue.RemoveAt(index);
                }
                else
                {
                    index++;
                }
            }

            queue.Insert(first, new Entry.StackMoveGroup(state, [.. order.Select(stackId => walks[stackId])]));

            return order.Count;
        }
    }

    public void Stop()
    {
        lock (locker)
        {
            playingEntry = null;
        }
    }
}
