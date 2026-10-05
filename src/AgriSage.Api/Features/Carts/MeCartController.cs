using AgriSage.Api.Extensions;
using AgriSage.Application.Features.Carts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgriSage.Api.Features.Carts;

// The Farmer's cart (FLOW_2 §4): prices are recalculated on every response.
[ApiController]
[Route("api/me/cart")]
[Authorize(Roles = ApiRoles.Farmer)]
public sealed class MeCartController(ICartService service) : ControllerBase
{
    [HttpGet]
    public Task<CartResponse> Get(CancellationToken token) => service.GetAsync(token);

    [HttpPost("items")]
    public Task<CartResponse> AddItem(AddCartItemRequest request, CancellationToken token) => service.AddItemAsync(request, token);

    [HttpPut("items/{itemId:guid}")]
    public Task<CartResponse> UpdateItem(Guid itemId, UpdateCartItemRequest request, CancellationToken token) =>
        service.UpdateItemAsync(itemId, request, token);

    [HttpDelete("items/{itemId:guid}")]
    public Task<CartResponse> RemoveItem(Guid itemId, CancellationToken token) => service.RemoveItemAsync(itemId, token);

    [HttpDelete]
    public async Task<IActionResult> Clear(CancellationToken token)
    {
        await service.ClearAsync(token);
        return NoContent();
    }
}
