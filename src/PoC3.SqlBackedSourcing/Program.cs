using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Dapper;
using System.Text.Json;

namespace SyncSourcing.PoC3.SqlBackedSourcing;

// --- INFRASTRUCTURE: GUID HANDLER ---
public class GuidTypeHandler : SqlMapper.TypeHandler<Guid>
{
    public override void SetValue(IDbDataParameter parameter, Guid value) 
        => parameter.Value = value.ToString().ToUpper();

    public override Guid Parse(object value) 
        => Guid.Parse(value.ToString() ?? Guid.Empty.ToString());
}

// --- DOMAIN LAYER ---
public record EventMetadata(Guid CorrelationId, string? UserId, int ExpectedVersion);

public record ShoppingCart(Guid Id, int Version, bool IsCancelled, decimal Total)
{
    public ShoppingCart() : this(Guid.Empty, 0, false, 0) { }
}

public record CommandResult(bool Success, string Message);

// --- INFRASTRUCTURE LAYER: THE SQL ENGINE ---
public class SqlEventStore
{
    private readonly string _connectionString = "Data Source=SyncSourcing.db";

    public async Task InitializeDatabase()
    {
        using var db = new SqliteConnection(_connectionString);
        await db.OpenAsync();

        await db.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS Carts (
                Id TEXT PRIMARY KEY,
                Version INT,
                IsCancelled BOOLEAN,
                Total DECIMAL
            );");

        await db.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS CartEvents (
                SequenceId INTEGER PRIMARY KEY AUTOINCREMENT,
                CartId TEXT,
                EventType TEXT,
                Payload TEXT,
                Metadata TEXT
            );");
    }

    public async Task<ShoppingCart?> GetCart(Guid id)
    {
        using var db = new SqliteConnection(_connectionString);
        return await db.QueryFirstOrDefaultAsync<ShoppingCart>(
            "SELECT * FROM Carts WHERE Id = @id", new { id });
    }

    public async Task<CommandResult> TryPersistEvent(
        Guid cartId, 
        string eventType, 
        object payload, 
        EventMetadata meta, 
        Func<ShoppingCart, ShoppingCart> applyFunc)
    {
        using var db = new SqliteConnection(_connectionString);
        await db.OpenAsync();

        // 1. Fetch current state
        var current = await db.QueryFirstOrDefaultAsync<ShoppingCart>(
            "SELECT * FROM Carts WHERE Id = @cartId", new { cartId });

        int currentVersion = current?.Version ?? 0;

        // 2. Concurrency Check
        if (meta.ExpectedVersion != currentVersion)
        {
            return new CommandResult(false, 
                $"[CONFLICT] {meta.UserId}: Expected v{meta.ExpectedVersion}, found v{currentVersion}");
        }

        var newState = applyFunc(current ?? new ShoppingCart { Id = cartId });

        // 3. BATCH SQL WITH UPSERT
        // We use INSERT...ON CONFLICT (UPSERT) to handle both initial creation and updates.
        // The 'WHERE' clause on the update ensures the version check is respected.
        var sqlBatch = @"
            INSERT INTO Carts (Id, Version, IsCancelled, Total) 
            VALUES (@Id, @Version, @IsCancelled, @Total)
            ON CONFLICT(Id) DO UPDATE SET 
                Version = excluded.Version, 
                IsCancelled = excluded.IsCancelled, 
                Total = excluded.Total
            WHERE Carts.Version = @ExpectedVersion;

            INSERT INTO CartEvents (CartId, EventType, Payload, Metadata)
            SELECT @Id, @Type, @Payload, @Meta
            WHERE (SELECT changes()) = 1;
        ";

        var affectedRows = await db.ExecuteAsync(sqlBatch, new {
            Id = newState.Id,
            Version = newState.Version,
            IsCancelled = newState.IsCancelled,
            Total = newState.Total,
            ExpectedVersion = meta.ExpectedVersion, 
            Type = eventType,
            Payload = JsonSerializer.Serialize(payload),
            Meta = JsonSerializer.Serialize(meta)
        });

        if (affectedRows >= 1) 
        {
            return new CommandResult(true, 
                $"[SUCCESS] {meta.UserId} batched {eventType} (v{newState.Version})");
        }

        return new CommandResult(false, $"[CONCURRENCY ERROR] {meta.UserId}: State was modified.");
    }
}

// --- APPLICATION LAYER ---
class Program
{
    static async Task Main()
    {
        SqlMapper.AddTypeHandler(new GuidTypeHandler());

        var store = new SqlEventStore();
        await store.InitializeDatabase();
        
        var cartId = Guid.NewGuid();
        Console.WriteLine("=== PoC 3: SQL Backed Sync-Sourcing (Batch Mode) ===\n");

        // Initialization now works because of the UPSERT logic
        var initResult = await store.TryPersistEvent(
            cartId, 
            "CartCreated", 
            new { }, 
            new EventMetadata(Guid.NewGuid(), "System", 0), 
            s => s with { Version = 1 });

        if (!initResult.Success) throw new Exception(initResult.Message);

        var initial = await store.GetCart(cartId);
        if (initial == null) throw new Exception("Critical Error: Cart not found after creation.");

        Console.WriteLine($"DB INITIALIZED: Cart {cartId} is at Version {initial.Version}\n");

        var workers = new List<Task<CommandResult>>();
        for (int i = 1; i <= 10; i++)
        {
            int id = i;
            workers.Add(Task.Run(() => 
                store.TryPersistEvent(
                    cartId, 
                    "StatusUpdated", 
                    new { Reason = "Race" }, 
                    new EventMetadata(Guid.NewGuid(), $"Worker-{id:D2}", initial.Version), 
                    s => s with { IsCancelled = true, Version = s.Version + 1 })
            ));
        }

        var results = await Task.WhenAll(workers);
        foreach (var res in results)
        {
            Console.ForegroundColor = res.Success ? ConsoleColor.Green : ConsoleColor.Red;
            Console.WriteLine(res.Message);
        }
        Console.ResetColor();

        var final = await store.GetCart(cartId);
        Console.WriteLine("\n" + new string('-', 60));
        Console.WriteLine($"FINAL DB STATE: Version {final!.Version} | Cancelled: {final.IsCancelled}");
        Console.WriteLine(new string('-', 60));
    }
}