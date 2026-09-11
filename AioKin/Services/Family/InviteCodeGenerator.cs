using System.Security.Cryptography;

namespace AioKin.Services.Family;

/// <summary>
/// Sinh ma moi vao gia dinh.
///
/// Bang chu cai la Crockford Base32: bo I, L, O, U. Bon ky tu do la nguon go nham khi doc ma
/// qua dien thoai hoac chep lai tu anh chup man hinh — I lan voi 1, O lan voi 0, U lan voi V.
///
/// Dung <see cref="RandomNumberGenerator"/> chu khong phai <see cref="Random"/>: ma moi la
/// mot thong tin xac thuc — ai doan duoc ma la vao duoc nhom. Random gieo tu dong ho, va
/// hai tien trinh khoi dong cung luc co the sinh ra cung day so.
/// </summary>
public static class InviteCodeGenerator
{
    public const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    public const int Length = 10;

    /// <summary>Mot ma moi. 32^10 kha nang — khong gian du rong de khong phai lo va cham.</summary>
    public static string Next()
    {
        Span<char> buffer = stackalloc char[Length];

        for (var i = 0; i < Length; i++)
            buffer[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];

        return new string(buffer);
    }
}
