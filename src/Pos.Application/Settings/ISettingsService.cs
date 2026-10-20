namespace Pos.Application.Settings;

/// <summary>
/// The shop details that appear on receipts and reports.
/// </summary>
public sealed class ShopProfile
{
    public string ShopName { get; set; } = "";
    public string Address { get; set; } = "";
    public string Phone { get; set; } = "";
    public string KraPin { get; set; } = "";
    public string ReceiptFooter { get; set; } = "Thank you for your business!";
}

public interface ISettingsService
{
    Task<ShopProfile> GetShopProfileAsync(CancellationToken ct = default);
    Task SaveShopProfileAsync(ShopProfile profile, CancellationToken ct = default);
}
