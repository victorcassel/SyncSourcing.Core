using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;

namespace SyncSourcing.PoC2.SyncedCacheSourcing;

// --- DOMAIN ---
public record EventMetadata(Guid CorrelationId, string? UserId, int ExpectedVersion);
public record OrderItem(Guid Id, string ProductId, decimal Price);
public abstract record CartEvent(Guid CartId, EventMetadata Metadata);
public record CartCreated(Guid CartId, EventMetadata Metadata) : CartEvent(CartId, Metadata);
public record ItemRemoved(Guid CartId, Guid ItemId, EventMetadata Metadata) : CartEvent(CartId, Metadata);
public record CartCancelled(Guid CartId, string Reason, EventMetadata Metadata) : CartEvent(CartId, Metadata);

public record ShoppingCart(Guid Id, int Version, bool IsCancelled, List<OrderItem> Items)
{
    public ShoppingCart() : this(Guid.Empty, 0, false, new List<OrderItem>()) { }
    public ShoppingCart Apply(CartEvent ev) => ev switch
    {
        CartCreated e => this with { Id = e.CartId, Version = 1 },
        ItemRemoved e => this with { Items = Items.Where(i => i.Id != e.ItemId).ToList(), Version = Version + 1 },
        CartCancelled e => this with { IsCancelled = true, Version = Version + 1 },
        _ => this
    };
}

public record CommandResult(bool Success, string Message);

// --- INFRASTRUCTURE ---
public class EventStoreManager
{
    private readonly ConcurrentDictionary<Guid, ShoppingCart> _cache = new();
    private readonly List<CartEvent> _eventLog = new();
    private readonly object _syncLock = new();

    public ShoppingCart? Get(Guid id) => _cache.TryGetValue(id, out var s) ? s : null;

    public CommandResult TryAddEvent(CartEvent ev)
    {
        lock (_syncLock) 
        {
            _cache.TryGetValue(ev.CartId, out var current);
            int currentVersion = current?.Version ?? 0;

            string action = ev switch {
                ItemRemoved => "Remove Item",
                CartCancelled => "Cancel Cart",
                _ => "Update"
            };

            if (ev.Metadata.ExpectedVersion != currentVersion)
                return new CommandResult(false, $"[CONFLICT] {ev.Metadata.UserId} tried to '{action}' on v{ev.Metadata.ExpectedVersion}. Current is v{currentVersion}.");

            var newState = (current ?? new ShoppingCart()).Apply(ev);
            _cache[ev.CartId] = newState;
            _eventLog.Add(ev);

            return new CommandResult(true, $"[SUCCESS] {ev.Metadata.UserId} won! Performed '{action}'. New version: {newState.Version}");
        }
    }
}

// --- APP ---
class Program
{
    static async Task Main()
    {
        var store = new EventStoreManager();
        var cartId = Guid.NewGuid();
        var itemId = Guid.NewGuid();

        Console.WriteLine("=== PoC 2: The Transparent Grand Prix ===\n");

        // Step 1: Initialize
        store.TryAddEvent(new CartCreated(cartId, new EventMetadata(Guid.NewGuid(), "System", 0)));
        
        // Manual setup of one item
        var initial = store.Get(cartId)! with { Items = new List<OrderItem> { new OrderItem(itemId, "Drone", 1200m) } };
        typeof(ConcurrentDictionary<Guid, ShoppingCart>).GetMethod("TryUpdate")?
            .Invoke(typeof(EventStoreManager).GetField("_cache", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(store), 
            new object[] { cartId, initial, store.Get(cartId)! });

        var stateBefore = store.Get(cartId)!;
        Console.WriteLine($"INITIAL STATE: Version {stateBefore.Version} | Items: {stateBefore.Items.Count} | Cancelled: {stateBefore.IsCancelled}");
        Console.WriteLine("------------------------------------------------------------");
        Console.WriteLine("Starting the race with 10 workers...\n");

        var workers = new List<Task<CommandResult>>();
        for (int i = 1; i <= 10; i++)
        {
            int id = i;
            workers.Add(Task.Run(() => {
                var meta = new EventMetadata(Guid.NewGuid(), $"Worker-{id:D2}", stateBefore.Version);
                return id % 2 == 0 
                    ? store.TryAddEvent(new ItemRemoved(cartId, itemId, meta))
                    : store.TryAddEvent(new CartCancelled(cartId, "Customer quit", meta));
            }));
        }

        var results = await Task.WhenAll(workers);
        foreach (var res in results)
        {
            Console.ForegroundColor = res.Success ? ConsoleColor.Green : ConsoleColor.Red;
            Console.WriteLine(res.Message);
        }
        Console.ResetColor();

        // Step 3: Final State
        var final = store.Get(cartId)!;
        Console.WriteLine("\n------------------------------------------------------------");
        Console.WriteLine("FINAL STATE AFTER RACE:");
        Console.WriteLine($"- Version: {final.Version}");
        Console.WriteLine($"- Item Count: {final.Items.Count}");
        Console.WriteLine($"- Status: {(final.IsCancelled ? "Cancelled" : "Open")}");
    }
}