using System;
using System.Collections.Generic;
using System.Linq;

namespace SyncSourcing.PoC1.BasicEventSourcing;

// --- DOMAIN LAYER: Data Structures ---
public record OrderItem(Guid Id, string ProductId, int Quantity, decimal Price);

// --- DOMAIN LAYER: Events (Categorized) ---
public abstract record CartEvent(Guid CartId, DateTime OccurredAt);

// 1. User Identity Domain (GDPR Sensitive)
public record UserAuthenticated(Guid CartId, string UserId, string FullName, string Email) : CartEvent(CartId, DateTime.UtcNow);
public record UserInfoCollected(Guid CartId, string IdentityNumber, string Address) : CartEvent(CartId, DateTime.UtcNow);
public record UserKYCVerified(Guid CartId, bool IsVerified, string RiskLevel) : CartEvent(CartId, DateTime.UtcNow);

// 2. Shopping Cart Domain
public record CartCreated(Guid CartId) : CartEvent(CartId, DateTime.UtcNow);
public record ItemAdded(Guid CartId, OrderItem Item) : CartEvent(CartId, DateTime.UtcNow);
public record ItemRemoved(Guid CartId, Guid ItemId) : CartEvent(CartId, DateTime.UtcNow);
public record TotalAmountUpdated(Guid CartId, decimal NewTotalAmount, string Message) : CartEvent(CartId, DateTime.UtcNow);
public record CartCancelled(Guid CartId, string Reason) : CartEvent(CartId, DateTime.UtcNow);

// --- DOMAIN LAYER: The State ---
public enum CartStatus { Open, Cancelled }
public record ShoppingCart(
    Guid Id, 
    string? CustomerName, 
    string? CustomerIdentity, // Sensitive
    bool IsKycVerified,
    decimal TotalAmount, 
    bool TotalNeedsRecalculation,
    CartStatus Status, 
    List<OrderItem> Items)
{
    public ShoppingCart() : this(Guid.Empty, null, null, false, 0, false, CartStatus.Open, new List<OrderItem>()) { }

    public ShoppingCart Apply(CartEvent ev) => ev switch
    {
        CartCreated e => this with { Id = e.CartId, Items = new List<OrderItem>() },
        
        // Handling sensitive data with "Cleaned" awareness
        UserAuthenticated e => this with { CustomerName = e.FullName },
        UserInfoCollected e => this with { CustomerIdentity = e.IdentityNumber },
        UserKYCVerified e => this with { IsKycVerified = e.IsVerified },
        
        ItemAdded e => AddItem(e.Item),
        ItemRemoved e => RemoveItem(e.ItemId),
        
        TotalAmountUpdated e => this with { TotalAmount = e.NewTotalAmount, TotalNeedsRecalculation = false },
        CartCancelled e => this with { Status = CartStatus.Cancelled },
        _ => this
    };

    private ShoppingCart AddItem(OrderItem item)
    {
        var newItems = new List<OrderItem>(Items) { item };
        return this with { Items = newItems, TotalNeedsRecalculation = true };
    }

    private ShoppingCart RemoveItem(Guid itemId)
    {
        var newItems = Items.Where(i => i.Id != itemId).ToList();
        return this with { Items = newItems, TotalNeedsRecalculation = true };
    }
}

// --- INFRASTRUCTURE LAYER ---
public class EventStoreManager
{
    private readonly List<CartEvent> _globalStream = new();

    public ShoppingCart Get(Guid id)
    {
        var cartEvents = _globalStream.Where(e => e.CartId == id);
        var state = new ShoppingCart();
        foreach (var ev in cartEvents)
        {
            state = state.Apply(ev);
        }
        return state;
    }

    public void AddEvent(CartEvent ev) => _globalStream.Add(ev);
}

// --- BUSINESS LOGIC LAYER (API) ---
public class CartService
{
    private readonly EventStoreManager _store;
    public CartService(EventStoreManager store) => _store = store;

    public void AuthenticateUser(Guid id, string userId, string name, string email) 
        => _store.AddEvent(new UserAuthenticated(id, userId, name, email));

    public void CollectUserInfo(Guid id, string pnr, string addr) 
        => _store.AddEvent(new UserInfoCollected(id, pnr, addr));

    public void AddItem(Guid id, string productId, int qty, decimal price) 
        => _store.AddEvent(new ItemAdded(id, new OrderItem(Guid.NewGuid(), productId, qty, price)));

    public void RemoveItem(Guid id, Guid itemId)
        => _store.AddEvent(new ItemRemoved(id, itemId));

    public void UpdateTotal(Guid id, decimal amount) 
        => _store.AddEvent(new TotalAmountUpdated(id, amount, "Recalculated based on items"));

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

        Console.WriteLine("=== PoC 1: Advanced Event Sourcing (Complex Domain) ===");
        
        // 1. Setup Identity
        service.AuthenticateUser(cartId, "user_123", "Victor Cassel", "victor@example.com");
        service.CollectUserInfo(cartId, "19800101-1234", "Storgatan 1, Sweden");
        
        // 2. Manage Items
        service.AddItem(cartId, "Laptop", 1, 1500m);
        var item2Id = Guid.NewGuid();
        store.AddEvent(new ItemAdded(cartId, new OrderItem(item2Id, "Mouse", 1, 50m)));

        // 3. Logic: Total needs recalculation
        var stateAfterItems = service.GetCart(cartId);
        if (stateAfterItems.TotalNeedsRecalculation)
        {
            Console.WriteLine("[System] Recalculating totals...");
            service.UpdateTotal(cartId, stateAfterItems.Items.Sum(i => i.Price * i.Quantity));
        }

        // 4. Remove item
        service.RemoveItem(cartId, item2Id);

        var finalState = service.GetCart(cartId);
        Console.WriteLine($"\nCustomer: {finalState.CustomerName}");
        Console.WriteLine($"ID Verified: {finalState.IsKycVerified}");
        Console.WriteLine($"Item Count: {finalState.Items.Count}");
        Console.WriteLine($"Total Amount: {finalState.TotalAmount}");
        Console.WriteLine($"Needs Sync? {finalState.TotalNeedsRecalculation}");
    }
}