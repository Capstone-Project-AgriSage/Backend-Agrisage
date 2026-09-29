using AgriSage.Domain.Features.Content.Entities;
using AgriSage.Domain.Features.Identity.Entities;
using AgriSage.Infrastructure.Persistence.Configurations.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgriSage.Infrastructure.Persistence.Configurations.Content;

// Table 65: contact_requests. user_id may be NULL (submitted before login).
internal sealed class ContactRequestConfiguration : EntityConfiguration<ContactRequest>
{
    protected override string TableName => "contact_requests";

    protected override void ConfigureEntity(EntityTypeBuilder<ContactRequest> builder)
    {
        builder.Property(request => request.RequestNumber).HasMaxLength(50);
        builder.Property(request => request.ContactName).HasMaxLength(150);
        builder.Property(request => request.ContactPhone).HasMaxLength(20);
        builder.Property(request => request.ContactEmail).HasMaxLength(255);
        builder.Property(request => request.Subject).HasMaxLength(255);
        builder.Property(request => request.Message).IsText();
        builder.Property(request => request.Status).HasMaxLength(20);
        builder.Property(request => request.ResolutionNote).HasMaxLength(2000);

        builder.HasReference<ContactRequest, User>(request => request.UserId);
        builder.HasUserReference(request => request.AssignedTo);

        builder.HasIndex(request => request.RequestNumber).IsUnique();
        builder.HasIndex(request => new { request.Status, request.CreatedAt }).IsDescending(false, true);
        builder.HasIndex(request => new { request.AssignedTo, request.Status });
    }
}
