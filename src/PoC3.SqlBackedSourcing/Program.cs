using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
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

// --- INFRASTRUCTURE: HYBRID EVENT STORE ---
public class HybridEventStore
{
    private readonly string _connectionString = "Data Source=SyncSourcing.db";
    // L1 Cache: The primary gatekeeper
    private readonly ConcurrentDictionary<Guid, ShoppingCart> _l1Cache = new();

    public async Task InitializeDatabase()
    {
        SqlMapper.AddTypeHandler(new GuidTypeHandler());
        using var db = new SqliteConnection(_connectionString);
        await db.OpenAsync();
        await db.ExecuteAsync("CREATE TABLE IF NOT EXISTS Carts (Id TEXT PRIMARY KEY, Version INT, IsCancelled BOOLEAN, Total DECIMAL);");
        await db.ExecuteAsync("CREATE TABLE IF NOT EXISTS CartEvents (SequenceId INTEGER PRIMARY KEY AUTOINCREMENT, CartId TEXT, EventType TEXT, Payload TEXT, Metadata TEXT);");
    }

    public async Task<ShoppingCart?> GetCart(Guid id)
    {
        // 1. Check L1 Cache first (Memory)
        if (_l1Cache.TryGetValue(id, out var cached)) return cached;

        // 2. L1 Miss: Check L2 (SQL)
        using var db = new SqliteConnection(_connectionString);
        var persistent = await db.QueryFirstOrDefaultAsync<ShoppingCart>("SELECT * FROM Carts WHERE Id = @id", new { id });
        
        // 3. Hydrate L1 if found
        if (persistent != null) _l1Cache[id] = persistent;
        
        return persistent;
    }

    public async Task<CommandResult> TryPersistEvent(
        Guid cartId, 
        string eventType, 
        object payload, 
        EventMetadata meta, 
        Func<ShoppingCart, ShoppingCart> applyFunc)
    {
        // 1. PRIMARY GATEKEEPER: Check L1 Cache Version
        var state = await GetCart(cartId); // Hydrates L1 if needed
        int currentVersion = state?.Version ?? 0;

        if (meta.ExpectedVersion != currentVersion)
            return new CommandResult(false, $"[L1 CONFLICT] {meta.UserId}: Expected v{meta.ExpectedVersion}, found v{currentVersion}");

        // 2. APPLY DOMAIN LOGIC
        var newState = applyFunc(state ?? new ShoppingCart { Id = cartId });

        // 3. PERSISTENT SHADOW UPDATE (L2)
        // We use the same Batch logic to ensure L2 stays in sync
        using var db = new SqliteConnection(_connectionString);
        await db.OpenAsync();

        var sqlBatch = @"
            INSERT INTO Carts (Id, Version, IsCancelled, Total) 
            VALUES (@Id, @Version, @IsCancelled, @Total)
            ON CONFLICT(Id) DO UPDATE SET 
                Version = excluded.Version, IsCancelled = excluded.IsCancelled, Total = excluded.Total
            WHERE Carts.Version = @ExpectedVersion;

            INSERT INTO CartEvents (CartId, EventType, Payload, Metadata)
            SELECT @Id, @Type, @Payload, @Meta
            WHERE (SELECT changes()) = 1;";

        var affectedRows = await db.ExecuteAsync(sqlBatch, new {
            Id = newState.Id, Version = newState.Version, IsCancelled = newState.IsCancelled, Total = newState.Total,
            ExpectedVersion = meta.ExpectedVersion, Type = eventType,
            Payload = JsonSerializer.Serialize(payload), Meta = JsonSerializer.Serialize(meta)
        });

        if (affectedRows >= 1) 
        {
            // 4. UPDATE L1 CACHE on successful L2 persistence
            _l1Cache[cartId] = newState;
            return new CommandResult(true, $"[SUCCESS] {meta.UserId} (L1 + L2 Synced) v{newState.Version}");
        }

        return new CommandResult(false, $"[L2 SHADOW ERROR] {meta.UserId}: Persistence mismatch.");
    }
}

// --- APP LAYER ---
class Program
{
    static async Task Main()
    {
        var store = new HybridEventStore();
        await store.InitializeDatabase();
        var cartId = Guid.NewGuid();
        
        Console.WriteLine("=== PoC 3: Hybrid Sync-Sourcing (L1 Memory + L2 Shadow DB) ===\n");

        // Initialization
        await store.TryPersistEvent(cartId, "CartCreated", new { }, new EventMetadata(Guid.NewGuid(), "System", 0), s => s with { Version = 1 });
        var initial = await store.GetCart(cartId);
        Console.WriteLine($"INITIALIZED: Cart {cartId} in L1 & L2 (v{initial!.Version})\n");

        // The Race: 10 workers hitting L1 first
        var workers = new List<Task<CommandResult>>();
        for (int i = 1; i <= 10; i++)
        {
            int id = i;
            workers.Add(Task.Run(() => store.TryPersistEvent(
                cartId, "Update", new { i }, new EventMetadata(Guid.NewGuid(), $"Worker-{id:D2}", initial.Version), 
                s => s with { Version = s.Version + 1 })));
        }

        var results = await Task.WhenAll(workers);
        foreach (var res in results)
        {
            Console.ForegroundColor = res.Success ? ConsoleColor.Green : ConsoleColor.Red;
            Console.WriteLine(res.Message);
        }
        Console.ResetColor();

        var final = await store.GetCart(cartId);
        Console.WriteLine($"\nFINAL STATE: Version {final!.Version} (Verified in L1 Memory)");
    }
}