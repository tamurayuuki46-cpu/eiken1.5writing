using System.Text.Json;

namespace EikenSpelling.Core.Settings;

/// <summary>JSON 全体を変換する前に、最上位の版の番号だけを読む(新しい版は形が変わっている可能性があるため)。</summary>
internal static class JsonVersionReader
{
    /// <summary>
    /// 版の番号。項目が無ければ null。int に収まらない数は int.MaxValue(新しすぎる版として扱う)。
    /// JSON として読めない・最上位がオブジェクトでない・数でない値なら InvalidDataException。
    /// </summary>
    public static int? Read(string json, string propertyName)
    {
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException("JSON の最上位がオブジェクトではありません。");
            }

            int? found = null;
            foreach (var property in doc.RootElement.EnumerateObject())
            {
                if (!string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (property.Value.ValueKind != JsonValueKind.Number)
                {
                    throw new InvalidDataException($"{propertyName} が数ではありません。");
                }

                var value = ToVersion(property.Value, propertyName);
                if (found is not null && found != value)
                {
                    throw new InvalidDataException($"{propertyName} が食い違って複数あります。");
                }

                found = value;
            }

            return found;
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("JSON を読み込めません。", ex);
        }
    }

    /// <summary>整数だけを版として認める。int を超える正の整数は新しすぎる版(int.MaxValue)、小数・それ以外は不正。</summary>
    private static int ToVersion(JsonElement value, string propertyName)
    {
        if (value.TryGetInt32(out var v))
        {
            return v;
        }

        if (value.TryGetDecimal(out var d) && d == decimal.Truncate(d) && d > int.MaxValue)
        {
            return int.MaxValue;
        }

        throw new InvalidDataException($"{propertyName} が整数ではありません。");
    }
}
