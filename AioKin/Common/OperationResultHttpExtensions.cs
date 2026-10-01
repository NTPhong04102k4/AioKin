using AioKin.Models.InputModel.Auth.User;
using Microsoft.AspNetCore.Mvc;

namespace AioKin.Common;

public static class OperationResultHttpExtensions
{
    /// <summary>
    /// Doi <see cref="OperationResult"/> thanh HTTP response. Giu ErrorCode lam nguon su that
    /// duy nhat cho status code, nen controller khong phai tu map tung truong hop.
    /// </summary>
    public static IActionResult ToActionResult(this ControllerBase controller, OperationResult result)
    {
        if (result.Success)
            return controller.Ok(result);

        return controller.StatusCode(MapErrorCodeToStatusCode(result.ErrorCode), result);
    }

    private static int MapErrorCodeToStatusCode(string? errorCode) => errorCode switch
    {
        // 401 Unauthorized
        "Unauthorized" => StatusCodes.Status401Unauthorized,
        "InvalidCredentials" => StatusCodes.Status401Unauthorized,
        "TokenRevoked" => StatusCodes.Status401Unauthorized,
        "InvalidRecoveryCode" => StatusCodes.Status401Unauthorized,
        "InvalidRefreshToken" => StatusCodes.Status401Unauthorized,
        "TokenReuseDetected" => StatusCodes.Status401Unauthorized,
        "SessionExpired" => StatusCodes.Status401Unauthorized,
        "InvalidOtp" => StatusCodes.Status401Unauthorized,
        "InvalidTemporaryPassword" => StatusCodes.Status401Unauthorized,
        "UserInactive" => StatusCodes.Status401Unauthorized,

        // 403 Forbidden
        "Forbidden" => StatusCodes.Status403Forbidden,
        "AccessDenied" => StatusCodes.Status403Forbidden,
        "NotAFamilyMember" => StatusCodes.Status403Forbidden,

        // 404 Not Found
        "NotFound" => StatusCodes.Status404NotFound,
        "UserNotFound" => StatusCodes.Status404NotFound,
        "RegistrationDataNotFound" => StatusCodes.Status404NotFound,

        // 409 Conflict
        "Conflict" => StatusCodes.Status409Conflict,
        "EmailExists" => StatusCodes.Status409Conflict,
        "UsernameExists" => StatusCodes.Status409Conflict,
        "UserNameExists" => StatusCodes.Status409Conflict,
        "PhoneExists" => StatusCodes.Status409Conflict,
        "Duplicate" => StatusCodes.Status409Conflict,
        "SuperAdminAmbiguous" => StatusCodes.Status409Conflict,
        "SuperAdminLimit" => StatusCodes.Status409Conflict,

        // 422 Unprocessable Entity
        "ValidationError" => StatusCodes.Status422UnprocessableEntity,
        "UnprocessableEntity" => StatusCodes.Status422UnprocessableEntity,

        // 429 Too Many Requests
        "TooManyRequests" => StatusCodes.Status429TooManyRequests,

        // 500 / 503 — su co ha tang, khong phai loi nghiep vu
        "InternalError" => StatusCodes.Status500InternalServerError,
        "EmailSendFailed" => StatusCodes.Status503ServiceUnavailable,
        "OtpGenerationFailed" => StatusCodes.Status503ServiceUnavailable,
        // Ruling P13 (progress.md, Task 3): pull khong the phuc vu (retention da het VA khong co
        // blob storage de tao snapshot, hoac blob storage loi luc tao snapshot) — loi ha tang,
        // khong phai loi client.
        "SyncUnavailable" => StatusCodes.Status503ServiceUnavailable,

        // 400 Bad Request — fallback cho loi nghiep vu chua phan loai
        _ => StatusCodes.Status400BadRequest
    };
}
