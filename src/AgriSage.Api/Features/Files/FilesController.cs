using AgriSage.Api.Extensions;
using AgriSage.Application.Features.Files;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AgriSage.Api.Features.Files;

// Image upload. Product/brand images: Admin and Store Owner; the returned URL is then saved through the catalog API
// (imageUrl / logoUrl). Delivery photos: every staff role (delivery staff take them); the URL is saved by the delivery
// use cases. The type is decided by the file content, the name by the server.
[ApiController]
[Route("api/files")]
public sealed class FilesController(IProductImageService images, IDeliveryProofService proofs) : ControllerBase
{
    // multipart/form-data with one field named "file": JPEG, PNG or WebP, at most 3 MB.
    [HttpPost("product-images")]
    [Authorize(Roles = ApiRoles.Manage)]
    [EnableRateLimiting(RateLimitingExtensions.UploadPolicy)]
    [RequestSizeLimit(ImageRules.MaxBytes + 512 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = ImageRules.MaxBytes + 512 * 1024)]
    [Consumes("multipart/form-data")]
    [ProducesResponseType<UploadedImageResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> UploadProductImage(IFormFile file, CancellationToken cancellationToken)
    {
        await using var stream = file.OpenReadStream();
        var response = await images.UploadAsync(stream, cancellationToken);

        return StatusCode(StatusCodes.Status201Created, response);
    }

    // The key is the storageKey returned by the upload.
    [HttpDelete("product-images")]
    [Authorize(Roles = ApiRoles.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteProductImage([FromQuery] string? key, CancellationToken cancellationToken)
    {
        await images.DeleteAsync(key, cancellationToken);

        return NoContent();
    }

    // multipart/form-data with one field named "file": JPEG, PNG or WebP, at most 5 MB.
    [HttpPost("delivery-proofs")]
    [Authorize(Roles = ApiRoles.Read)]
    [EnableRateLimiting(RateLimitingExtensions.UploadPolicy)]
    [RequestSizeLimit(ImageRules.MaxProofBytes + 512 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = ImageRules.MaxProofBytes + 512 * 1024)]
    [Consumes("multipart/form-data")]
    [ProducesResponseType<UploadedImageResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> UploadDeliveryProof(IFormFile file, CancellationToken cancellationToken)
    {
        await using var stream = file.OpenReadStream();
        var response = await proofs.UploadAsync(stream, cancellationToken);

        return StatusCode(StatusCodes.Status201Created, response);
    }

    // Admin and Store Owner only: a photo kept as delivery evidence must not be removed by delivery staff.
    [HttpDelete("delivery-proofs")]
    [Authorize(Roles = ApiRoles.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteDeliveryProof([FromQuery] string? key, CancellationToken cancellationToken)
    {
        await proofs.DeleteAsync(key, cancellationToken);

        return NoContent();
    }
}
