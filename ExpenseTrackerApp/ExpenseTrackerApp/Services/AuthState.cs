namespace ExpenseTrackerApp.Services;

public sealed class AuthState
{
    private static readonly Lazy<AuthState> _instance = new(() => new AuthState());
    public static AuthState Instance => _instance.Value;

    public string? IdToken { get; private set; }
    public string? UserId { get; private set; }
    public string? UserEmail { get; private set; }
    public string? Role { get; private set; }

    public bool IsAuthenticated => !string.IsNullOrEmpty(IdToken);

    public void SetSession(string idToken, string userId, string userEmail, string role)
    {
        IdToken = idToken;
        UserId = userId;
        UserEmail = userEmail;
        Role = role;
    }

    public void Clear()
    {
        IdToken = null;
        UserId = null;
        UserEmail = null;
        Role = null;
    }
}
