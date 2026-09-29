using AgriSage.Application.Common.Models;
using AgriSage.Application.Common.Validators;

namespace AgriSage.UnitTests.Application.Common;

public class PaginationRequestValidatorTests
{
    private readonly PaginationRequestValidator _validator = new();

    [Theory]
    [InlineData(1, 1)]
    [InlineData(1, PaginationRequest.DefaultPageSize)]
    [InlineData(5, PaginationRequest.MaxPageSize)]
    public void Valid_page_and_size_pass(int page, int pageSize)
    {
        var result = _validator.Validate(new PaginationRequest { Page = page, PageSize = pageSize });

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData(0, 20, nameof(PaginationRequest.Page))]
    [InlineData(-1, 20, nameof(PaginationRequest.Page))]
    [InlineData(1, 0, nameof(PaginationRequest.PageSize))]
    [InlineData(1, PaginationRequest.MaxPageSize + 1, nameof(PaginationRequest.PageSize))]
    public void Invalid_page_or_size_fails(int page, int pageSize, string expectedProperty)
    {
        var result = _validator.Validate(new PaginationRequest { Page = page, PageSize = pageSize });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == expectedProperty);
    }
}
