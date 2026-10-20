namespace Pos.Domain.Expenses;

public sealed class Expense
{
    public int Id { get; set; }

    /// <summary>Free-text category. Examples: "Rent", "Salary", "Transport", "Utilities".</summary>
    public string Category { get; set; } = "";

    /// <summary>What the money was spent on. Optional but recommended.</summary>
    public string? Description { get; set; }

    /// <summary>Always positive. Expenses are outflows; sign is contextual.</summary>
    public long AmountCents { get; set; }

    public string BusinessDate { get; set; } = "";
    public int? UserId { get; set; }
    public string? Notes { get; set; }
    public string? CreatedAt { get; set; }

    public Money Amount => new(AmountCents);
}
