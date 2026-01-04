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

// --- ARCHITECTURAL STANDARDS: MESSAGING ---
public interface IMessagePublisher
{
    void Publish(string eventType, object payload);
}

public class ConsoleMessagePublisher : IMessagePublisher
{
    public void Publish(string eventType, object payload)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"   ▶️ [PUB] {eventType} broadcasted to Bus.");
        Console.ResetColor();
    }
}

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

// --- HYBRID EVENT STORE ---
public class HybridEventStore
{
    private readonly string _connectionString = "Data Source=SyncSourcing.db";
    private readonly ConcurrentDictionary<Guid, ShoppingCart> _l1Cache = new();
    private readonly IMessagePublisher _publisher;

    public HybridEventStore(IMessagePublisher publisher)
    {
        _publisher = publisher;
    }

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
        if (_l1Cache.TryGetValue(id, out var cached)) return cached;
        using var db = new SqliteConnection(_connectionString);
        var persistent = await db.QueryFirstOrDefaultAsync<ShoppingCart>("SELECT * FROM Carts WHERE Id = @id", new { id });
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
        // ◀️ [SUB] Command Received & L1 Check
        var state = await GetCart(cartId);
        int currentVersion = state?.Version ?? 0;

        if (meta.ExpectedVersion != currentVersion)
            return new CommandResult(false, $"[L1 CONFLICT] {meta.UserId}: Expected v{meta.ExpectedVersion}, found v{currentVersion}");

        var newState = applyFunc(state ?? new ShoppingCart { Id = cartId });

        // ▶️ [PUB] State Shadowed (L2 Optimized Batch)
        using var db = new SqliteConnection(_connectionString);
        await db.OpenAsync();

        var sqlBatch = @"
            -- Try Update first
            UPDATE Carts 
            SET Version = @Version, IsCancelled = @IsCancelled, Total = @Total 
            WHERE Id = @Id AND Version = @ExpectedVersion;

            -- Try Insert only if no rows were updated and we expected a new record
            INSERT INTO Carts (Id, Version, IsCancelled, Total)
            SELECT @Id, @Version, @IsCancelled, @Total
            WHERE (SELECT changes()) = 0 AND @ExpectedVersion = 0;

            -- Record the event only if state changed
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
            _l1Cache[cartId] = newState;
            _publisher.Publish(eventType, payload);
            return new CommandResult(true, $"[SUCCESS] {meta.UserId} Synced v{newState.Version}");
        }

        return new CommandResult(false, $"[L2 ERROR] Concurrency violation or record already exists.");
    }
}

// --- APP LAYER ---
class Program
{
    static async Task Main()
    {
        var publisher = new ConsoleMessagePublisher();
        var store = new HybridEventStore(publisher);
        
        await store.InitializeDatabase();
        var cartId = Guid.NewGuid();
        
        Console.WriteLine("=== PoC 3: Hybrid Sync-Sourcing (With Messaging) ===\n");

        await store.TryPersistEvent(cartId, "CartCreated", new { }, new EventMetadata(Guid.NewGuid(), "System", 0), s => s with { Version = 1 });
        var initial = await store.GetCart(cartId);

        var workers = new List<Task<CommandResult>>();
        for (int i = 1; i <= 10; i++)
        {
            int id = i;
            workers.Add(Task.Run(() => store.TryPersistEvent(
                cartId, "StatusUpdated", new { Reason = "Race" }, new EventMetadata(Guid.NewGuid(), $"Worker-{id:D2}", initial!.Version), 
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
        Console.WriteLine($"\nFINAL STATE: Version {final!.Version}");
    }
}