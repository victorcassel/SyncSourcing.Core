using System;
using System.Collections.Generic;
using System.Linq;

namespace SyncSourcing.PoC1.BasicEventSourcing;

// --- DOMAIN LAYER: Metadata & Data Structures ---
public record EventMetadata(
    Guid CorrelationId, 
    string? UserId = null, // Optional for traceability
    string? CausalityId = null, 
    string SchemaVersion = "1.0",
    Dictionary<string, object>? ExtraData = null);

public record OrderItem(Guid Id, string ProductId, int Quantity, decimal Price);

// --- DOMAIN LAYER: Events (Lean Business Data Only) ---
public abstract record CartEvent(Guid CartId, DateTime OccurredAt, EventMetadata Metadata);

public record CartCreated(Guid CartId, EventMetadata Metadata) 
    : CartEvent(CartId, DateTime.UtcNow, Metadata);

public record UserAuthenticated(Guid CartId, string UserId, string FullName, string Email, EventMetadata Metadata) 
    : CartEvent(CartId, DateTime.UtcNow, Metadata);

public record UserKYCVerified(Guid CartId, bool IsVerified, string RiskLevel, EventMetadata Metadata) 
    : CartEvent(CartId, DateTime.UtcNow, Metadata);

public record ItemAdded(Guid CartId, OrderItem Item, EventMetadata Metadata) 
    : CartEvent(CartId, DateTime.UtcNow, Metadata);

public record ItemRemoved(Guid CartId, Guid ItemId, EventMetadata Metadata) 
    : CartEvent(CartId, DateTime.UtcNow, Metadata);

public record TotalAmountUpdated(Guid CartId, decimal NewTotalAmount, string Message, EventMetadata Metadata) 
    : CartEvent(CartId, DateTime.UtcNow, Metadata);

// --- DOMAIN LAYER: The State ---
public record ShoppingCart(
    Guid Id, 
    string? CustomerName, 
    bool IsKycVerified,
    decimal TotalAmount, 
    bool TotalNeedsRecalculation,
    List<OrderItem> Items)
{
    public ShoppingCart() : this(Guid.Empty, null, false, 0, false, new List<OrderItem>()) { }

    public ShoppingCart Apply(CartEvent ev) => ev switch
    {
        CartCreated e => this with { Id = e.CartId },
        UserAuthenticated e => this with { CustomerName = e.FullName },
        UserKYCVerified e => this with { IsKycVerified = e.IsVerified },
        ItemAdded e => this with { 
            Items = new List<OrderItem>(Items) { e.Item }, 
            TotalNeedsRecalculation = true 
        },
        ItemRemoved e => this with { 
            Items = Items.Where(i => i.Id != e.ItemId).ToList(), 
            TotalNeedsRecalculation = true 
        },
        TotalAmountUpdated e => this with { 
            TotalAmount = e.NewTotalAmount, 
            TotalNeedsRecalculation = false 
        },
        _ => this
    };
}

// --- INFRASTRUCTURE LAYER ---
public class EventStoreManager
{
    private readonly List<CartEvent> _globalStream = new();
    public ShoppingCart Get(Guid id)
    {
        var state = new ShoppingCart();
        foreach (var ev in _globalStream.Where(e => e.CartId == id)) state = state.Apply(ev);
        return state;
    }
    public void AddEvent(CartEvent ev) => _globalStream.Add(ev);
}

// --- BUSINESS LOGIC LAYER (API) ---
public class CartService
{
    private readonly EventStoreManager _store;
    public CartService(EventStoreManager store) => _store = store;

    private EventMetadata CreateMeta(string? userId = null, string? causality = null) 
        => new(Guid.NewGuid(), userId, causality);

    public void CreateCart(Guid id, string user) 
        => _store.AddEvent(new CartCreated(id, CreateMeta(user)));

    public void AuthenticateUser(Guid id, string externalUserId, string name, string email) 
        => _store.AddEvent(new UserAuthenticated(id, externalUserId, name, email, CreateMeta(externalUserId)));

    public void VerifyKYC(Guid id, string user, bool verified)
        => _store.AddEvent(new UserKYCVerified(id, verified, "Low", CreateMeta(user)));

    public void AddItem(Guid id, string user, string prod, int qty, decimal price) 
        => _store.AddEvent(new ItemAdded(id, new OrderItem(Guid.NewGuid(), prod, qty, price), CreateMeta(user)));

    public void RemoveItem(Guid id, string user, Guid itemId)
        => _store.AddEvent(new ItemRemoved(id, itemId, CreateMeta(user)));

    public void UpdateTotal(Guid id, string user, decimal amount, string causality) 
        => _store.AddEvent(new TotalAmountUpdated(id, amount, "Recalculation", CreateMeta(user, causality)));

    public ShoppingCart GetCart(Guid id) => _store.Get(id);
}

// --- DEMO ---
class Program
{
    static void Main()
    {
        var store = new EventStoreManager();
        var service = new CartService(store);
        var cartId = Guid.NewGuid();
        const string currentUserId = "user_vcl_99";

        Console.WriteLine("=== PoC 1: Lean Event Sourcing (Focus on Business Data) ===");
        
        service.CreateCart(cartId, currentUserId);
        service.AuthenticateUser(cartId, "AUTH0|12345", "Victor Cassel", "victor@example.com");
        service.VerifyKYC(cartId, currentUserId, true);
        
        var cameraId = Guid.NewGuid();
        // Notice how 'currentUserId' goes to metadata, while 'OrderItem' is the business data
        service.AddItem(cartId, currentUserId, "Mirrorless Camera", 1, 3200m);

        var cart = service.GetCart(cartId);
        if (cart.TotalNeedsRecalculation)
        {
            service.UpdateTotal(cartId, currentUserId, cart.Items.Sum(i => i.Price), "ItemAdded");
        }

        var final = service.GetCart(cartId);
        Console.WriteLine($"\nCart Summary:");
        Console.WriteLine($"- Customer: {final.CustomerName}");
        Console.WriteLine($"- KYC Status: {(final.IsKycVerified ? "Verified" : "Pending")}");
        Console.WriteLine($"- Total: {final.TotalAmount} SEK");
        Console.WriteLine($"- Items: {final.Items.Count}");
    }
}