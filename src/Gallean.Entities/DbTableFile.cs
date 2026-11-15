using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Gallean.Entities;

// Reads and writes whole data tables ({"formatVersion": 1, "rows": [...]}) for any database record type, e.g. for
// scenarios saved by the map editor.
public static class DbTableFile
{
    private static readonly DbJsonSerializerContext Context = CreateContext();

    public static List<T> Read<T>(string path)
    {
        return DbFileReader.Read(path, GetTypeInfo<T>());
    }

    public static void Write<T>(string path, List<T> rows)
    {
        using FileStream stream = File.Create(path);
        using Utf8JsonWriter writer = new(stream, new JsonWriterOptions { Indented = true });

        writer.WriteStartObject();
        writer.WriteNumber("formatVersion", DbFile.CurrentFormatVersion);
        writer.WritePropertyName("rows");
        JsonSerializer.Serialize(writer, rows, GetTypeInfo<T>());
        writer.WriteEndObject();
    }

    private static JsonTypeInfo<List<T>> GetTypeInfo<T>()
    {
        return (JsonTypeInfo<List<T>>?)Context.GetTypeInfo(typeof(List<T>))
               ?? throw new NotSupportedException($"No table serialization for {typeof(T).Name}.");
    }

    private static DbJsonSerializerContext CreateContext()
    {
        JsonSerializerOptions options = new(DbJsonSerializerContext.Default.Options);
        options.Converters.Add(new Db.IdJsonConverterFactory());

        return new DbJsonSerializerContext(options);
    }
}
