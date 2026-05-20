using System.Text;
using System.Text.Json;

namespace ExpenseTrackerApp.Services;

public class CognitoService
{
    private const string Region = "eu-north-1";
    private const string ClientId = "1t8t6ehkl2m9bnbts69b7njles";
    private const string CognitoUrl = "https://cognito-idp.eu-north-1.amazonaws.com/";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _http = new();
    private string? _pendingSession;
    private string? _pendingUsername;

    public string? PendingUsername => _pendingUsername;

    public async Task<SignInResult> SignInAsync(string email, string password)
    {
        var payload = new
        {
            AuthFlow = "USER_PASSWORD_AUTH",
            ClientId,
            AuthParameters = new Dictionary<string, string>
            {
                ["USERNAME"] = email.Trim(),
                ["PASSWORD"] = password
            }
        };

        var response = await PostCognitoAsync("AWSCognitoIdentityProviderService.InitiateAuth", payload);
        return ParseAuthResponse(response, email.Trim());
    }

    public async Task<SignInResult> CompleteNewPasswordAsync(string email, string newPassword)
    {
        if (string.IsNullOrEmpty(_pendingSession))
            return SignInResult.Fail("Session Cognito manquante. Reconnectez-vous.");

        var payload = new
        {
            ChallengeName = "NEW_PASSWORD_REQUIRED",
            ClientId,
            ChallengeResponses = new Dictionary<string, string>
            {
                ["USERNAME"] = _pendingUsername ?? email.Trim(),
                ["NEW_PASSWORD"] = newPassword
            },
            Session = _pendingSession
        };

        var response = await PostCognitoAsync("AWSCognitoIdentityProviderService.RespondToAuthChallenge", payload);
        _pendingSession = null;
        return ParseAuthResponse(response, email.Trim());
    }

    private async Task<string> PostCognitoAsync(string target, object body)
    {
        var json = JsonSerializer.Serialize(body);
        using var request = new HttpRequestMessage(HttpMethod.Post, CognitoUrl);
        request.Headers.TryAddWithoutValidation("X-Amz-Target", target);
        request.Content = new StringContent(json, Encoding.UTF8, "application/x-amz-json-1.1");

        using var response = await _http.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            var message = ExtractCognitoError(content) ?? $"Erreur Cognito ({(int)response.StatusCode})";
            throw new InvalidOperationException(message);
        }

        return content;
    }

    private SignInResult ParseAuthResponse(string json, string email)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.TryGetProperty("ChallengeName", out var challenge) &&
            challenge.GetString() == "NEW_PASSWORD_REQUIRED")
        {
            _pendingSession = root.GetProperty("Session").GetString();
            if (root.TryGetProperty("ChallengeParameters", out var parameters) &&
                parameters.TryGetProperty("USER_ID_FOR_SRP", out var userIdForSrp))
            {
                _pendingUsername = userIdForSrp.GetString();
            }
            else
            {
                _pendingUsername = email;
            }

            return SignInResult.NewPasswordRequired();
        }

        if (!root.TryGetProperty("AuthenticationResult", out var authResult))
            return SignInResult.Fail("Réponse Cognito invalide.");

        var idToken = authResult.GetProperty("IdToken").GetString();
        if (string.IsNullOrEmpty(idToken))
            return SignInResult.Fail("IdToken manquant.");

        var (userId, userEmail, role) = DecodeJwtClaims(idToken);
        AuthState.Instance.SetSession(idToken, userId, userEmail, role);

        return SignInResult.Success(idToken, role);
    }

    public static (string UserId, string Email, string Role) DecodeJwtClaims(string idToken)
    {
        var parts = idToken.Split('.');
        if (parts.Length < 2)
            return ("", "", "");

        var payloadJson = DecodeBase64Url(parts[1]);
        using var doc = JsonDocument.Parse(payloadJson);
        var root = doc.RootElement;

        var userId = root.TryGetProperty("sub", out var sub) ? sub.GetString() ?? "" : "";
        var email = root.TryGetProperty("email", out var emailEl) ? emailEl.GetString() ?? "" : "";

        var role = "";
        if (root.TryGetProperty("cognito:groups", out var groupsEl))
        {
            var groups = new List<string>();
            if (groupsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var g in groupsEl.EnumerateArray())
                {
                    var value = g.GetString();
                    if (!string.IsNullOrEmpty(value))
                        groups.Add(value);
                }
            }
            else if (groupsEl.ValueKind == JsonValueKind.String)
            {
                var value = groupsEl.GetString();
                if (!string.IsNullOrEmpty(value))
                    groups.Add(value);
            }

            if (groups.Contains("finance"))
                role = "finance";
            else if (groups.Contains("employees"))
                role = "employees";
        }

        return (userId, email, role);
    }

    private static string DecodeBase64Url(string input)
    {
        var padded = input.Replace('-', '+').Replace('_', '/');
        switch (padded.Length % 4)
        {
            case 2: padded += "=="; break;
            case 3: padded += "="; break;
        }
        var bytes = Convert.FromBase64String(padded);
        return Encoding.UTF8.GetString(bytes);
    }

    private static string? ExtractCognitoError(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("message", out var message))
                return message.GetString();
        }
        catch
        {
            // ignore parse errors
        }

        return null;
    }
}

public class SignInResult
{
    public bool IsSuccess { get; init; }
    public bool RequiresNewPassword { get; init; }
    public string? IdToken { get; init; }
    public string? Role { get; init; }
    public string? ErrorMessage { get; init; }

    public static SignInResult Success(string idToken, string role) =>
        new() { IsSuccess = true, IdToken = idToken, Role = role };

    public static SignInResult NewPasswordRequired() =>
        new() { RequiresNewPassword = true };

    public static SignInResult Fail(string message) =>
        new() { ErrorMessage = message };
}
