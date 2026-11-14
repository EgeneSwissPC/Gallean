using System.Collections.Concurrent;
using SkiaSharp;

namespace Gallean.Client.Shaders;

internal static class ShaderLibrary
{
    private static readonly ConcurrentDictionary<ShaderKey, Lazy<SKRuntimeEffect>> Effects = new();

    // Runtime effects are immutable and safe to share, so each shader is compiled once per process instead of once
    // per program instance (every world screen, water rebuild and battle used to compile its own copies).
    // The shared effects must never be disposed, which is why ShaderProgram does not dispose its builder.
    public static SKRuntimeEffect GetEffect(ShaderKey key)
    {
        Lazy<SKRuntimeEffect> effect = Effects.GetOrAdd(key, static key => new Lazy<SKRuntimeEffect>(() => Compile(key)));

        try
        {
            return effect.Value;
        }
        catch
        {
            Effects.TryRemove(new KeyValuePair<ShaderKey, Lazy<SKRuntimeEffect>>(key, effect));
            throw;
        }
    }

    public static SKRuntimeEffect Compile(ShaderKey key)
    {
        SKRuntimeEffect? effect = SKRuntimeEffect.CreateShader(ReadSource(key), out string errors);

        if (effect is null)
        {
            throw new InvalidOperationException($"Failed to compile shader '{key.Name}': {errors}.");
        }

        return effect;
    }

    private static string ReadSource(ShaderKey key)
    {
        Stream? stream = typeof(ShaderLibrary).Assembly.GetManifestResourceStream(key.Resource);

        if (stream is null)
        {
            throw new KeyNotFoundException($"No shader resource named '{key.Resource}'.");
        }

        using StreamReader reader = new(stream);

        return reader.ReadToEnd();
    }
}
