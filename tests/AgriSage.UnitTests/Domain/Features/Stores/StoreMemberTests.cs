using AgriSage.Domain.Common.Exceptions;
using AgriSage.Domain.Features.Stores.Entities;
using AgriSage.Domain.Features.Stores.Enums;

namespace AgriSage.UnitTests.Domain.Features.Stores;

public class StoreMemberTests
{
    private static readonly DateOnly JoinedAt = new(2026, 1, 1);

    private static StoreMember CreateMember() =>
        new(Guid.NewGuid(), Guid.NewGuid(), "EMP-01", JoinedAt);

    [Fact]
    public void Leave_marks_member_as_left_with_date()
    {
        var member = CreateMember();

        member.Leave(new DateOnly(2026, 6, 30));

        Assert.Equal(StoreMemberStatus.Left, member.Status);
        Assert.Equal(new DateOnly(2026, 6, 30), member.LeftAt);
    }

    [Fact]
    public void Leave_before_join_date_throws()
    {
        var member = CreateMember();

        Assert.Throws<DomainException>(() => member.Leave(JoinedAt.AddDays(-1)));
        Assert.Equal(StoreMemberStatus.Active, member.Status);
    }

    [Fact]
    public void Leave_twice_throws()
    {
        var member = CreateMember();
        member.Leave(new DateOnly(2026, 6, 30));

        Assert.Throws<DomainException>(() => member.Leave(new DateOnly(2026, 7, 1)));
    }

    [Fact]
    public void Activate_after_leaving_reuses_same_membership_record()
    {
        var member = CreateMember();
        var originalId = member.Id;
        member.Leave(new DateOnly(2026, 6, 30));

        member.Activate();

        Assert.Equal(originalId, member.Id);
        Assert.Equal(StoreMemberStatus.Active, member.Status);
        Assert.Null(member.LeftAt);
    }

    [Fact]
    public void Deactivate_after_leaving_throws()
    {
        var member = CreateMember();
        member.Leave(new DateOnly(2026, 6, 30));

        Assert.Throws<DomainException>(member.Deactivate);
    }

    [Fact]
    public void Ai_review_permission_can_be_granted_and_revoked()
    {
        var member = CreateMember();

        member.GrantAiReview();
        Assert.True(member.CanReviewAi);

        member.RevokeAiReview();
        Assert.False(member.CanReviewAi);
    }
}
