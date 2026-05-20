using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ExpenseTrackerApp.Models;

namespace ExpenseTrackerApp.Services;

public class ApiService
{
    private const string BaseUrl = "https://1opj2xu2n4.execute-api.eu-north-1.amazonaws.com/prod/";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _http;
    private readonly AuthState _auth;

    public ApiService()
    {
        _auth = AuthState.Instance;
        _http = new HttpClient { BaseAddress = new Uri(BaseUrl) };
    }

    public async Task<List<Expense>> GetExpensesAsync()
    {
        using var request = CreateRequest(HttpMethod.Get, "expenses");
        using var response = await _http.SendAsync(request);
        var json = await response.Content.ReadAsStringAsync();
        await EnsureSuccessAsync(response, json);

        var result = JsonSerializer.Deserialize<ExpensesListResponse>(json, JsonOptions);
        return result?.Expenses ?? new List<Expense>();
    }

    public async Task<Expense> CreateExpenseAsync(decimal amount, string category, string description)
    {
        var body = new
        {
            Amount = amount,
            Category = category,
            Description = description
        };

        using var request = CreateRequest(HttpMethod.Post, "expenses");
        request.Content = new StringContent(
            JsonSerializer.Serialize(body),
            Encoding.UTF8,
            "application/json");

        using var response = await _http.SendAsync(request);
        var json = await response.Content.ReadAsStringAsync();
        await EnsureSuccessAsync(response, json);

        var expense = JsonSerializer.Deserialize<Expense>(json, JsonOptions);
        return expense ?? new Expense();
    }

    public async Task SubmitExpenseAsync(string expenseId)
    {
        using var request = CreateRequest(HttpMethod.Post, $"expenses/{expenseId}/submit");
        using var response = await _http.SendAsync(request);
        var json = await response.Content.ReadAsStringAsync();
        await EnsureSuccessAsync(response, json);
    }

    public async Task ApproveExpenseAsync(string expenseId, string justification)
    {
        await PostJustificationAsync($"expenses/{expenseId}/approve", justification);
    }

    public async Task RejectExpenseAsync(string expenseId, string justification)
    {
        await PostJustificationAsync($"expenses/{expenseId}/reject", justification);
    }

    public async Task<string> GetReceiptUrlAsync(string expenseId)
    {
        using var request = CreateRequest(
            HttpMethod.Get,
            $"expenses/{expenseId}/receipt-url?action=download");
        using var response = await _http.SendAsync(request);
        var json = await response.Content.ReadAsStringAsync();
        await EnsureSuccessAsync(response, json);

        var result = JsonSerializer.Deserialize<ReceiptUrlResponse>(json, JsonOptions);
        if (string.IsNullOrEmpty(result?.PresignedUrl))
            throw new InvalidOperationException("URL du reçu introuvable.");

        return result.PresignedUrl;
    }

    private async Task PostJustificationAsync(string path, string justification)
    {
        var body = new { Justification = justification };
        using var request = CreateRequest(HttpMethod.Post, path);
        request.Content = new StringContent(
            JsonSerializer.Serialize(body),
            Encoding.UTF8,
            "application/json");

        using var response = await _http.SendAsync(request);
        var json = await response.Content.ReadAsStringAsync();
        await EnsureSuccessAsync(response, json);
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string relativePath)
    {
        var request = new HttpRequestMessage(method, relativePath);
        if (!string.IsNullOrEmpty(_auth.IdToken))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _auth.IdToken);
        return request;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string json)
    {
        if (response.IsSuccessStatusCode)
            return;

        string? message = null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("message", out var msg))
                message = msg.GetString();
        }
        catch
        {
            // ignore
        }

        throw new InvalidOperationException(
            message ?? $"Erreur API ({(int)response.StatusCode})");
    }

    private class ExpensesListResponse
    {
        public List<Expense> Expenses { get; set; } = new();
        public int Count { get; set; }
    }

    private class ReceiptUrlResponse
    {
        public string PresignedUrl { get; set; } = "";
    }
}
