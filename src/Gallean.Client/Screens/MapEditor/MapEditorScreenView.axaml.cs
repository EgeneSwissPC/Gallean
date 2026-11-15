using Avalonia;
using Avalonia.Controls;
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
