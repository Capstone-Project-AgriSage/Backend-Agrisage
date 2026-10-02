using AgriSage.Api.Extensions;
using AgriSage.Application.Features.Files;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AgriSage.Api.Features.Files;

// Image upload for products and brands (Admin and Store Owner). The returned URL is then saved through the catalog
// API (imageUrl / logoUrl). The type is decided by the file content, the name by the server.
[ApiController]
[Route("api/files")]
[Authorize(Roles = ApiRoles.Manage)]
public sealed class FilesController(IProductImageService images) : ControllerBase
{
    // multipart/form-data with one field named "file": JPEG, PNG or WebP, at most 3 MB.
    [HttpPost("product-images")]
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
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteProductImage([FromQuery] string? key, CancellationToken cancellationToken)
    {
        await images.DeleteAsync(key, cancellationToken);

        return NoContent();
    }
}
