using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SyncSourcing.PoC1.SyncedCache;

public record PriceUpdated(Guid ProductId, decimal NewPrice, int Version, DateTime Timestamp);

public class Product
{
    public Guid Id { get; init; } = Guid.NewGuid();
    private decimal _price;
    public decimal Price => _price; 
    private int _version = 0;
    public int Version => _version;

    public Product(decimal initialPrice) => _price = initialPrice;

    public bool TryUpdatePrice(decimal newPrice, int expectedVersion, out PriceUpdated? @event)
    {
        @event = null;
        int nextVersion = expectedVersion + 1;

        if (Interlocked.CompareExchange(ref _version, nextVersion, expectedVersion) == expectedVersion)
        {
            _price = newPrice;
            @event = new PriceUpdated(this.Id, newPrice, nextVersion, DateTime.UtcNow);
            return true;
        }
        return false;
    }
}

class Program
{
    private static readonly ConcurrentBag<PriceUpdated> _eventLog = new();

    static async Task Main(string[] args)
    {
        Console.WriteLine("=== PoC1: The Sync-Sourcing Grand Prix ===");
        var product = new Product(10.0m);
        int currentVersion = product.Version;
        int contestantCount = 20;

        Console.WriteLine($"Starting Race with {contestantCount} users. Version is {currentVersion}.");
        Console.WriteLine("3... 2... 1... GO!\n");

        // Prepare 20 contestants
        var tasks = Enumerable.Range(1, contestantCount).Select(i => 
            Task.Run(() => {
                // Small random jitter so they don't hit the CPU in the exact same nanosecond 
                // based on array order, making it truly random.
                Thread.Sleep(new Random().Next(1, 10)); 
                return TrySave(product, 10.0m + i, currentVersion, $"User {i:00}");
            })
        );

        var results = await Task.WhenAll(tasks);

        // Summary
        var winner = results.FirstOrDefault(r => r.Success);
        Console.WriteLine("\n--- Race Over ---");
        if (winner != null)
            Console.WriteLine($"WINNER: {winner.Name} (Set price to {winner.Price})");
        
        Console.WriteLine($"LOSERS: {results.Count(r => !r.Success)} users were rejected by the Atomic Gatekeeper.");
        Console.WriteLine($"Final Cache State: Price={product.Price}, Version={product.Version}");
    }

    // Helper record to capture the result of a race participant
    record RaceResult(string Name, bool Success, decimal Price);

    static RaceResult TrySave(Product product, decimal newPrice, int versionAtStart, string userName)
    {
        bool success = product.TryUpdatePrice(newPrice, versionAtStart, out var @event);
        if (success) {
            _eventLog.Add(@event!);
            // We only print the success to keep the window clean
            Console.WriteLine($"[!] {userName} reached the CPU first and WON.");
        }
        return new RaceResult(userName, success, newPrice);
    }
}