using ExpenseTrackerApp.Models;
using ExpenseTrackerApp.Services;

namespace ExpenseTrackerApp.Pages;

public partial class EmployeePage : ContentPage
{
    private readonly ApiService _apiService = new();
    private readonly AuthState _auth = AuthState.Instance;

    private static readonly string[] Categories =
    {
        "Transport", "Repas", "Hébergement", "Equipement", "Autre"
    };

    private static readonly FilePickerFileType ReceiptImageTypes = new(
        new Dictionary<DevicePlatform, IEnumerable<string>>
        {
            { DevicePlatform.WinUI, new[] { ".jpg", ".jpeg", ".png" } },
            { DevicePlatform.Android, new[] { "image/jpeg", "image/png" } },
            { DevicePlatform.iOS, new[] { "public.jpeg", "public.png" } },
            { DevicePlatform.MacCatalyst, new[] { "public.jpeg", "public.png" } }
        });

    public EmployeePage()
    {
        InitializeComponent();
        CategoryPicker.ItemsSource = Categories;
        WelcomeLabel.Text = $"Connecté : {_auth.UserEmail}";
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
                .Select(e => new ExpenseItemView(e))
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

    private void OnNewExpenseClicked(object sender, EventArgs e)
    {
        FormPanel.IsVisible = true;
        AmountEntry.Text = "";
        DescriptionEntry.Text = "";
        CategoryPicker.SelectedIndex = 0;
    }

    private void OnCancelFormClicked(object sender, EventArgs e)
    {
        FormPanel.IsVisible = false;
    }

    private async void OnCreateExpenseClicked(object sender, EventArgs e)
    {
        if (!decimal.TryParse(AmountEntry.Text, out var amount) || amount <= 0)
        {
            await DisplayAlert("Erreur", "Montant invalide.", "OK");
            return;
        }

        var category = CategoryPicker.SelectedItem as string;
        if (string.IsNullOrEmpty(category))
        {
            await DisplayAlert("Erreur", "Choisissez une catégorie.", "OK");
            return;
        }

        try
        {
            SetLoading(true);
            await _apiService.CreateExpenseAsync(amount, category, DescriptionEntry.Text ?? "");
            FormPanel.IsVisible = false;
            await LoadExpensesAsync();
            await DisplayAlert("Succès", "Dépense créée.", "OK");
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

    private async void OnSubmitClicked(object sender, EventArgs e)
    {
        if (sender is not Button button || button.CommandParameter is not string expenseId)
            return;

        try
        {
            SetLoading(true);
            await _apiService.SubmitExpenseAsync(expenseId);
            await LoadExpensesAsync();
            await DisplayAlert("Succès", "Dépense soumise.", "OK");
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

    private async void OnAttachReceiptClicked(object sender, EventArgs e)
    {
        if (sender is not Button button || button.CommandParameter is not string expenseId)
            return;

        try
        {
            var file = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Choisir un reçu (JPG ou PNG)",
                FileTypes = ReceiptImageTypes
            });

            if (file == null)
                return;

            SetLoading(true);

            var presignedUrl = await _apiService.GetReceiptUploadUrlAsync(expenseId);
            await using var stream = await file.OpenReadAsync();
            await _apiService.UploadReceiptToS3Async(presignedUrl, stream);

            await LoadExpensesAsync();
            await DisplayAlert("Succès", "Reçu uploadé avec succès !", "OK");
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

    private async void OnViewReceiptClicked(object sender, EventArgs e)
    {
        if (sender is not Button button || button.CommandParameter is not string expenseId)
            return;

        try
        {
            SetLoading(true);
            var url = await _apiService.GetReceiptUrlAsync(expenseId);
            await Launcher.Default.OpenAsync(new Uri(url));
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

    private sealed class ExpenseItemView
    {
        public ExpenseItemView(Expense expense)
        {
            ExpenseId = expense.ExpenseId;
            Category = expense.Category;
            Description = expense.Description;
            CreatedAt = expense.CreatedAt;
            AmountDisplay = $"{expense.Amount:N2} €";
            StatusDisplay = expense.Status;
            StatusColor = GetStatusColor(expense.Status);
            CanSubmit = expense.Status is "DRAFT" or "REJECTED";
            CanAttachReceipt = expense.Status == "DRAFT";
            HasReceipt = !string.IsNullOrEmpty(expense.ReceiptKey);
        }

        public string ExpenseId { get; }
        public string AmountDisplay { get; }
        public string Category { get; }
        public string Description { get; }
        public string CreatedAt { get; }
        public string StatusDisplay { get; }
        public Color StatusColor { get; }
        public bool CanSubmit { get; }
        public bool CanAttachReceipt { get; }
        public bool HasReceipt { get; }

        private static Color GetStatusColor(string status) => status switch
        {
            "DRAFT" => Colors.Gray,
            "SUBMITTED" => Colors.DodgerBlue,
            "APPROVED" => Colors.Green,
            "REJECTED" => Colors.Red,
            _ => Colors.Black
        };
    }
}
