using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Gallean.Common;
using Gallean.Configuration;

namespace Gallean.Client;

// The map editor's map view: renders the edited world with the game's world renderer (WorldScreenSnapshotWorker,
// WorldRenderOperation). Left button paints, right or middle button drags the map, the wheel zooms and WASD or the
// arrow keys scroll, as in the game.
internal sealed class MapEditorVisual : AbstractAnimatedVisual
{
    private const double CameraSpeed = 2000.0;
    private const double ZoomStepFactor = 1.15;

    private static readonly ImmutableSolidColorBrush BackdropBrush = new(Client.Theme.World.BackdropColor);

    private readonly AnimationClock animationClock = new();
    private readonly HashSet<Key> pressedKeys = [];
    private readonly WorldRenderer renderer = new();
    private WorldScreenSnapshotWorker snapshotWorker = null!;
    private WorldCamera camera = null!;
    private Point? panAnchor;
    private bool painting;
    private CleanupList? cleanupList;
    private bool redrawRequested = true;
    private int lastFrameIndex = -1;
    private WorldRenderState? lastWorldState;

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

        if (point.Properties.IsLeftButtonPressed)
        {
            painting = true;
            e.Pointer.Capture(this);
            ViewModel.BeginStroke(WorldTransform.FromWorldPoint(worldPoint));
            e.Handled = true;
        }
        else if (point.Properties.IsRightButtonPressed || point.Properties.IsMiddleButtonPressed)
        {
            panAnchor = worldPoint;
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
        else if (e.InitialPressMouseButton is MouseButton.Right or MouseButton.Middle)
        {
            panAnchor = null;
        }

        e.Pointer.Capture(null);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);

        panAnchor = null;

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

        if (e.Key == Key.Delete && ViewModel.DeleteArmyCommand.CanExecute(null))
        {
            ViewModel.DeleteArmyCommand.Execute(null);
            e.Handled = true;
        }
        else if (IsPanKey(e.Key) && e.KeyModifiers == KeyModifiers.None)
        {
            pressedKeys.Add(e.Key);
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
        bool redraw = redrawRequested || !ReferenceEquals(worldState, lastWorldState);

        if (ViewModel.IsActive)
        {
            UpdateCameraBounds();
            redraw |= UpdateCamera(delta);
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
            TimeSpan.Zero,
            settings.RenderResolutionRatio,
            false,
            ViewModel.Game.DevTools.Diagnostics,
            contentLease));
    }
}
