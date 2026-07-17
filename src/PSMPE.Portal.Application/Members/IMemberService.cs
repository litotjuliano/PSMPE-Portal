using PSMPE.Portal.Application.Common.Models;
using PSMPE.Portal.Application.Members.Dtos;
using PSMPE.Portal.Domain.Enums;

namespace PSMPE.Portal.Application.Members;

public interface IMemberService
{
    Task<PagedResult<MemberDto>> GetAllAsync(
        int page, int pageSize, string sortBy, string sortDir, MembershipStatus? status,
        bool? pendingApprovalOnly = null, bool? pendingPrcVerificationOnly = null,
        IReadOnlyCollection<Guid>? excludeUserIds = null, CancellationToken cancellationToken = default);
    Task<MemberDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<MemberDto?> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<bool> MembershipNoExistsAsync(string membershipNo, CancellationToken cancellationToken = default);
    Task<MemberDto> CreateAsync(CreateMemberRequest request, CancellationToken cancellationToken = default);
    Task<Result> UpdateAsync(Guid id, UpdateMemberRequest request, CancellationToken cancellationToken = default);
    Task<Result> ApproveAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Result<MemberDto>> UpsertMyProfileAsync(Guid userId, UpdateMyProfileRequest request, CancellationToken cancellationToken = default);
    Task<Result> SubmitMyProfileAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Result> ApprovePrcVerificationAsync(Guid memberId, Guid decidedByUserId, CancellationToken cancellationToken = default);
    Task<Result> RejectPrcVerificationAsync(Guid memberId, string reason, Guid decidedByUserId, CancellationToken cancellationToken = default);
    Task<ProfileCompletenessDto?> GetProfileCompletenessAsync(Guid userId, CancellationToken cancellationToken = default);
}
