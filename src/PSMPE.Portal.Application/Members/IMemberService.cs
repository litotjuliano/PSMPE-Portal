using PSMPE.Portal.Application.Common.Models;
using PSMPE.Portal.Application.Members.Dtos;

namespace PSMPE.Portal.Application.Members;

public interface IMemberService
{
    Task<PagedResult<MemberDto>> GetAllAsync(
        int page, int pageSize, string sortBy, string sortDir, CancellationToken cancellationToken = default);
    Task<MemberDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<MemberDto?> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<bool> MembershipNoExistsAsync(string membershipNo, CancellationToken cancellationToken = default);
    Task<MemberDto> CreateAsync(CreateMemberRequest request, CancellationToken cancellationToken = default);
    Task<Result> UpdateAsync(Guid id, UpdateMemberRequest request, CancellationToken cancellationToken = default);
    Task<MemberDto> UpsertMyProfileAsync(Guid userId, UpdateMyProfileRequest request, CancellationToken cancellationToken = default);
    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
