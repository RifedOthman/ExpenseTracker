namespace ExpenseTrackerApp.Models;

public class Expense
{
    public string ExpenseId { get; set; } = "";
    public string UserId { get; set; } = "";
    public string UserEmail { get; set; } = "";
    public string Status { get; set; } = "";
    public decimal Amount { get; set; }
    public string Category { get; set; } = "";
    public string Description { get; set; } = "";
    public string CreatedAt { get; set; } = "";
    public string UpdatedAt { get; set; } = "";
    public string Justification { get; set; } = "";
    public string ReceiptKey { get; set; } = "";
}
