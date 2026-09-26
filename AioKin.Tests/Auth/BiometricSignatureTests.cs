using System.Security.Cryptography;
using AioKin.Services.Auth.Biometric;
using Xunit;

namespace AioKin.Tests.Auth;

public class BiometricSignatureTests
{
    private static (string PublicKeyBase64, ECDsa Key) NewKeyPair()
    {
        var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return (Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()), key);
    }

    private static string Sign(ECDsa key, string nonceBase64)
    {
        var nonceBytes = Convert.FromBase64String(nonceBase64);
        var signature = key.SignData(nonceBytes, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        return Convert.ToBase64String(signature);
    }

    [Fact]
    public void Chu_ky_dung_key_dung_nonce_thi_hop_le()
    {
        var (publicKey, key) = NewKeyPair();
        var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        var signature = Sign(key, nonce);

        Assert.True(BiometricSignature.Verify(publicKey, nonce, signature));
    }

    [Fact]
    public void Chu_ky_boi_key_khac_thi_khong_hop_le()
    {
        var (publicKey, _) = NewKeyPair();
        var (_, otherKey) = NewKeyPair();
        var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        var signature = Sign(otherKey, nonce);

        Assert.False(BiometricSignature.Verify(publicKey, nonce, signature));
    }

    [Fact]
    public void Chu_ky_cho_nonce_khac_thi_khong_hop_le()
    {
        var (publicKey, key) = NewKeyPair();
        var signedNonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var signature = Sign(key, signedNonce);

        var differentNonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        Assert.False(BiometricSignature.Verify(publicKey, differentNonce, signature));
    }

    [Fact]
    public void Chu_ky_bi_sua_mot_byte_thi_khong_hop_le()
    {
        var (publicKey, key) = NewKeyPair();
        var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var signatureBytes = Convert.FromBase64String(Sign(key, nonce));
        signatureBytes[^1] ^= 0xFF; // sua byte cuoi cua chu ky DER
        var tamperedSignature = Convert.ToBase64String(signatureBytes);

        Assert.False(BiometricSignature.Verify(publicKey, nonce, tamperedSignature));
    }

    [Fact]
    public void Key_khong_phai_P256_thi_bi_tu_choi()
    {
        // P7: key hop le ve mat DER (vi du P-384) nhung sai duong cong phai bi tu choi
        using var key384 = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        var publicKey = Convert.ToBase64String(key384.ExportSubjectPublicKeyInfo());
        var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var nonceBytes = Convert.FromBase64String(nonce);
        var signature = Convert.ToBase64String(
            key384.SignData(nonceBytes, HashAlgorithmName.SHA384, DSASignatureFormat.Rfc3279DerSequence));

        Assert.False(BiometricSignature.Verify(publicKey, nonce, signature));
    }

    [Fact]
    public void Key_P521_cung_bi_tu_choi()
    {
        using var key521 = ECDsa.Create(ECCurve.NamedCurves.nistP521);
        var publicKey = Convert.ToBase64String(key521.ExportSubjectPublicKeyInfo());
        var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var nonceBytes = Convert.FromBase64String(nonce);
        var signature = Convert.ToBase64String(
            key521.SignData(nonceBytes, HashAlgorithmName.SHA512, DSASignatureFormat.Rfc3279DerSequence));

        Assert.False(BiometricSignature.Verify(publicKey, nonce, signature));
    }

    [Theory]
    [InlineData("khong-phai-base64!!!", "AAAA", "AAAA")]
    [InlineData(null, "AAAA", "AAAA")]
    public void Input_hong_thi_tra_false_khong_nem_loi(string? publicKey, string nonce, string signature)
    {
        Assert.False(BiometricSignature.Verify(publicKey ?? "", nonce, signature));
    }

    [Fact]
    public void Nonce_khong_phai_base64_thi_tra_false()
    {
        var (publicKey, key) = NewKeyPair();
        var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var signature = Sign(key, nonce);

        Assert.False(BiometricSignature.Verify(publicKey, "khong-phai-base64!!!", signature));
    }

    [Fact]
    public void Signature_khong_phai_base64_thi_tra_false()
    {
        var (publicKey, _) = NewKeyPair();
        var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        Assert.False(BiometricSignature.Verify(publicKey, nonce, "khong-phai-base64!!!"));
    }

    [Fact]
    public void PublicKey_hop_le_base64_nhung_khong_phai_SPKI_thi_tra_false()
    {
        var randomBytes = RandomNumberGenerator.GetBytes(64);
        var publicKey = Convert.ToBase64String(randomBytes);
        var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var signature = Convert.ToBase64String(RandomNumberGenerator.GetBytes(70));

        Assert.False(BiometricSignature.Verify(publicKey, nonce, signature));
    }
}
