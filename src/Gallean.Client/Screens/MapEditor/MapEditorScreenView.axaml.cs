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
