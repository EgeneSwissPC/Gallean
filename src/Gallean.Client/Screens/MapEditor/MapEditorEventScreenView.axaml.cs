using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;

namespace Gallean.Client;

// Drag and drop of blocks, Scratch-style: press a palette block or a script block, move, and let go over the script
// (before or after the block under the mouse, or at the end of a group / of Then) or over the palette (removes a
// script block). A press without a move on a palette block is a click (adds it). The view only finds where the drop
// lands; the view model decides whether it is allowed (conditions only in groups, effects only in Then).
internal sealed partial class MapEditorEventScreenView : UserControl
{
    private const double DragThreshold = 6;

    private MapEditorPaletteBlock? pressedPalette;
    private MapEditorEventBlockViewModel? pressedBlock;
    private Point pressPoint;
    private bool dragging;

    public MapEditorEventScreenView()
    {
        AvaloniaXamlLoader.Load(this);
        AddHandler(PointerMovedEvent, OnPointerMovedAnywhere, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, OnPointerReleasedAnywhere, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerCaptureLostEvent, (_, _) => EndDrag(), RoutingStrategies.Bubble, handledEventsToo: true);
    }

    private MapEditorEventScreenViewModel? ViewModel => DataContext as MapEditorEventScreenViewModel;

    private void OnPaletteBlockPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: MapEditorPaletteBlock palette } && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            pressedPalette = palette;
            pressedBlock = null;
            StartPress(palette.Label, palette.Brush, e);
        }
    }

    // Presses on the block's own fields (boxes, buttons, drop-downs) edit the field instead of dragging.
    private void OnBlockPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: MapEditorEventBlockViewModel block } ||
            !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed ||
            IsInsideField(e.Source as Visual, (Visual)sender))
        {
            return;
        }

        pressedBlock = block;
        pressedPalette = null;
        string label = string.Join(" ", block.Parts.OfType<MapEditorBlockText>().Select(part => part.Text));
        StartPress(label, block.Brush, e);
    }

    private void OnRemoveBlockClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: MapEditorEventBlockViewModel block })
        {
            ViewModel?.RemoveBlockCommand.Execute(block);
        }
    }

    private void StartPress(string label, Avalonia.Media.IBrush brush, PointerPressedEventArgs e)
    {
        pressPoint = e.GetPosition(this);
        dragging = false;
        this.FindControl<TextBlock>("DragGhostText")!.Text = label;
        this.FindControl<Border>("DragGhost")!.Background = brush;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    private void OnPointerMovedAnywhere(object? sender, PointerEventArgs e)
    {
        if (pressedPalette is null && pressedBlock is null)
        {
            return;
        }

        Point point = e.GetPosition(this);

        if (!dragging && (Math.Abs(point.X - pressPoint.X) > DragThreshold || Math.Abs(point.Y - pressPoint.Y) > DragThreshold))
        {
            dragging = true;
            this.FindControl<Border>("DragGhost")!.IsVisible = true;
        }

        if (dragging)
        {
            Border ghost = this.FindControl<Border>("DragGhost")!;
            Canvas.SetLeft(ghost, point.X + 12);
            Canvas.SetTop(ghost, point.Y + 8);
        }
    }

    private void OnPointerReleasedAnywhere(object? sender, PointerReleasedEventArgs e)
    {
        if (pressedPalette is null && pressedBlock is null)
        {
            return;
        }

        MapEditorEventScreenViewModel? viewModel = ViewModel;

        if (viewModel is not null)
        {
            if (dragging)
            {
                Drop(viewModel, e.GetPosition(this));
            }
            else if (pressedPalette is { } palette)
            {
                viewModel.AddBlockCommand.Execute(palette);
            }
        }

        e.Pointer.Capture(null);
        EndDrag();
    }

    private void EndDrag()
    {
        pressedPalette = null;
        pressedBlock = null;
        dragging = false;
        this.FindControl<Border>("DragGhost")!.IsVisible = false;
    }

    private void Drop(MapEditorEventScreenViewModel viewModel, Point point)
    {
        Visual? hit = this.InputHitTest(point) as Visual;
        Control? target = hit?.GetSelfAndVisualAncestors().OfType<Control>().FirstOrDefault(control =>
            control.DataContext is MapEditorEventBlockViewModel && control is Border { Classes: var classes } && classes.Contains("block") ||
            control.Tag is "group" or "effects" or "palette");

        if (target is null)
        {
            return;
        }

        if (target.Tag is "palette")
        {
            if (pressedBlock is { } removed)
            {
                viewModel.RemoveBlockCommand.Execute(removed);
            }

            return;
        }

        IList<MapEditorEventBlockViewModel>? list;
        int index;

        if (target.DataContext is MapEditorEventBlockViewModel over)
        {
            if (ReferenceEquals(over, pressedBlock) || viewModel.FindList(over) is not { } overList)
            {
                return;
            }

            list = overList;
            Point local = this.TranslatePoint(point, target) ?? default;
            index = list.IndexOf(over) + (local.Y > target.Bounds.Height / 2 ? 1 : 0);
        }
        else if (target.Tag is "effects")
        {
            list = viewModel.Effects;
            index = list.Count;
        }
        else if (target.DataContext is MapEditorEventGroupViewModel group)
        {
            list = group.Blocks;
            index = list.Count;
        }
        else
        {
            return;
        }

        if (pressedPalette is { } palette)
        {
            viewModel.InsertBlock(palette, list, index);
        }
        else if (pressedBlock is { } block)
        {
            viewModel.MoveBlock(block, list, index);
        }
    }

    private static bool IsInsideField(Visual? source, Visual block)
    {
        for (Visual? visual = source; visual is not null && !ReferenceEquals(visual, block); visual = visual.GetVisualParent())
        {
            if (visual is TextBox or ComboBox or Button or CheckBox)
            {
                return true;
            }
        }

        return false;
    }
}
