namespace Gallean.Client;

// Tracks the atlas textures a content provider created so the ones no longer referenced can be disposed
// deterministically. An atlas can be up to 8192 px wide (up to ~84 MB each); leaving them to the finalizer kept
// their GPU copies in Skia's resource cache, and long sessions exhausted the GPU address space on some drivers.
internal sealed class AtlasTextureTracker
{
    private readonly HashSet<AtlasTexture> textures = new(ReferenceEqualityComparer.Instance);

    public int Count => textures.Count;

    public AtlasTexture Add(AtlasTexture texture)
    {
        textures.Add(texture);

        return texture;
    }

    public void ReleaseUnused(IEnumerable<AtlasTexture> used)
    {
        HashSet<AtlasTexture> live = new(used, ReferenceEqualityComparer.Instance);

        textures.RemoveWhere(texture =>
        {
            if (live.Contains(texture))
            {
                return false;
            }

            texture.Dispose();

            return true;
        });
    }
}
