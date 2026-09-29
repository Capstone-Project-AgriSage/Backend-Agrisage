using AgriSage.Application.Common.Models;

namespace AgriSage.UnitTests.Application.Common;

public class PagedResultTests
{
    [Theory]
    [InlineData(0, 20, 0)]
    [InlineData(1, 20, 1)]
    [InlineData(20, 20, 1)]
    [InlineData(21, 20, 2)]
    [InlineData(250, 100, 3)]
    public void TotalPages_rounds_up(long totalCount, int pageSize, int expectedTotalPages)
    {
        var result = new PagedResult<int>([], Page: 1, pageSize, totalCount);

        Assert.Equal(expectedTotalPages, result.TotalPages);
    }

    [Fact]
    public void PaginationRequest_defaults_to_first_page_and_default_size()
    {
        var request = new PaginationRequest();

        Assert.Equal(1, request.Page);
        Assert.Equal(PaginationRequest.DefaultPageSize, request.PageSize);
        Assert.Equal(0, request.Skip);
    }

    [Fact]
    public void PaginationRequest_skip_is_offset_of_page()
    {
        var request = new PaginationRequest { Page = 3, PageSize = 25 };

        Assert.Equal(50, request.Skip);
    }
}
