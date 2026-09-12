namespace NawiriAI.Connectors;

// All identifiers and records are invented. Immutable internal records keep fixture data out of query inputs.
internal sealed record DemoProduct(string TenantId, string Id, string Label, string Category, decimal Price, decimal UnitCost);
internal sealed record DemoSaleItem(string ProductId, decimal Quantity, decimal UnitPrice, decimal UnitCost)
{
    public decimal Revenue => Quantity * UnitPrice;
    public decimal Cost => Quantity * UnitCost;
}
internal sealed record DemoSale(string TenantId, string BranchId, string Id, DateOnly Date, string CustomerId, IReadOnlyList<DemoSaleItem> Items)
{
    public decimal Revenue => Items.Sum(x => x.Revenue);
    public decimal Cost => Items.Sum(x => x.Cost);
}
internal sealed record DemoInventory(string TenantId, string BranchId, string ProductId, decimal Quantity, decimal ReorderLevel);
internal sealed record DemoCustomer(string TenantId, string BranchId, string Id, string Label, decimal Balance, DateOnly DueDate);
internal sealed record DemoPayment(string TenantId, string BranchId, string SaleId, DateOnly Date, string Method, decimal Amount);
internal sealed record DemoExpense(string TenantId, string BranchId, string Id, DateOnly Date, string Category, decimal Amount);
internal sealed record SyntheticDataset(IReadOnlyList<DemoProduct> Products, IReadOnlyList<DemoSale> Sales,
    IReadOnlyList<DemoInventory> Inventory, IReadOnlyList<DemoCustomer> Customers, IReadOnlyList<DemoPayment> Payments, IReadOnlyList<DemoExpense> Expenses)
{
    public static SyntheticDataset Create(DateOnly today, int seed)
    {
        const string tenant = SyntheticBusinessDataProvider.SampleTenant;
        DemoProduct[] products = [
            new(tenant, "demo-product-tea", "Demo Tea", "Demo Beverages", 120, 80),
            new(tenant, "demo-product-coffee", "Demo Coffee", "Demo Beverages", 250, 150),
            new(tenant, "demo-product-bread", "Demo Bread", "Demo Pantry", 80, 50),
            new(tenant, "demo-product-rice", "Demo Rice", "Demo Pantry", 400, 260),
            new(tenant, "demo-product-soap", "Demo Soap", "Demo Household", 180, 110),
            new(tenant, "demo-product-tissue", "Demo Tissue", "Demo Household", 90, 55) ];
        var sales = new List<DemoSale>();
        var payments = new List<DemoPayment>();
        var expenses = new List<DemoExpense>();
        var inventory = new List<DemoInventory>();
        var customers = new List<DemoCustomer>();
        uint state = unchecked((uint)seed);
        int Next(int upper) { state = unchecked(state * 1664525u + 1013904223u); return (int)(state % (uint)upper); }
        foreach (var branch in new[] { SyntheticBusinessDataProvider.SampleBranch, SyntheticBusinessDataProvider.SecondBranch })
        {
            var main = branch == SyntheticBusinessDataProvider.SampleBranch;
            for (var index = 0; index < products.Length; index++)
                inventory.Add(new(tenant, branch, products[index].Id, new decimal[] { 12, 2, 0, 20, 4, 50 }[index] * (main ? 1 : 2), new decimal[] { 5, 5, 4, 6, 5, 10 }[index]));
            for (var index = 0; index < 4; index++)
                customers.Add(new(tenant, branch, $"{branch}-customer-{index}", $"Demo customer {index + 1}", new decimal[] { 1200, 600, 0, 2400 }[index] * (main ? 1 : 0.5m), today.AddDays(new[] { -45, -10, 10, -75 }[index])));
            for (var offset = 0; offset < 120; offset++)
            {
                var day = today.AddDays(-offset);
                for (var saleIndex = 0; saleIndex < (main ? 3 : 1); saleIndex++)
                {
                    var first = products[(offset + saleIndex) % products.Length];
                    var second = products[(offset + saleIndex + 2) % products.Length];
                    var quantity = 1 + Next(4);
                    if (main && offset == 0 && saleIndex == 2) quantity = 20;
                    var sale = new DemoSale(tenant, branch, $"{branch}-sale-{offset:D3}-{saleIndex}", day, $"{branch}-customer-{(offset + saleIndex) % 4}", Array.AsReadOnly(new[] {
                        new DemoSaleItem(first.Id, quantity, first.Price, first.UnitCost), new DemoSaleItem(second.Id, 1 + Next(2), second.Price, second.UnitCost) }));
                    sales.Add(sale);
                    payments.Add(new(tenant, branch, sale.Id, day, new[] { "Cash", "Card", "Mobile money" }[saleIndex % 3], sale.Revenue));
                }
                expenses.Add(new(tenant, branch, $"{branch}-expense-{offset:D3}-rent", day, "Demo rent allocation", main ? 30 : 15));
                expenses.Add(new(tenant, branch, $"{branch}-expense-{offset:D3}-utilities", day, "Demo utilities", main ? 10 : 5));
            }
        }
        // Deliberate out-of-scope sentinels exercise tenant filtering even when a caller guesses identifiers.
        sales.Add(new("demo-other-company", SyntheticBusinessDataProvider.SampleBranch, "demo-foreign-sale", today, "demo-foreign-customer",
            Array.AsReadOnly(new[] { new DemoSaleItem(products[0].Id, 1000000, 120, 80) })));
        inventory.Add(new("demo-other-company", SyntheticBusinessDataProvider.SampleBranch, products[0].Id, 999999, 5));
        customers.Add(new("demo-other-company", SyntheticBusinessDataProvider.SampleBranch, "demo-foreign-customer", "Demo foreign customer", 99999999, today));
        payments.Add(new("demo-other-company", SyntheticBusinessDataProvider.SampleBranch, "demo-foreign-sale", today, "Cash", 120000000));
        expenses.Add(new("demo-other-company", SyntheticBusinessDataProvider.SampleBranch, "demo-foreign-expense", today, "Foreign sentinel", 99999999));
        return new(Array.AsReadOnly(products), Array.AsReadOnly(sales.ToArray()), Array.AsReadOnly(inventory.ToArray()),
            Array.AsReadOnly(customers.ToArray()), Array.AsReadOnly(payments.ToArray()), Array.AsReadOnly(expenses.ToArray()));
    }
}
