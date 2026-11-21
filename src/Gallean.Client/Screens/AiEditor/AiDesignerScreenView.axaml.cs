using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;

namespace Gallean.Client;

// Gallean Upgrade (AI designer): drag and drop like the map editor's event screen. Press a palette block or
// a stage block, move, and let go over a condition set, a priority row, the "new lowest row" strip, the rules
// area or the palette (removes the block). A press without a move on a palette block is a click (adds it to
// the selected stage). The view finds where the drop lands; the view model decides whether it fits.
internal sealed partial class AiDesignerScreenView : UserControl
{
    private const double DragThreshold = 6;

    private AiDesignerPaletteItem? pressedPalette;
    private AiDesignerBlockViewModel? pressedBlock;
    private Point pressPoint;
    private bool dragging;

    public AiDesignerScreenView()
    {
        AvaloniaXamlLoader.Load(this);
        AddHandler(PointerMovedEvent, OnPointerMovedAnywhere, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, OnPointerReleasedAnywhere, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerCaptureLostEvent, (_, _) => EndDrag(), RoutingStrategies.Bubble, handledEventsToo: true);
    }

    private AiDesignerScreenViewModel? ViewModel => DataContext as AiDesignerScreenViewModel;

    private AiDesignerScreenViewModel? subscribed;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (subscribed is not null)
        {
            subscribed.StageAdded -= OnStageAdded;
        }

        subscribed = ViewModel;

        if (subscribed is not null)
        {
            subscribed.StageAdded += OnStageAdded;
        }
    }

    // A new stage is added above the last card, usually out of sight: scroll to its top.
    private void OnStageAdded(AiDesignerStageViewModel stage)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            Border? card = this.GetVisualDescendants().OfType<Border>()
                .FirstOrDefault(border => border.Classes.Contains("stage") && ReferenceEquals(border.DataContext, stage));
            card?.BringIntoView(new Rect(0, 0, card.Bounds.Width, Math.Min(card.Bounds.Height, 120)));
        }, Avalonia.Threading.DispatcherPriority.Background);
    }

    private void OnPaletteBlockEntered(object? sender, PointerEventArgs e)
    {
        if (sender is Control { DataContext: AiDesignerPaletteItem item } && ViewModel is { } viewModel)
        {
            viewModel.HelpItem = item;
        }
    }

    private void OnPaletteBlockPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: AiDesignerPaletteItem item } && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            pressedPalette = item;
            pressedBlock = null;
            StartPress(item.Label, item.Brush, e);
        }
    }

    // Presses on a block's number boxes and buttons edit them instead of dragging.
    private void OnBlockPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: AiDesignerBlockViewModel block } ||
            !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed ||
            IsInsideField(e.Source as Visual, (Visual)sender))
        {
            return;
        }

        pressedBlock = block;
        pressedPalette = null;

        if (ViewModel is { } viewModel)
        {
            viewModel.HelpItem = block.Item;
        }

        StartPress(block.Label, block.Brush, e);
    }

    private void OnStagePressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: AiDesignerStageViewModel stage })
        {
            ViewModel?.SelectStage(stage);
        }
    }

    private void OnRemoveBlockClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: AiDesignerBlockViewModel block })
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

        if (ViewModel is { } viewModel)
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

    private void Drop(AiDesignerScreenViewModel viewModel, Point point)
    {
        Visual? hit = this.InputHitTest(point) as Visual;
        Control? target = hit?.GetSelfAndVisualAncestors().OfType<Control>().FirstOrDefault(control =>
            control.DataContext is AiDesignerBlockViewModel && control is Border { Classes: var classes } && classes.Contains("block") ||
            control.Tag is "group" or "tier" or "newtier" or "rules" or "palette");

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

        object? container;
        int index;

        if (target.DataContext is AiDesignerBlockViewModel over)
        {
            if (ReferenceEquals(over, pressedBlock) || viewModel.FindContainer(over) is not { } overContainer)
            {
                return;
            }

            container = overContainer;
            Point local = this.TranslatePoint(point, target) ?? default;
            int position = overContainer switch
            {
                AiDesignerGroupViewModel group => group.Blocks.IndexOf(over),
                AiDesignerTierViewModel tier => tier.Blocks.IndexOf(over),
                AiDesignerStageViewModel stage => stage.Rules.IndexOf(over),
                _ => 0
            };

            // Rows and condition sets run left to right, rules top to bottom.
            bool after = overContainer is AiDesignerStageViewModel ? local.Y > target.Bounds.Height / 2 : local.X > target.Bounds.Width / 2;
            index = position + (after ? 1 : 0);
        }
        else
        {
            container = target.DataContext;
            index = int.MaxValue;
        }

        if (container is null)
        {
            return;
        }

        AiDesignerPaletteItem? item = pressedPalette ?? pressedBlock?.Item;

        if (item is null || !AiDesignerScreenViewModel.Accepts(container, item))
        {
            viewModel.StatusText = Tr("That block doesn't fit there.");
            return;
        }

        if (pressedBlock is { } block)
        {
            viewModel.MoveBlock(block, container, index);
        }
        else
        {
            viewModel.InsertBlock(item, container, index);
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
