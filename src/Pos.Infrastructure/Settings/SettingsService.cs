using Dapper;
using Microsoft.Data.Sqlite;
using Pos.Application.Settings;
using Pos.Infrastructure.Data;

namespace Pos.Infrastructure.Settings;

public sealed class SettingsService : ISettingsService
{
    private const string KeyShopName = "shop_name";
    private const string KeyShopAddress = "shop_address";
    private const string KeyShopPhone = "shop_phone";
    private const string KeyShopKraPin = "shop_kra_pin";
    private const string KeyReceiptFooter = "receipt_footer";

    private readonly DbConnectionFactory _factory;

    public SettingsService(DbConnectionFactory factory) => _factory = factory;

    public async Task<ShopProfile> GetShopProfileAsync(CancellationToken ct = default)
    {
        using var connection = _factory.CreateOpen();
        var rows = await connection.QueryAsync<(string Key, string Value)>(new CommandDefinition(
            "SELECT key, value FROM settings WHERE key IN ($k1, $k2, $k3, $k4, $k5)",
            new
            {
                k1 = KeyShopName,
                k2 = KeyShopAddress,
                k3 = KeyShopPhone,
                k4 = KeyShopKraPin,
                k5 = KeyReceiptFooter,
            },
            cancellationToken: ct));

        var dict = rows.ToDictionary(r => r.Key, r => r.Value);

        return new ShopProfile
        {
            ShopName = dict.GetValueOrDefault(KeyShopName, ""),
            Address = dict.GetValueOrDefault(KeyShopAddress, ""),
            Phone = dict.GetValueOrDefault(KeyShopPhone, ""),
            KraPin = dict.GetValueOrDefault(KeyShopKraPin, ""),
            ReceiptFooter = dict.GetValueOrDefault(KeyReceiptFooter, "Thank you for your business!"),
        };
    }

    public async Task SaveShopProfileAsync(ShopProfile profile, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        using SqliteConnection connection = _factory.CreateOpen();
        using SqliteTransaction tx = connection.BeginTransaction();

        try
        {
            await UpsertAsync(connection, tx, KeyShopName, profile.ShopName ?? "", ct);
            await UpsertAsync(connection, tx, KeyShopAddress, profile.Address ?? "", ct);
            await UpsertAsync(connection, tx, KeyShopPhone, profile.Phone ?? "", ct);
            await UpsertAsync(connection, tx, KeyShopKraPin, profile.KraPin ?? "", ct);
            await UpsertAsync(connection, tx, KeyReceiptFooter, profile.ReceiptFooter ?? "", ct);

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    private static async Task UpsertAsync(
        SqliteConnection c, SqliteTransaction tx, string key, string value, CancellationToken ct)
    {
        await c.ExecuteAsync(new CommandDefinition(@"
            INSERT INTO settings(key, value) VALUES ($key, $value)
            ON CONFLICT(key) DO UPDATE SET
                value = excluded.value,
                updated_at = strftime('%Y-%m-%dT%H:%M:%SZ','now')",
            new { key, value },
            tx, cancellationToken: ct));
    }
}
