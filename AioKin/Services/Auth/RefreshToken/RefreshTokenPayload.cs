namespace AioKin.Services.Auth.RefreshToken;

/// <summary>
/// Du lieu luu kem refresh token trong Redis, duoi khoa sha256(token).
///
/// TokenFamilyId: dat ten "TokenFamilyId" chu khong phai "FamilyId" de tranh trung voi khai
/// niem Family (ho gia dinh) da co san o RedisKeys.FamilyMembership — hai thu khong lien quan
/// nhau. Moi lan dang nhap moi (khong phai rotate) sinh mot TokenFamilyId rieng; xoay vong giu
/// nguyen gia tri nay xuyen suot ca chuoi token cua cung mot phien thiet bi.
///
/// AbsoluteExpiresAt: tran tren tuyet doi cua CA CHUOI, dat mot lan luc dang nhap va khong doi
/// qua moi lan rotate — khac voi TTL sliding (RefreshTokenExpiryDays) duoc gia han moi lan.
///
/// IsRevoked: token da bi xoay vong qua (tombstoned) nhung CHUA bi xoa khoi Redis — giu lai
/// trong mot khoang ngan (xem RedisTtl.RefreshTokenTombstone) de ValidateAsync van tra ve duoc
/// payload nay, cho phep phat hien replay (tai su dung) cua mot token da bi thay the.
/// </summary>
public sealed record RefreshTokenPayload(
    string Subject,
    string Role,
    string? DeviceId,
    string? DeviceName,
    string? Platform,
    string TokenFamilyId,
    DateTime AbsoluteExpiresAt,
    bool IsRevoked = false);
