using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Gallean.Client;

// Lays out the army cards along the bottom of the world screen. While the cards fit, it behaves like a
// horizontal StackPanel centred on the window (CenterShift makes up for space reserved on one side).
// When they don't fit, unselected cards overlap more (down to CompactStep visible pixels each) and the
// selected card moves SelectedGap away from its neighbours so it shows whole. If even that is too wide,
// the bar scrolls: the mouse wheel moves it and a newly selected card is brought into view.
internal sealed class WorldStackBarPanel : Panel
{
    public static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<WorldStackBarPanel, double>(nameof(Spacing));

    public static readonly StyledProperty<double> CompactStepProperty =
        AvaloniaProperty.Register<WorldStackBarPanel, double>(nameof(CompactStep), 48.0);

    public static readonly StyledProperty<double> SelectedGapProperty =
        AvaloniaProperty.Register<WorldStackBarPanel, double>(nameof(SelectedGap), 24.0);

    public static readonly StyledProperty<double> CenterShiftProperty =
        AvaloniaProperty.Register<WorldStackBarPanel, double>(nameof(CenterShift));

    public static readonly AttachedProperty<bool> IsSelectedProperty =
        AvaloniaProperty.RegisterAttached<WorldStackBarPanel, Control, bool>("IsSelected");

    // Vertical room kept unclipped while scrolling (raised cards, action sockets and captions above them).
    private const double ClipOverflow = 1000.0;

    private const int WheelCards = 3;

    private object? lastSelectedItem;

    // True from a selection change until the player scrolls with the wheel: the selected card is kept in view
    // (it can still grow after the selection changes, and cards can come and go).
    private bool isFollowingSelection;

    static WorldStackBarPanel()
    {
        AffectsMeasure<WorldStackBarPanel>(SpacingProperty, CompactStepProperty, SelectedGapProperty, CenterShiftProperty);
        AffectsParentMeasure<WorldStackBarPanel>(IsSelectedProperty);
    }

    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    public double CompactStep
    {
        get => GetValue(CompactStepProperty);
        set => SetValue(CompactStepProperty, value);
    }

    public double SelectedGap
    {
        get => GetValue(SelectedGapProperty);
        set => SetValue(SelectedGapProperty, value);
    }

    public double CenterShift
    {
        get => GetValue(CenterShiftProperty);
        set => SetValue(CenterShiftProperty, value);
    }

    public double ScrollOffset { get; private set; }

    public bool IsScrolling { get; private set; }

    public static bool GetIsSelected(Control control)
    {
        return control.GetValue(IsSelectedProperty);
    }

    public static void SetIsSelected(Control control, bool value)
    {
        control.SetValue(IsSelectedProperty, value);
    }

    public void ScrollBy(double delta)
    {
        if (IsScrolling)
        {
            ScrollOffset += delta;
            isFollowingSelection = false;
            InvalidateArrange();
        }
    }

    internal static Layout Compute(
        IReadOnlyList<double> widths,
        int selectedIndex,
        double available,
        double spacing,
        double compactStep,
        double selectedGap,
        double centerShift,
        double scrollOffset)
    {
        int count = widths.Count;
        double[] positions = new double[count];

        if (count == 0)
        {
            return new Layout(positions, 0.0, IsCompact: false, IsScrolling: false, 0.0);
        }

        double natural = widths.Sum() + spacing * (count - 1);

        if (double.IsInfinity(available) || natural <= available)
        {
            double start = double.IsInfinity(available) ? 0.0 : Math.Clamp(available / 2 + centerShift - natural / 2, 0.0, available - natural);
            double x = start;

            for (int index = 0; index < count; index++)
            {
                positions[index] = x;
                x += widths[index] + spacing;
            }

            return new Layout(positions, natural, IsCompact: false, IsScrolling: false, 0.0);
        }

        // Fixed part: the last card shows whole, the selected card shows whole with a gap on each side.
        // Card art has transparent sides: Spacing (negative) is the overlap at which two cards' art just touch.
        bool hasSelected = selectedIndex >= 0 && selectedIndex < count;
        double fixedWidth = widths[count - 1];
        int stepCount = count - 1;

        if (hasSelected && selectedIndex < count - 1)
        {
            fixedWidth += widths[selectedIndex] + spacing + selectedGap;
            stepCount--;
        }

        if (hasSelected && selectedIndex > 0)
        {
            fixedWidth += selectedGap;
        }

        double naturalStep = widths[0] + spacing;
        double step = stepCount > 0 ? Math.Clamp((available - fixedWidth) / stepCount, compactStep, Math.Max(compactStep, naturalStep)) : naturalStep;
        double content = fixedWidth + stepCount * step;
        bool isScrolling = content > available;
        double offset = isScrolling ? Math.Clamp(scrollOffset, 0.0, content - available) : 0.0;
        double left = isScrolling ? -offset : Math.Clamp(available / 2 + centerShift - content / 2, 0.0, available - content);
        double position = left;

        for (int index = 0; index < count; index++)
        {
            positions[index] = position;

            if (hasSelected && index == selectedIndex)
            {
                position += widths[index] + spacing + selectedGap;
            }
            else
            {
                position += step;

                if (hasSelected && index + 1 == selectedIndex)
                {
                    position += selectedGap;
                }
            }
        }

        return new Layout(positions, content, IsCompact: true, isScrolling, offset);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = 0.0;
        double height = 0.0;

        foreach (Control child in Children)
        {
            child.Measure(Size.Infinity);
            width += child.DesiredSize.Width;
            height = Math.Max(height, child.DesiredSize.Height);
        }

        if (Children.Count > 1)
        {
            width += Spacing * (Children.Count - 1);
        }

        return new Size(Math.Min(width, availableSize.Width), height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        List<double> widths = new(Children.Count);
        int selectedIndex = -1;

        for (int index = 0; index < Children.Count; index++)
        {
            Control child = Children[index];
            widths.Add(child.DesiredSize.Width);

            if (GetIsSelected(child))
            {
                selectedIndex = index;
            }
        }

        object? selectedItem = selectedIndex >= 0 ? Children[selectedIndex].DataContext : null;
        Layout layout = Compute(widths, selectedIndex, finalSize.Width, Spacing, CompactStep, SelectedGap, CenterShift, ScrollOffset);

        bool isNewSelection = !ReferenceEquals(selectedItem, lastSelectedItem);

        if (isNewSelection)
        {
            isFollowingSelection = true;
        }

        if (layout.IsScrolling && selectedIndex >= 0 && isFollowingSelection)
        {
            // A newly selected card that is out of view gets centred; later it is only nudged back into view.
            double cardLeft = layout.Positions[selectedIndex] + layout.ScrollOffset - SelectedGap;
            double cardRight = layout.Positions[selectedIndex] + layout.ScrollOffset + widths[selectedIndex] + SelectedGap;
            double viewLeft = layout.ScrollOffset;
            double viewRight = layout.ScrollOffset + finalSize.Width;

            if (cardLeft < viewLeft || cardRight > viewRight)
            {
                double offset = isNewSelection ? (cardLeft + cardRight) / 2 - finalSize.Width / 2 :
                    cardLeft < viewLeft ? cardLeft : cardRight - finalSize.Width;
                layout = Compute(widths, selectedIndex, finalSize.Width, Spacing, CompactStep, SelectedGap, CenterShift, offset);
            }
        }

        lastSelectedItem = selectedItem;
        ScrollOffset = layout.ScrollOffset;
        IsScrolling = layout.IsScrolling;

        for (int index = 0; index < Children.Count; index++)
        {
            Control child = Children[index];
            child.Arrange(new Rect(layout.Positions[index], 0.0, widths[index], finalSize.Height));
        }

        Clip = layout.IsScrolling ? new RectangleGeometry(new Rect(0.0, -ClipOverflow, finalSize.Width, finalSize.Height + 2 * ClipOverflow)) : null;

        return finalSize;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);

        if (!IsScrolling)
        {
            return;
        }

        double delta = e.Delta.Y != 0.0 ? e.Delta.Y : e.Delta.X;
        ScrollBy(-delta * WheelCards * CompactStep);
        e.Handled = true;
    }

    internal sealed record Layout(double[] Positions, double ContentWidth, bool IsCompact, bool IsScrolling, double ScrollOffset);
}
