using ExpenseTrackerApp.Services;

namespace ExpenseTrackerApp.Pages;

public partial class LoginPage : ContentPage
{
    private readonly CognitoService _cognitoService = new();
    private bool _newPasswordRequired;

    public LoginPage()
    {
        InitializeComponent();
    }

    private async void OnLoginClicked(object sender, EventArgs e)
    {
        var email = EmailEntry.Text?.Trim();
        var password = PasswordEntry.Text;

        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
        {
            await DisplayAlert("Erreur", "Email et mot de passe requis.", "OK");
            return;
        }

        try
        {
            SetLoading(true);

            SignInResult result;
            if (_newPasswordRequired)
            {
                var newPassword = NewPasswordEntry.Text;
                if (string.IsNullOrEmpty(newPassword))
                {
                    await DisplayAlert("Erreur", "Saisissez le nouveau mot de passe.", "OK");
                    return;
                }

                result = await _cognitoService.CompleteNewPasswordAsync(email, newPassword);
            }
            else
            {
                result = await _cognitoService.SignInAsync(email, password);
            }

            if (result.RequiresNewPassword)
            {
                _newPasswordRequired = true;
                NewPasswordEntry.IsVisible = true;
                PasswordEntry.IsVisible = false;
                LoginButton.Text = "Définir le mot de passe";
                await DisplayAlert(
                    "Nouveau mot de passe",
                    "Cognito exige un nouveau mot de passe pour ce compte.",
                    "OK");
                return;
            }

            if (!result.IsSuccess)
            {
                await DisplayAlert("Erreur", result.ErrorMessage ?? "Connexion échouée.", "OK");
                return;
            }

            await NavigateByRoleAsync(result.Role ?? AuthState.Instance.Role ?? "");
        }
        catch (Exception ex)
        {
            await DisplayAlert("Erreur", ex.Message, "OK");
        }
        finally
        {
            SetLoading(false);
        }
    }

    private static async Task NavigateByRoleAsync(string role)
    {
        if (role == "finance")
            await Shell.Current.GoToAsync("//finance");
        else if (role == "employees")
            await Shell.Current.GoToAsync("//employee");
        else
            await Shell.Current.DisplayAlert(
                "Accès refusé",
                "Aucun groupe Cognito reconnu (employees ou finance).",
                "OK");
    }

    private void SetLoading(bool isLoading)
    {
        LoadingIndicator.IsRunning = isLoading;
        LoadingIndicator.IsVisible = isLoading;
        LoginButton.IsEnabled = !isLoading;
    }
}
