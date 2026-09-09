using System.Text.Json;
using System.Text.Json.Serialization;
using TimetableAlert.Core.Models;

namespace TimetableAlert.Core;

/// <summary>
/// Source-generated JSON contracts for <see cref="TimetableFile"/>. Using the generator rather
/// than reflection keeps the wire format explicit and the app free of runtime type discovery.
/// </summary>
[JsonSerializable(typeof(TimetableFile))]
public sealed partial class TimetableJsonContext : JsonSerializerContext;

/// <summary>How timetable JSON is read.</summary>
public static class TimetableJson
{
    /// <summary>
    /// Reader settings for timetable files. Deliberately forgiving, because these are written by
    /// hand: property names match whatever the camel-cased name is in any case, comments are
    /// allowed, and so is a trailing comma after the last lesson.
    /// </summary>
    public static JsonSerializerOptions Options { get; } = new()
    {
        TypeInfoResolver = TimetableJsonContext.Default,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };
}

/// <summary>
/// Source-generated JSON contract used to write a timetable back out. Kept separate from the
/// reader so the file the app caches looks like one a person would write: camel-cased, indented,
/// and without a spray of nulls.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(TimetableFile))]
public sealed partial class TimetableWriteJsonContext : JsonSerializerContext;
