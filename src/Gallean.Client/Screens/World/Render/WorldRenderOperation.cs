using System.Diagnostics;
using Avalonia;
using Avalonia.Media;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using SkiaSharp;

namespace Gallean.Client;

internal sealed class WorldRenderOperation(
    Rect bounds,
    Matrix transform,
    WorldRenderState state,
    WorldRenderContent content,
    WorldRenderer renderer,
    IAssetProvider assets,
    TimeSpan loopTime,
    TimeSpan animationTime,
    TimeSpan terrainExpansionTime,
    float renderResolution,
    bool hitboxOverlay,
    DiagnosticsRecorder diagnostics,
    IDisposable? contentLease = null) : ICustomDrawOperation
{
    private readonly SKMatrix skiaTransform = transform.ToSKMatrix();

    public Rect Bounds => bounds;

    public bool HitTest(Point point)
    {
        return bounds.Contains(point);
    }

    public bool Equals(ICustomDrawOperation? other)
    {
        return ReferenceEquals(this, other);
    }

    public void Dispose()
    {
        // Avalonia disposes a draw operation once its render data has been replaced, so the content (and its atlas
        // textures) is no longer drawn after this point.
        contentLease?.Dispose();
    }

    public void Render(ImmediateDrawingContext context)
    {
        using ISkiaSharpApiLease lease = context.GetSkiaApiLease();

        SKCanvas canvas = lease.SkCanvas;

        canvas.Save();
        canvas.Concat(in skiaTransform);

        bool measure = diagnostics.IsEnabled;
        long renderStartTimestamp = measure ? Stopwatch.GetTimestamp() : 0;

        renderer.Render(canvas, content, state, assets, loopTime, animationTime, terrainExpansionTime, renderResolution, hitboxOverlay);

        canvas.Restore();

        if (measure)
        {
            diagnostics.AddDraw(Stopwatch.GetElapsedTime(renderStartTimestamp));
        }
    }
}
