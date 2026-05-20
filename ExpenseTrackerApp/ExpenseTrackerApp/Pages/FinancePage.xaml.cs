using ExpenseTrackerApp.Models;
using ExpenseTrackerApp.Services;

namespace ExpenseTrackerApp.Pages;

public partial class FinancePage : ContentPage
{
    private readonly ApiService _apiService = new();
    private readonly AuthState _auth = AuthState.Instance;

    public FinancePage()
    {
        InitializeComponent();
        WelcomeLabel.Text = $"Finance — {_auth.UserEmail}";
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (!_auth.IsAuthenticated)
        {
            await Shell.Current.GoToAsync("//login");
            return;
        }

        await LoadExpensesAsync();
    }

    private async Task LoadExpensesAsync()
    {
        try
        {
            SetLoading(true);
            var expenses = await _apiService.GetExpensesAsync();
            ExpensesCollection.ItemsSource = expenses
                .Select(e => new FinanceExpenseView(e))
                .ToList();
        }
        catch (Exception ex)
        {
            await DisplayAlert("Erreur", ex.Message, "OK");
        }
        finally
        {
            SetLoading(false);
            ExpensesRefresh.IsRefreshing = false;
        }
    }

    private async void OnApproveClicked(object sender, EventArgs e)
    {
        if (sender is not Button button || button.CommandParameter is not string expenseId)
            return;

        var justification = await DisplayPromptAsync(
            "Approuver",
            "Justification :",
            placeholder: "Ex. Approved - valid receipt");

        if (string.IsNullOrWhiteSpace(justification))
            return;

        await ProcessActionAsync(
            () => _apiService.ApproveExpenseAsync(expenseId, justification),
            "Dépense approuvée.");
    }

    private async void OnRejectClicked(object sender, EventArgs e)
    {
        if (sender is not Button button || button.CommandParameter is not string expenseId)
            return;

        var justification = await DisplayPromptAsync(
            "Rejeter",
            "Justification :",
            placeholder: "Ex. Missing receipt");

        if (string.IsNullOrWhiteSpace(justification))
            return;

        await ProcessActionAsync(
            () => _apiService.RejectExpenseAsync(expenseId, justification),
            "Dépense rejetée.");
    }

    private async Task ProcessActionAsync(Func<Task> action, string successMessage)
    {
        try
        {
            SetLoading(true);
            await action();
            await LoadExpensesAsync();
            await DisplayAlert("Succès", successMessage, "OK");
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

    private async void OnRefreshing(object? sender, EventArgs e)
    {
        await LoadExpensesAsync();
    }

    private async void OnLogoutClicked(object sender, EventArgs e)
    {
        _auth.Clear();
        await Shell.Current.GoToAsync("//login");
    }

    private void SetLoading(bool isLoading)
    {
        LoadingIndicator.IsRunning = isLoading;
        LoadingIndicator.IsVisible = isLoading;
    }

    private sealed class FinanceExpenseView
    {
        public FinanceExpenseView(Expense expense)
        {
            ExpenseId = expense.ExpenseId;
            Category = expense.Category;
            Description = expense.Description;
            UserEmail = expense.UserEmail;
            AmountDisplay = $"{expense.Amount:N2} € — {expense.Category}";
        }

        public string ExpenseId { get; }
        public string AmountDisplay { get; }
        public string Category { get; }
        public string Description { get; }
        public string UserEmail { get; }
    }
}
