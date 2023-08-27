using System.Text.Json.Serialization;

namespace ConvA;

public class Config {
    public string? RepositoryPath { get; set; }
    public ConversionType? ConversionType { get; set; }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(Config))]
[JsonSerializable(typeof(ConversionType))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(int))]
internal partial class SourceGenerationContext : JsonSerializerContext
{
}