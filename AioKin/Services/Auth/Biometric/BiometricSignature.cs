using System.Security.Cryptography;

namespace AioKin.Services.Auth.Biometric;

/// <summary>
/// Verify chu ky ECDSA P-256 cho dang nhap sinh trac. Dinh dang DER
/// (Rfc3279DerSequence) — Android SHA256withECDSA va iOS SecKeyCreateSignature deu xuat
/// dinh dang nay mac dinh, khac IEEE P1363 la mac dinh cua .NET.
///
/// Khong log bat cu gi trong lop nay — khong logger, khong Console — vi tham so
/// dau vao la key/nonce/chu ky, khong duoc de lot ra ngoai duoi bat ky hinh thuc nao.
/// </summary>
public static class BiometricSignature
{
    // OID chinh thuc cua duong cong P-256 (secp256r1 / prime256v1 / nistP256).
    private const string P256Oid = "1.2.840.10045.3.1.7";

    /// <summary>
    /// Verify chu ky. Khong bao gio nem loi ra ngoai — moi dau vao hong (base64 sai,
    /// key sai dinh dang, sai duong cong, chu ky sai) deu tra ve false. Bat buoc de
    /// AuthController/BiometricAuthService co the anh xa moi nhanh that bai thanh cung
    /// mot loi generic ma khong can try/catch rieng.
    /// </summary>
    public static bool Verify(string publicKeyBase64, string nonceBase64, string signatureBase64)
    {
        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKeyBase64), out _);

            if (!IsValidP256PublicKey(ecdsa))
            {
                return false;
            }

            var nonceBytes = Convert.FromBase64String(nonceBase64);
            var signatureBytes = Convert.FromBase64String(signatureBase64);

            return ecdsa.VerifyData(nonceBytes, signatureBytes, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException or ArgumentException or ArgumentNullException)
        {
            // Du lieu tu client khong dang tin: base64 hong, key sai dinh dang/duong cong,
            // chu ky sai do dai — tat ca deu la "khong hop le", khong phai loi he thong.
            return false;
        }
    }

    /// <summary>
    /// P7: key import thanh cong tu SubjectPublicKeyInfo khong co nghia no la P-256 —
    /// ImportSubjectPublicKeyInfo chap nhan bat ky duong cong EC nao ma no tim thay trong
    /// DER (P-384, P-521, ...). Phai kiem tra rieng OID/ten duong cong sau khi import,
    /// khong thi mot key P-384 hop le van co the "verify" thanh cong voi hash sai muc dich.
    /// </summary>
    private static bool IsValidP256PublicKey(ECDsa ecdsa)
    {
        try
        {
            var parameters = ecdsa.ExportParameters(false);
            var curve = parameters.Curve;

            if (curve.Oid?.Value == P256Oid)
            {
                return true;
            }

            return string.Equals(curve.Oid?.FriendlyName, nameof(ECCurve.NamedCurves.nistP256), StringComparison.OrdinalIgnoreCase)
                || string.Equals(curve.Oid?.FriendlyName, "prime256v1", StringComparison.OrdinalIgnoreCase)
                || string.Equals(curve.Oid?.FriendlyName, "secp256r1", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException or PlatformNotSupportedException or NotSupportedException)
        {
            // Khong doc duoc tham so duong cong => coi nhu khong phai P-256 hop le.
            return false;
        }
    }
}
