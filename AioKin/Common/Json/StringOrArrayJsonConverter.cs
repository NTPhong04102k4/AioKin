using System.Text.Json;
using System.Text.Json.Serialization;

namespace AioKin.Common.Json;

/// <summary>
/// Doc mot field JSON co the la chuoi HOAC mang chuoi, tra ve luon duoi dang danh sach.
///
/// CASL cho phep ca hai dang o cung mot field (<c>"read"</c> va <c>["read","update"]</c> deu
/// hop le), va bo rule duoc luu duoi dang JSON tho trong cot <c>roles.permissions</c> — noi
/// ma nguoi ta se sua bang tay. Khai kieu cung thanh <c>string[]</c> se lam ca bo rule nem
/// ngoai le chi vi mot dong viet dang rut gon.
///
/// Luc ghi thi LUON phat ra mang: <c>asStringList()</c> ben app xu ly duoc ca hai, con mang
/// thi khong co gi de doan.
///
/// Phan tu khong phai chuoi bi bo qua thay vi nem — dung dung ky luat cua ben app: rule hong
/// thi bo, khong dung thanh rule rong co the vo tinh khop moi thu.
/// </summary>
public class StringOrArrayJsonConverter : JsonConverter<List<string>>
{
    public override List<string> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
                var single = reader.GetString();
                return string.IsNullOrWhiteSpace(single) ? [] : [single];

            case JsonTokenType.StartArray:
                var values = new List<string>();
                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                {
                    if (reader.TokenType == JsonTokenType.String)
                    {
                        var value = reader.GetString();
                        if (!string.IsNullOrWhiteSpace(value))
                            values.Add(value);
                    }
                    else
                    {
                        // Bo qua phan tu la so/object/mang long nhau, ke ca cay con cua no.
                        reader.Skip();
                    }
                }
                return values;

            case JsonTokenType.Null:
                return [];

            default:
                throw new JsonException(
                    $"Field rule phai la chuoi hoac mang chuoi, nhan duoc {reader.TokenType}.");
        }
    }

    public override void Write(Utf8JsonWriter writer, List<string> value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var item in value)
            writer.WriteStringValue(item);
        writer.WriteEndArray();
    }
}
