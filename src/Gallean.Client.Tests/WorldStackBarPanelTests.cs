using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;

namespace Gallean.Client.Tests;

[NonParallelizable]
public sealed class WorldStackBarPanelTests
{
    private static double[] Cards(int count, int selectedIndex = -1)
    {
        double[] widths = new double[count];

        for (int index = 0; index < count; index++)
        {
            widths[index] = index == selectedIndex ? 156.0 : 130.0;
        }

        return widths;
    }

    private static WorldStackBarPanel.Layout Compute(double[] widths, int selectedIndex, double available, double scrollOffset = 0.0)
    {
        return WorldStackBarPanel.Compute(widths, selectedIndex, available, spacing: -40.0, compactStep: 48.0, selectedGap: 24.0, centerShift: 98.0, scrollOffset);
    }

    [Test]
    public void FitIsCentredOnTheWindowTest()
    {
        // 3 cards = 130 * 3 - 80 = 310 wide; window 1366 = 1170 available + 196 reserved on the right.
        WorldStackBarPanel.Layout layout = Compute(Cards(3), -1, 1170.0);

        Assert.That(layout.IsCompact, Is.False);
        Assert.That(layout.IsScrolling, Is.False);
        Assert.That(layout.Positions, Is.EqualTo(new[] { 528.0, 618.0, 708.0 }));
        Assert.That(layout.Positions[0] + layout.ContentWidth / 2, Is.EqualTo(1366.0 / 2));
    }

    [Test]
    public void FitClampsToTheLeftEdgeTest()
    {
        // 12 cards = 1120 wide: centring on the window would push them past the reserved right side.
        WorldStackBarPanel.Layout layout = Compute(Cards(12), -1, 1170.0);

        Assert.That(layout.IsCompact, Is.False);
        Assert.That(layout.Positions[0], Is.EqualTo(50.0));
        Assert.That(layout.Positions[11] + 130.0, Is.EqualTo(1170.0));
    }

    [Test]
    public void CompactSpreadsTheSelectedCardTest()
    {
        double[] widths = Cards(16, selectedIndex: 5);
        WorldStackBarPanel.Layout layout = Compute(widths, 5, 1170.0);

        Assert.That(layout.IsCompact, Is.True);
        Assert.That(layout.IsScrolling, Is.False);
        Assert.That(layout.ContentWidth, Is.EqualTo(1170.0).Within(1e-9));

        double step = layout.Positions[1] - layout.Positions[0];

        Assert.That(step, Is.LessThan(90.0));
        Assert.That(step, Is.GreaterThanOrEqualTo(48.0));
        // The card before the selected one keeps its slice, then a gap; the selected card's art shows whole, then a gap.
        Assert.That(layout.Positions[5] - layout.Positions[4], Is.EqualTo(step + 24.0).Within(1e-9));
        Assert.That(layout.Positions[6] - layout.Positions[5], Is.EqualTo(156.0 - 40.0 + 24.0).Within(1e-9));
        Assert.That(layout.Positions[7] - layout.Positions[6], Is.EqualTo(step).Within(1e-9));
        Assert.That(layout.Positions[15] + 130.0, Is.EqualTo(1170.0).Within(1e-9));
    }

    [Test]
    public void CompactWithSelectedLastCardTest()
    {
        double[] widths = Cards(16, selectedIndex: 15);
        WorldStackBarPanel.Layout layout = Compute(widths, 15, 1170.0);

        Assert.That(layout.IsScrolling, Is.False);
        Assert.That(layout.Positions[15] + 156.0, Is.EqualTo(1170.0).Within(1e-9));
        Assert.That(layout.Positions[15] - layout.Positions[14], Is.EqualTo(layout.Positions[1] - layout.Positions[0] + 24.0).Within(1e-9));
    }

    [Test]
    public void ScrollsWhenTheCompactStepIsReachedTest()
    {
        double[] widths = Cards(55, selectedIndex: 0);
        WorldStackBarPanel.Layout layout = Compute(widths, 0, 1170.0, scrollOffset: 100.0);

        // 156 - 40 + 24 + 53 * 48 + 130 = 2814.
        Assert.That(layout.IsScrolling, Is.True);
        Assert.That(layout.ContentWidth, Is.EqualTo(2814.0));
        Assert.That(layout.ScrollOffset, Is.EqualTo(100.0));
        Assert.That(layout.Positions[0], Is.EqualTo(-100.0));
        Assert.That(layout.Positions[2] - layout.Positions[1], Is.EqualTo(48.0));
    }

    [Test]
    public void ScrollOffsetIsClampedTest()
    {
        double[] widths = Cards(55, selectedIndex: 0);

        Assert.That(Compute(widths, 0, 1170.0, scrollOffset: -50.0).ScrollOffset, Is.EqualTo(0.0));
        Assert.That(Compute(widths, 0, 1170.0, scrollOffset: 1e6).ScrollOffset, Is.EqualTo(2814.0 - 1170.0));
    }

    [AvaloniaTest]
    public void PanelBringsANewlySelectedCardIntoViewTest()
    {
        WorldStackBarPanel panel = new() { Spacing = -40.0, CompactStep = 48.0, SelectedGap = 24.0 };
        List<Border> cards = [];

        for (int index = 0; index < 55; index++)
        {
            Border card = new() { Width = 130.0, Height = 170.0, DataContext = index };
            cards.Add(card);
            panel.Children.Add(card);
        }

        WorldStackBarPanel.SetIsSelected(cards[0], true);
        panel.Measure(new Size(1170.0, 300.0));
        panel.Arrange(new Rect(0.0, 0.0, 1170.0, 300.0));

        Assert.That(panel.IsScrolling, Is.True);
        Assert.That(panel.ScrollOffset, Is.EqualTo(0.0));
        Assert.That(panel.Clip, Is.Not.Null);

        WorldStackBarPanel.SetIsSelected(cards[0], false);
        WorldStackBarPanel.SetIsSelected(cards[45], true);
        panel.Measure(new Size(1170.0, 300.0));
        panel.Arrange(new Rect(0.0, 0.0, 1170.0, 300.0));

        Rect selected = cards[45].Bounds;

        Assert.That(selected.X, Is.GreaterThanOrEqualTo(24.0));
        Assert.That(selected.Right, Is.LessThanOrEqualTo(1170.0 - 24.0));

        // The wheel moves the bar away; it stays there until the selection changes.
        panel.ScrollBy(-400.0);
        panel.Arrange(new Rect(0.0, 0.0, 1170.0, 300.0));

        Assert.That(cards[45].Bounds.X, Is.EqualTo(selected.X + 400.0).Within(1e-9));
    }

    [AvaloniaTest]
    public void PanelWithoutOverflowIsNotClippedTest()
    {
        WorldStackBarPanel panel = new() { Spacing = -40.0 };

        for (int index = 0; index < 3; index++)
        {
            panel.Children.Add(new Border { Width = 130.0, Height = 170.0 });
        }

        panel.Measure(new Size(1170.0, 300.0));
        panel.Arrange(new Rect(0.0, 0.0, 1170.0, 300.0));

        Assert.That(panel.IsScrolling, Is.False);
        Assert.That(panel.Clip, Is.Null);
        Assert.That(panel.DesiredSize.Width, Is.EqualTo(310.0));
    }
}
