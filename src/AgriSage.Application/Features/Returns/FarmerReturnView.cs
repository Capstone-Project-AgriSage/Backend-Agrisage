namespace AgriSage.Application.Features.Returns;

// What a FARMER may see of a return: the same response as the staff get, without what the goods cost the store
// (original COGS unit cost and the inventory cost of the returned goods). Pure functions, no I/O.
public static class FarmerReturnView
{
    public static SalesReturnResponse Redact(SalesReturnResponse response) =>
        response with
        {
            Items = response.Items
                .Select(item => item with { OriginalCogsUnitCost = null, ReturnInventoryCostValue = null })
                .ToList(),
        };

    public static ReturnableResponse Redact(ReturnableResponse response) =>
        response with
        {
            Items = response.Items
                .Select(item => item with
                {
                    Sources = item.Sources.Select(source => source with { OriginalCogsUnitCost = null }).ToList(),
                })
                .ToList(),
        };
}
