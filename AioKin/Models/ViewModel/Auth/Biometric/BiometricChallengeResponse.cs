namespace AioKin.Models.ViewModel.Auth.Biometric;

public class BiometricChallengeResponse
{
    public string ChallengeId { get; set; } = string.Empty;
    public string Nonce { get; set; } = string.Empty;
}
