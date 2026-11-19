using Avalonia;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Gallean.Common;
using Gallean.Configuration;

namespace Gallean.Client;

// The map editor's map view: renders the edited world with the game's world renderer (WorldScreenSnapshotWorker,
// WorldRenderOperation). Input follows the game: left button paints, right button shows the info popup while held,
// the middle button drags the map, the wheel zooms, and the screen edges, WASD and the arrow keys scroll.
internal sealed class MapEditorVisual : AbstractAnimatedVisual
{
    private const double CameraSpeed = 2000.0;
    private const double ZoomStepFactor = 1.15;

    private static readonly ImmutableSolidColorBrush BackdropBrush = new(Client.Theme.World.BackdropColor);
    private static readonly ImmutableSolidColorBrush FitsBrush = new(MapEditorPreviewColors.Fits, MapEditorPreviewColors.FillOpacity);
    private static readonly ImmutableSolidColorBrush ChangesBrush = new(MapEditorPreviewColors.Changes, MapEditorPreviewColors.FillOpacity);
    private static readonly ImmutableSolidColorBrush BlockedBrush = new(MapEditorPreviewColors.Blocked, MapEditorPreviewColors.FillOpacity);

    // Tile diamonds are drawn a little smaller than the tile, so neighbouring outlines don't merge.
    private const double TileInset = 0.06;

    // The game fades new race land in over up to ~2.3 s (terrain expansion). The editor shows every change at once:
    // a time far past the end of any expansion draws the new land and leaves out the fading trees. (With zero, the
    // renderer kept drawing the old land on the last changed tiles until the next change.)
    private static readonly TimeSpan FinishedTerrainExpansion = TimeSpan.FromDays(1);

    private readonly AnimationClock animationClock = new();
    private readonly HashSet<Key> pressedKeys = [];
    private readonly WorldRenderer renderer = new();
    private readonly WorldVisual.ScrollHelper scrollHelper = new();
    private WorldScreenSnapshotWorker snapshotWorker = null!;
    private WorldCamera camera = null!;
    private Point? panAnchor;
    private Point? lastPointer;
    private bool painting;
    private CleanupList? cleanupList;
    private bool redrawRequested = true;
    private int lastFrameIndex = -1;
    private WorldRenderState? lastWorldState;
    private MapEditorPlacementPreview? drawnPreview;
    private PreviewGeometry? previewGeometry;

    public MapEditorVisual()
    {
        Focusable = false;
        ClipToBounds = true;
    }

    private MapEditorScreenViewModel ViewModel => (MapEditorScreenViewModel)DataContext!;
    private IAssetProvider Assets => AssetScope.GetRequiredAssets(this);

    public void Attach(WorldCamera worldCamera, WorldScreenSnapshotWorker worker)
    {
        camera = worldCamera;
        snapshotWorker = worker;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        CleanupList cleanup = new();
        cleanupList = cleanup;

        TopLevel? topLevel = TopLevel.GetTopLevel(this);

        if (topLevel is not null)
        {
            cleanup.Add(topLevel.AddDisposableHandler(KeyDownEvent, OnTopLevelKeyDown, RoutingStrategies.Tunnel));
            cleanup.Add(topLevel.AddDisposableHandler(KeyUpEvent, OnTopLevelKeyUp, RoutingStrategies.Bubble, true));
            topLevel.LostFocus += OnLostFocus;
            cleanup.Add(new ActionDisposable(() => topLevel.LostFocus -= OnLostFocus));
        }

        snapshotWorker.Request(ViewModel.RenderRequest);
        UpdateCameraBounds();

        camera.SetZoom(MapEditorScreenViewModel.InitialZoom);

        if (ViewModel.CameraFocus is { } focus)
        {
            camera.LookAt(WorldTransform.ToWorldPoint(focus));
        }

        redrawRequested = true;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        cleanupList?.Dispose();
        cleanupList = null;
        pressedKeys.Clear();
        scrollHelper.HandleInputReleased();
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);

        camera.ViewportSize = e.NewSize;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);

        if (ViewModel.IsActive && e.Delta.Y != 0)
        {
            double factor = e.Delta.Y > 0 ? ZoomStepFactor : 1.0 / ZoomStepFactor;
            camera.ZoomTo(camera.TargetZoom * factor, e.GetPosition(this));
            redrawRequested = true;
            e.Handled = true;
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (!ViewModel.IsActive)
        {
            return;
        }

        PointerPoint point = e.GetCurrentPoint(this);
        Point worldPoint = GetWorldPoint(e);

        // Take keyboard focus away from the panel, so WASD moves the map instead of typing into a field.
        Focus();

        if (point.Properties.IsLeftButtonPressed && ViewModel.IsLocationTool && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            ViewModel.PlaceLocationAt(WorldTransform.FromWorldPoint(worldPoint));
            e.Handled = true;
        }
        else if (point.Properties.IsLeftButtonPressed)
        {
            painting = true;
            e.Pointer.Capture(this);
            ViewModel.BeginStroke(WorldTransform.FromWorldPoint(worldPoint), e.KeyModifiers.HasFlag(KeyModifiers.Shift));
            e.Handled = true;
        }
        else if (point.Properties.IsRightButtonPressed && (ViewModel.CancelEventPick() || ViewModel.CancelLocationDrag()))
        {
            e.Handled = true;
        }
        else if (point.Properties.IsRightButtonPressed)
        {
            ViewModel.ShowPositionInfo(WorldTransform.FromWorldPoint(worldPoint));
            e.Handled = true;
        }
        else if (point.Properties.IsMiddleButtonPressed)
        {
            panAnchor = worldPoint;
            ViewModel.SetPreviewSuspended(true);
            e.Pointer.Capture(this);
            e.Handled = true;
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (e.InitialPressMouseButton == MouseButton.Left && painting)
        {
            painting = false;
            ViewModel.EndStroke();
        }
        else if (e.InitialPressMouseButton == MouseButton.Right)
        {
            ViewModel.HidePositionInfo();
        }
        else if (e.InitialPressMouseButton == MouseButton.Middle)
        {
            panAnchor = null;
            ViewModel.SetPreviewSuspended(false);
        }

        e.Pointer.Capture(null);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);

        panAnchor = null;
        ViewModel.SetPreviewSuspended(false);
        ViewModel.HidePositionInfo();

        // Losing the mouse mid-drag (another window took it) puts a dragged location back instead of dropping it.
        ViewModel.CancelLocationDrag();

        if (painting)
        {
            painting = false;
            ViewModel.EndStroke();
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (!ViewModel.IsActive)
        {
            return;
        }

        // Edge scrolling only looks at the map view, so the side panel never scrolls the map.
        scrollHelper.HandlePointerMoved(e.GetPosition(this));
        lastPointer = e.GetPosition(this);

        if (panAnchor is not null)
        {
            camera.Move(panAnchor.Value - GetWorldPoint(e));
            redrawRequested = true;
        }

        WorldPosition tile = WorldTransform.FromWorldPoint(GetWorldPoint(e));

        if (painting)
        {
            ViewModel.ContinueStroke(tile);
        }

        ViewModel.UpdateHover(tile);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);

        ViewModel.UpdateHover(null);
        scrollHelper.HandlePointerExited();
    }

    private Point GetWorldPoint(PointerEventArgs e)
    {
        return camera.ScreenToWorld(e.GetPosition(this));
    }

    private static bool IsPanKey(Key key)
    {
        return key is Key.W or Key.A or Key.S or Key.D or Key.Up or Key.Down or Key.Left or Key.Right;
    }

    // Runs before the focused control sees the key, so a focused list or combo box does not swallow the camera keys.
    private void OnTopLevelKeyDown(object? sender, KeyEventArgs e)
    {
        if (!ViewModel.IsActive || TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is TextBox)
        {
            return;
        }

        if (e.KeyModifiers == KeyModifiers.Control)
        {
            if (e.Key == Key.Z && ViewModel.UndoCommand.CanExecute(null))
            {
                ViewModel.UndoCommand.Execute(null);
                e.Handled = true;
            }
            else if (e.Key == Key.S && ViewModel.SaveCommand.CanExecute(null))
            {
                ViewModel.SaveCommand.Execute(null);
                e.Handled = true;
            }

            return;
        }

        if (IsPanKey(e.Key) && e.KeyModifiers == KeyModifiers.None)
        {
            pressedKeys.Add(e.Key);
            e.Handled = true;
        }
        else if (e.Key == Key.P && e.KeyModifiers == KeyModifiers.None)
        {
            ViewModel.IsPreviewEnabled = !ViewModel.IsPreviewEnabled;
            e.Handled = true;
        }
        else if (e.Key == Key.E && e.KeyModifiers == KeyModifiers.None)
        {
            ViewModel.IsEconomyOpen = !ViewModel.IsEconomyOpen;
            e.Handled = true;
        }
        else if (e.Key == Key.L && e.KeyModifiers == KeyModifiers.None)
        {
            ViewModel.IsLocationsOpen = !ViewModel.IsLocationsOpen;
            e.Handled = true;
        }
        else if (e.Key == Key.V && e.KeyModifiers == KeyModifiers.None)
        {
            ViewModel.IsEventsOpen = !ViewModel.IsEventsOpen;
            e.Handled = true;
        }
        else if (ViewModel.IsLocationTool && e.Key is Key.OemPlus or Key.Add or Key.OemMinus or Key.Subtract)
        {
            ViewModel.ChangeLocationRadius(e.Key is Key.OemPlus or Key.Add ? 1 : -1);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None && (ViewModel.CancelEventPick() || ViewModel.CancelLocationDrag() || ViewModel.CancelRoad()))
        {
            e.Handled = true;
        }
    }

    private void OnTopLevelKeyUp(object? sender, KeyEventArgs e)
    {
        pressedKeys.Remove(e.Key);
    }

    private void OnLostFocus(object? sender, RoutedEventArgs e)
    {
        pressedKeys.Clear();
        scrollHelper.HandleInputReleased();
    }

    protected override void OnFrame(TimeSpan frameTime)
    {
        Exception? workerException = snapshotWorker.Exception;

        if (workerException is not null)
        {
            StopFrames();
            ViewModel.Game.ShowError(Tr("Failed to draw the map: {0}", workerException.Message));
            return;
        }

        TimeSpan delta = animationClock.Tick();
        snapshotWorker.Request(ViewModel.RenderRequest);

        WorldRenderState? worldState = snapshotWorker.Current?.WorldState;
        int frameIndex = AnimationRate.Sprites.GetFrameIndex(animationClock.Now);
        bool redraw = redrawRequested || !ReferenceEquals(worldState, lastWorldState) || !ReferenceEquals(ViewModel.PlacementPreview, drawnPreview);

        if (ViewModel.IsActive)
        {
            UpdateCameraBounds();

            if (ViewModel.TakeCameraTarget() is { } target)
            {
                camera.LookAt(WorldTransform.ToWorldPoint(target));
                redraw = true;
            }

            if (UpdateCamera(delta))
            {
                redraw = true;

                // The map scrolled under a still mouse (edge scrolling while dragging or painting): follow the cursor.
                if (painting && lastPointer is { } pointer)
                {
                    WorldPosition tile = WorldTransform.FromWorldPoint(camera.ScreenToWorld(pointer));
                    ViewModel.ContinueStroke(tile);
                    ViewModel.UpdateHover(tile);
                }
            }
            redraw |= frameIndex != lastFrameIndex;
        }

        if (redraw)
        {
            redrawRequested = false;
            lastFrameIndex = frameIndex;
            lastWorldState = worldState;
            InvalidateVisual();
        }
    }

    private void UpdateCameraBounds()
    {
        TopLevel? topLevel = TopLevel.GetTopLevel(this);

        camera.ViewportSize = Bounds.Size;
        camera.MapWidth = ViewModel.MapWidth;
        camera.MapHeight = ViewModel.MapHeight;

        if (topLevel is not null)
        {
            double viewportScaling = UiScaleDecorator.GetViewportScaling(topLevel.ClientSize.Height * topLevel.RenderScaling);
            camera.UiScale = UiScaleDecorator.GetRenderScaling(this) / viewportScaling;
        }
    }

    private bool UpdateCamera(TimeSpan delta)
    {
        double directionX = (IsPressed(Key.Right, Key.D) ? 1 : 0) - (IsPressed(Key.Left, Key.A) ? 1 : 0);
        double directionY = (IsPressed(Key.Down, Key.S) ? 1 : 0) - (IsPressed(Key.Up, Key.W) ? 1 : 0);
        bool moved = false;

        if (directionX == 0 && directionY == 0)
        {
            Vector edge = scrollHelper.GetDirection(Bounds.Size);
            directionX = edge.X;
            directionY = edge.Y;
        }

        if (directionX != 0 || directionY != 0)
        {
            Vector normalized = new Vector(directionX, directionY).Normalize();
            moved = camera.PanBy(normalized * (CameraSpeed * delta.TotalSeconds / camera.Zoom));
        }

        return camera.Animate(delta) || moved;
    }

    private bool IsPressed(Key arrow, Key letter)
    {
        return pressedKeys.Contains(arrow) || pressedKeys.Contains(letter);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        WorldScreenSnapshot? snapshot = snapshotWorker.AcquireCurrent(out IDisposable? contentLease);

        if (snapshot is null)
        {
            context.FillRectangle(BackdropBrush, new Rect(Bounds.Size));
            return;
        }

        GameSettings settings = SettingsScope.GetSettings(this);

        context.Custom(new WorldRenderOperation(
            new Rect(Bounds.Size),
            camera.GetViewMatrix(),
            snapshot.WorldState,
            snapshot.WorldContent,
            renderer,
            Assets,
            animationClock.Now,
            TimeSpan.Zero,
            FinishedTerrainExpansion,
            settings.RenderResolutionRatio,
            false,
            ViewModel.Game.DevTools.Diagnostics,
            contentLease));

        DrawLocations(context);
        DrawPlacementPreview(context);
    }

    // Locations (editor only; the game never draws them): a pale square per zone and its name at its top corner, in the
    // outlined style of the game's path cost labels. The selected location and the hovered list row are gold.
    private void DrawLocations(DrawingContext context)
    {
        IReadOnlyList<MapEditorLocationMark> marks = ViewModel.PlacementPreview.Locations;

        if (marks.Count == 0)
        {
            return;
        }

        Matrix view = camera.GetViewMatrix();
        double pixel = 1.0 / Math.Max(0.01, Math.Abs(view.M11));
        ImmutableSolidColorBrush fill = new(MapEditorPreviewColors.Location, MapEditorPreviewColors.LocationFillOpacity);
        Pen outline = new(new SolidColorBrush(MapEditorPreviewColors.Location, MapEditorPreviewColors.OutlineOpacity), 1.5 * pixel);
        Pen halo = new(new SolidColorBrush(MapEditorPreviewColors.Halo), 5 * pixel);
        Pen glow = new(new SolidColorBrush(MapEditorPreviewColors.Glow), 2.5 * pixel);

        using (context.PushTransform(view))
        {
            foreach (MapEditorLocationMark mark in marks)
            {
                Geometry square = PreviewGeometry.Square(mark.Footprint);
                context.DrawGeometry(fill, mark.Marked ? null : outline, square);

                if (mark.Marked)
                {
                    context.DrawGeometry(null, halo, square);
                    context.DrawGeometry(null, glow, square);
                }
            }
        }

        Typeface typeface = new(TextElement.GetFontFamily(this), FontStyle.Normal, FontWeight.SemiBold);
        IBrush labelFill = new SolidColorBrush(MapEditorPreviewColors.LocationLabel);
        IBrush markedFill = new SolidColorBrush(MapEditorPreviewColors.GlowInner);
        Pen labelOutline = new(new SolidColorBrush(MapEditorPreviewColors.LocationLabelOutline), 3, lineJoin: PenLineJoin.Round);

        foreach (MapEditorLocationMark mark in marks)
        {
            FormattedText text = new(mark.Name, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, 14, labelFill);
            // On the square's top corner tile, so zones sharing a centre (a small trap inside a larger warning zone)
            // don't hide each other's names.
            Point center = WorldTransform.ToWorldPoint(mark.Footprint.TopLeft).Transform(view);
            Geometry? glyphs = text.BuildGeometry(new Point(center.X - (text.Width / 2), center.Y - (text.Height / 2)));

            if (glyphs is not null)
            {
                context.DrawGeometry(null, labelOutline, glyphs);
                context.DrawGeometry(mark.Marked ? markedFill : labelFill, null, glyphs);
            }
        }
    }

    // The placement preview, drawn over the map like the game's cursor: semi-transparent tile diamonds with an
    // outline, the gold outline of an object a click opens or removes, and a faint outline of a capital's current
    // place when placing it again moves it. Editor-only; the game's renderer is untouched.
    private void DrawPlacementPreview(DrawingContext context)
    {
        MapEditorPlacementPreview preview = ViewModel.PlacementPreview;

        if (!ReferenceEquals(preview, drawnPreview))
        {
            drawnPreview = preview;
            previewGeometry = PreviewGeometry.Build(preview);
        }

        if (previewGeometry is not { } geometry)
        {
            return;
        }

        Matrix view = camera.GetViewMatrix();
        double pixel = 1.0 / Math.Max(0.01, Math.Abs(view.M11));

        using (context.PushTransform(view))
        {
            bool fill = !preview.OutlineOnly;
            DrawTiles(context, geometry.Fits, fill ? FitsBrush : null, MapEditorPreviewColors.Fits, pixel);
            DrawTiles(context, geometry.Changes, fill ? ChangesBrush : null, MapEditorPreviewColors.Changes, pixel);
            DrawTiles(context, geometry.Blocked, BlockedBrush, MapEditorPreviewColors.Blocked, pixel);

            if (geometry.Footprint is not null)
            {
                Color color = preview.Fits ? MapEditorPreviewColors.Fits : MapEditorPreviewColors.Blocked;
                context.DrawGeometry(null, new Pen(new SolidColorBrush(color), 2.5 * pixel), geometry.Footprint);
            }

            if (geometry.MovesFrom is not null)
            {
                context.DrawGeometry(null, new Pen(new SolidColorBrush(MapEditorPreviewColors.MovesFrom), 1.5 * pixel, new DashStyle([4, 3], 0)), geometry.MovesFrom);
            }

            if (geometry.Highlight is not null)
            {
                context.DrawGeometry(null, new Pen(new SolidColorBrush(MapEditorPreviewColors.Halo), 5 * pixel), geometry.Highlight);
                context.DrawGeometry(null, new Pen(new SolidColorBrush(MapEditorPreviewColors.Glow), 2.5 * pixel), geometry.Highlight);
            }

            if (geometry.Glow is not null)
            {
                context.DrawGeometry(null, new Pen(new SolidColorBrush(MapEditorPreviewColors.Halo), 6 * pixel), geometry.Glow);
                context.DrawGeometry(null, new Pen(new SolidColorBrush(MapEditorPreviewColors.Glow), 3.5 * pixel), geometry.Glow);
                context.DrawGeometry(null, new Pen(new SolidColorBrush(MapEditorPreviewColors.GlowInner), 1.2 * pixel), geometry.Glow);
            }
        }
    }

    private static void DrawTiles(DrawingContext context, Geometry? tiles, IBrush? fill, Color outline, double pixel)
    {
        if (tiles is not null)
        {
            context.DrawGeometry(fill, new Pen(new SolidColorBrush(outline, MapEditorPreviewColors.OutlineOpacity), 1.5 * pixel), tiles);
        }
    }

    private sealed record PreviewGeometry(Geometry? Fits, Geometry? Changes, Geometry? Blocked, Geometry? Glow, Geometry? MovesFrom, Geometry? Footprint)
    {
        public Geometry? Highlight { get; init; }

        public static PreviewGeometry? Build(MapEditorPlacementPreview preview)
        {
            if (preview.Tiles.Count == 0 && preview.Glow is null && preview.MovesFrom is null && preview.Highlight.Count == 0)
            {
                return null;
            }

            return new PreviewGeometry(
                Tiles(preview, MapEditorTileState.Fits),
                Tiles(preview, MapEditorTileState.Changes),
                Tiles(preview, MapEditorTileState.Blocked),
                preview.Glow is { } glow ? Outline(glow) : null,
                preview.MovesFrom is { } moves ? Outline(moves) : null,
                preview.Footprint is { } footprint ? Outline(footprint) : null)
            {
                Highlight = preview.Highlight.Count > 0 ? Diamonds(preview.Highlight) : null
            };
        }

        private static StreamGeometry Diamonds(IReadOnlyList<WorldPosition> tiles)
        {
            StreamGeometry geometry = new();

            using (StreamGeometryContext stream = geometry.Open())
            {
                foreach (WorldPosition tile in tiles)
                {
                    AddDiamond(stream, tile.X - 0.5, tile.Y - 0.5, tile.X + 0.5, tile.Y + 0.5);
                }
            }

            return geometry;
        }

        private static StreamGeometry? Tiles(MapEditorPlacementPreview preview, MapEditorTileState state)
        {
            StreamGeometry geometry = new();
            bool any = false;

            using (StreamGeometryContext stream = geometry.Open())
            {
                foreach (MapEditorPreviewTile tile in preview.Tiles)
                {
                    if (tile.State == state)
                    {
                        AddDiamond(stream, tile.Position.X - 0.5 + TileInset, tile.Position.Y - 0.5 + TileInset, tile.Position.X + 0.5 - TileInset, tile.Position.Y + 0.5 - TileInset);
                        any = true;
                    }
                }
            }

            return any ? geometry : null;
        }

        public static StreamGeometry Square(WorldFootprint footprint)
        {
            return Outline(footprint);
        }

        private static StreamGeometry Outline(WorldFootprint footprint)
        {
            StreamGeometry geometry = new();

            using (StreamGeometryContext stream = geometry.Open())
            {
                AddDiamond(
                    stream,
                    footprint.TopLeft.X - 0.5,
                    footprint.TopLeft.Y - 0.5,
                    footprint.TopLeft.X + footprint.Width - 0.5,
                    footprint.TopLeft.Y + footprint.Height - 0.5);
            }

            return geometry;
        }

        // The tile-space rectangle (x0, y0)-(x1, y1) is a diamond on screen.
        private static void AddDiamond(StreamGeometryContext stream, double x0, double y0, double x1, double y1)
        {
            stream.BeginFigure(WorldTransform.ToWorldPoint(x0, y0), isFilled: true);
            stream.LineTo(WorldTransform.ToWorldPoint(x1, y0));
            stream.LineTo(WorldTransform.ToWorldPoint(x1, y1));
            stream.LineTo(WorldTransform.ToWorldPoint(x0, y1));
            stream.EndFigure(isClosed: true);
        }
    }
}
