using PSMPE.Portal.Application.Common.Models;
using PSMPE.Portal.Application.Members.Dtos;
using PSMPE.Portal.Domain.Enums;

namespace PSMPE.Portal.Application.Members;

public interface IMemberService
{
    Task<PagedResult<MemberDto>> GetAllAsync(
        int page, int pageSize, string sortBy, string sortDir, MembershipStatus? status,
        bool? pendingApprovalOnly = null, bool? pendingPrcVerificationOnly = null, string? search = null,
        IReadOnlyCollection<Guid>? excludeUserIds = null, CancellationToken cancellationToken = default);
    Task<MemberDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<MemberDto?> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<bool> MembershipNoExistsAsync(string membershipNo, Guid? excludeMemberId = null, CancellationToken cancellationToken = default);
    Task<Result<MemberDto>> CreateAsync(CreateMemberRequest request, CancellationToken cancellationToken = default);
    Task<Result> UpdateAsync(Guid id, UpdateMemberRequest request, CancellationToken cancellationToken = default);
    /// <summary>Assigns PSMPE's membership control number and approves. The number is mandatory -
    /// nothing else in the product sets it.</summary>
    Task<Result> ApproveAsync(Guid id, ApproveMemberRequest request, Guid decidedByUserId, CancellationToken cancellationToken = default);
    Task<Result<MemberDto>> UpsertMyProfileAsync(Guid userId, UpdateMyProfileRequest request, CancellationToken cancellationToken = default);
    Task<Result> SubmitMyProfileAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    /// <summary>Used by AdminController.DeleteUser before deleting the login account - the Member
    /// row cascades away with it, which would throw a raw DbUpdateException if PRC verification
    /// history exists (same Restrict FK DeleteAsync above already guards against by Member.Id).</summary>
    Task<bool> HasPrcVerificationHistoryAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<Result> ApprovePrcVerificationAsync(Guid memberId, Guid decidedByUserId, CancellationToken cancellationToken = default);
    Task<Result> RejectPrcVerificationAsync(Guid memberId, string reason, Guid decidedByUserId, CancellationToken cancellationToken = default);
    Task<ProfileCompletenessDto?> GetProfileCompletenessAsync(Guid userId, CancellationToken cancellationToken = default);
}
