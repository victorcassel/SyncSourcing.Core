using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SyncSourcing.PoC1.SyncedCache;

// --- 1. THE EVENT (The immutable record of what happened) ---
public record PriceUpdated(Guid ProductId, decimal NewPrice, int Version, DateTime Timestamp);

// --- 2. THE DOMAIN OBJECT (The Cache / Managed State) ---
public class Product
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public decimal Price { get; private set; }
    
    // The "Gatekeeper": Sequence number within this specific aggregate
    private int _version = 0;
    public int Version => _version;

    // The Sync-Sourcing Core Logic:
    // Atomic Compare-and-Swap (CAS) to update State and generate Event in "sync"
    public bool TryUpdatePrice(decimal newPrice, int expectedVersion, out PriceUpdated? @event)
    {
        @event = null;
        int nextVersion = expectedVersion + 1;

        // ATOMIC GATEKEEPER:
        // Only updates _version to nextVersion if current value is exactly expectedVersion.
        // This mirrors: UPDATE Product SET Version = 6 WHERE Id = @Id AND Version = 5;
        if (Interlocked.CompareExchange(ref _version, nextVersion, expectedVersion) == expectedVersion)
        {
            // The Gate is open! Update the local state (The Cache)
            this.Price = newPrice;

            // Create the event to be stored
            @event = new PriceUpdated(this.Id, newPrice, nextVersion, DateTime.UtcNow);
            return true;
        }

        return false; // Concurrency conflict!
    }
}

// --- 3. THE DEMO PROGRAM ---
class Program
{
    // A simple thread-safe list to simulate our "Event Store"
    private static readonly ConcurrentBag<PriceUpdated> _eventLog = new();

    static async Task Main(string[] args)
    {
        Console.WriteLine("=== PoC1: Atomic Sync-Sourcing (In-Memory) ===\n");

        var product = new Product { Price = 10.0m };
        Console.WriteLine($"Initial State: Price={product.Price}, Version={product.Version}");

        // SIMULATION: Two different users try to update the price at the exact same time
        Console.WriteLine("\n--- Racing two simultaneous updates ---");

        int currentVersion = product.Version;

        var task1 = Task.Run(() => TrySaveChange(product, 15.0m, currentVersion, "User A"));
        var task2 = Task.Run(() => TrySaveChange(product, 20.0m, currentVersion, "User B"));

        await Task.WhenAll(task1, task2);

        Console.WriteLine("\n--- Final Results ---");
        Console.WriteLine($"Final Cache State: Price={product.Price}, Version={product.Version}");
        Console.WriteLine($"Total Events in Log: {_eventLog.Count}");
        foreach (var ev in _eventLog)
        {
            Console.WriteLine($" - Event: {ev.NewPrice} (v{ev.Version}) at {ev.Timestamp:HH:mm:ss.fff}");
        }
    }

    static void TrySaveChange(Product product, decimal newPrice, int versionAtStart, string userName)
    {
        if (product.TryUpdatePrice(newPrice, versionAtStart, out var @event))
        {
            // Because TryUpdatePrice was atomic, we are GUARANTEED that 
            // the Cache is updated and we are the only one who got this Event.
            _eventLog.Add(@event!);
            Console.WriteLine($"[SUCCESS] {userName} updated price to {newPrice}");
        }
        else
        {
            Console.WriteLine($"[CONFLICT] {userName} failed to update. Version {versionAtStart} was already changed.");
        }
    }
}
