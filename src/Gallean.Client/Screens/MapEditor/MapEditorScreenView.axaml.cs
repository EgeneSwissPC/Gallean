using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Gallean.Engine;

namespace Gallean.Client;

internal sealed partial class MapEditorScreenView : UserControl
{
    private readonly IGameDefinitionProvider definitions;
    private readonly IGameScenarioProvider scenario;
    private readonly DiagnosticsRecorder diagnostics;

    public MapEditorScreenView(IGameDefinitionProvider definitions, IGameScenarioProvider scenario, DiagnosticsRecorder diagnostics)
    {
        this.definitions = definitions;
        this.scenario = scenario;
        this.diagnostics = diagnostics;

        AvaloniaXamlLoader.Load(this);

        this.FindControl<MapEditorVisual>("Map")!.AddHandler(PointerPressedEvent, OnMapPointerPressed, RoutingStrategies.Tunnel);

        // Selecting a location on the map scrolls the list to its row.
        ListBox locationList = this.FindControl<ListBox>("LocationList")!;
        locationList.SelectionChanged += (_, _) =>
        {
            if (locationList.SelectedItem is { } item)
            {
                locationList.ScrollIntoView(item);
            }
        };
    }

    private MapEditorScreenViewModel? EditorViewModel => DataContext as MapEditorScreenViewModel;

    private void OnLocationRowPointerEntered(object? sender, PointerEventArgs e)
    {
        if (sender is Control { DataContext: MapEditorLocationRowViewModel row })
        {
            EditorViewModel?.SetHoveredLocation(row.Id);
        }
    }

    private void OnLocationRowPointerExited(object? sender, PointerEventArgs e)
    {
        EditorViewModel?.SetHoveredLocation(null);
    }

    private void OnLocationRowDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { DataContext: IMapEditorRenameRow row })
        {
            row.BeginEditCommand.Execute(null);
        }
    }

    // Events list: a double-click opens the event's block editor (the name is renamed from the row's button).
    private void OnEventRowDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { DataContext: MapEditorEventRowViewModel row } && !row.IsEditing)
        {
            row.OpenCommand.Execute(null);
        }
    }

    // The name box: Enter keeps the typed name, Esc the old one.
    private void OnLocationNameKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is Control { DataContext: IMapEditorRenameRow row })
        {
            if (e.Key == Key.Enter)
            {
                row.CommitEditCommand.Execute(null);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                row.CancelEditCommand.Execute(null);
                e.Handled = true;
            }
        }
    }

    // The X/Y boxes of the selected location: Enter or leaving the box moves it.
    private void OnLocationPositionKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            EditorViewModel?.ApplyLocationPosition();
            e.Handled = true;
        }
    }

    private void OnLocationPositionLostFocus(object? sender, RoutedEventArgs e)
    {
        EditorViewModel?.ApplyLocationPosition();
    }

    private void OnLocationNameLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: IMapEditorRenameRow row })
        {
            row.CommitEditCommand.Execute(null);
        }
    }

    // When the box appears (a new location, or a double-click), it takes the focus with the name selected.
    private void OnLocationNameBoxPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == IsVisibleProperty && e.NewValue is true && sender is TextBox box)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                box.Focus();
                box.SelectAll();
            });
        }
    }

    // Like the world screen: the right-click info popup opens next to the cursor.
    private void OnMapPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        MapEditorVisual map = (MapEditorVisual)sender!;
        PointerPoint point = e.GetCurrentPoint(map);

        if (point.Properties.IsRightButtonPressed)
        {
            Rect cursor = new(point.Position, new Size(1, 1));
            this.FindControl<Popup>("PositionInfoPopup")!.PlacementRect = cursor.Inflate(new Thickness(24, 0));
        }
    }

    internal WorldScreenSnapshotWorker? SnapshotWorker { get; private set; }
    internal WorldCamera Camera { get; } = new() { MinZoom = MapEditorScreenViewModel.MinZoom };

    private IAssetProvider Assets => AssetScope.GetRequiredAssets(this);

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        SnapshotWorker = new WorldScreenSnapshotWorker(Assets, definitions, scenario, diagnostics);

        this.FindControl<MapEditorVisual>("Map")!.Attach(Camera, SnapshotWorker);
        this.FindControl<MinimapVisual>("Minimap")!.Attach(Camera, SnapshotWorker);

        base.OnAttachedToVisualTree(e);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        SnapshotWorker?.Dispose();
        SnapshotWorker = null;
    }
}
