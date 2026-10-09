using AgriSage.Api.Extensions;
using AgriSage.Application.Common.Models;
using AgriSage.Application.Features.Audit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgriSage.Api.Features.Audit;

[ApiController, Route("api/audit-logs"), Authorize(Roles = ApiRoles.Manage)]
public sealed class AuditLogsController(IAuditLogService audit) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<AuditLogResponse>> List([FromQuery] AuditLogListRequest request, CancellationToken token) => audit.ListAsync(request, token);
    [HttpGet("{id:guid}")]
    public Task<AuditLogResponse> Get(Guid id, CancellationToken token) => audit.GetAsync(id, token);
}
