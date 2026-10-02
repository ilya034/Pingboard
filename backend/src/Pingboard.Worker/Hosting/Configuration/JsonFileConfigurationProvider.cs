using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace Pingboard.Worker.Hosting.Configuration;

/// <summary>Читает JSON-файл и раскладывает его во плоские ключи <c>Section:Key</c>.</summary>
public sealed class JsonFileConfigurationProvider(string path) : ConfigurationProvider
{
    public string Path { get; } = path;

    public override void Load()
    {
        var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        if (File.Exists(Path))
        {
            using var stream = File.OpenRead(Path);

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(stream, new JsonDocumentOptions
                {
                    CommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true
                });
            }
            catch (JsonException ex)
            {
                // Пустой или битый файл — это ошибка конфигурации, и её текст должен
                // называть файл: иначе старт падает с голым «JsonReaderException».
                throw new InvalidOperationException(
                    $"Не удалось прочитать конфигурацию {Path}: {ex.Message}", ex);
            }

            using (document)
            {
                Flatten(document.RootElement, null, data);
            }
        }

        Data = data;
    }

    private static void Flatten(JsonElement element, string? prefix, Dictionary<string, string?> data)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                    Flatten(property.Value, prefix is null ? property.Name : $"{prefix}:{property.Name}", data);

                break;

            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    Flatten(item, $"{prefix}:{index}", data);
                    index++;
                }

                break;

            case JsonValueKind.Null:
                if (prefix is not null) data[prefix] = null;
                break;

            default:
                // Корень-скаляр («config.json» со строкой внутри) — не конфигурация:
                // раньше здесь был NullReferenceException на prefix, потому что ключа нет.
                if (prefix is not null) data[prefix] = element.ToString();
                break;
        }
    }
}
