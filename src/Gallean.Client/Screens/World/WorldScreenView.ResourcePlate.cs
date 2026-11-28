using Avalonia;
using Avalonia.Media;

namespace Gallean.Client;

// Gallean Upgrade (docs/cities-overview.md): with the fourth top-left button (Cities), the centred resource plate would
// run into the buttons once it shows three or more resources (Russian labels) or four (English). Slide the plate to the
// right just enough to keep a gap; it stays centred whenever there is room. A render transform, so layout never loops.
internal sealed partial class WorldScreenView
{
    private const double ResourcePlateGap = 12;

    private double resourcePlateShift;

    private void KeepResourcePlateClearOfNav(object? sender, EventArgs e)
    {
        Point? navOrigin = Nav.TranslatePoint(default, this);
        Point? centerOrigin = TopCenter.TranslatePoint(default, this);

        if (navOrigin is null || centerOrigin is null)
        {
            return;
        }

        double navRight = navOrigin.Value.X + Nav.Bounds.Width;
        double plateLeft = centerOrigin.Value.X + ResourcePlate.Bounds.X;
        double shift = Math.Max(0, Math.Ceiling(navRight + ResourcePlateGap - plateLeft));

        if (shift != resourcePlateShift)
        {
            resourcePlateShift = shift;
            ResourcePlate.RenderTransform = shift > 0 ? new TranslateTransform(shift, 0) : null;
        }
    }
}
