using Microsoft.EntityFrameworkCore;
using PSMPE.Portal.Application.Common.Interfaces;
using PSMPE.Portal.Application.Common.Models;
using PSMPE.Portal.Application.Members.Dtos;
using PSMPE.Portal.Domain.Entities;
using PSMPE.Portal.Domain.Enums;

namespace PSMPE.Portal.Application.Members;

public class MemberService(IApplicationDbContext db) : IMemberService
{
    private static MemberDto ToDto(Member m) => new(
        m.Id, m.UserId, m.User.Email ?? string.Empty, m.FirstName, m.MiddleName, m.LastName, m.Suffix,
        m.Birthdate, m.Gender, m.Address, m.MembershipNo, m.PrcLicenseNo, m.Chapter, m.Company,
        m.Status, m.RenewalDueDate, m.NationalDuesReferenceNo, m.PhotoUrl, m.PrcIdUrl, m.CreatedAt, m.UpdatedAt);

    public async Task<PagedResult<MemberDto>> GetAllAsync(
        int page, int pageSize, string sortBy, string sortDir, CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        IQueryable<Member> query = db.Members.AsNoTracking().Include(m => m.User);

        var descending = string.Equals(sortDir, "desc", StringComparison.OrdinalIgnoreCase);
        query = sortBy.ToLowerInvariant() switch
        {
            "membershipno" => descending ? query.OrderByDescending(m => m.MembershipNo) : query.OrderBy(m => m.MembershipNo),
            "chapter" => descending ? query.OrderByDescending(m => m.Chapter) : query.OrderBy(m => m.Chapter),
            "status" => descending ? query.OrderByDescending(m => m.Status) : query.OrderBy(m => m.Status),
            _ => descending ? query.OrderByDescending(m => m.LastName) : query.OrderBy(m => m.LastName),
        };

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);

        return new PagedResult<MemberDto>(items.Select(ToDto).ToList(), totalCount, page, pageSize);
    }

    public async Task<MemberDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var member = await db.Members.AsNoTracking().Include(m => m.User).FirstOrDefaultAsync(m => m.Id == id, cancellationToken);
        return member is null ? null : ToDto(member);
    }

    public async Task<MemberDto?> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var member = await db.Members.AsNoTracking().Include(m => m.User).FirstOrDefaultAsync(m => m.UserId == userId, cancellationToken);
        return member is null ? null : ToDto(member);
    }

    public Task<bool> MembershipNoExistsAsync(string membershipNo, CancellationToken cancellationToken = default) =>
        db.Members.AsNoTracking().AnyAsync(m => m.MembershipNo == membershipNo, cancellationToken);

    public async Task<MemberDto> CreateAsync(CreateMemberRequest request, CancellationToken cancellationToken = default)
    {
        var member = new Member
        {
            UserId = request.UserId,
            MembershipNo = request.MembershipNo,
            FirstName = request.FirstName,
            MiddleName = request.MiddleName,
            LastName = request.LastName,
            Suffix = request.Suffix,
            Birthdate = request.Birthdate,
            Gender = request.Gender,
            Address = request.Address,
            PrcLicenseNo = request.PrcLicenseNo,
            Chapter = request.Chapter,
            Company = request.Company,
            Status = MembershipStatus.Pending,
            RenewalDueDate = request.RenewalDueDate,
            NationalDuesReferenceNo = request.NationalDuesReferenceNo
        };

        db.Members.Add(member);
        await db.SaveChangesAsync(cancellationToken);
        return await GetByIdAsync(member.Id, cancellationToken) ?? throw new InvalidOperationException("Member was not persisted.");
    }

    public async Task<Result> UpdateAsync(Guid id, UpdateMemberRequest request, CancellationToken cancellationToken = default)
    {
        var member = await db.Members.FirstOrDefaultAsync(m => m.Id == id, cancellationToken);
        if (member is null)
        {
            return Result.NotFound($"Member '{id}' was not found.");
        }

        member.FirstName = request.FirstName;
        member.MiddleName = request.MiddleName;
        member.LastName = request.LastName;
        member.Suffix = request.Suffix;
        member.Birthdate = request.Birthdate;
        member.Gender = request.Gender;
        member.Address = request.Address;
        member.PrcLicenseNo = request.PrcLicenseNo;
        member.Chapter = request.Chapter;
        member.Company = request.Company;
        member.Status = request.Status;
        member.RenewalDueDate = request.RenewalDueDate;
        member.NationalDuesReferenceNo = request.NationalDuesReferenceNo;
        member.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<MemberDto> UpsertMyProfileAsync(Guid userId, UpdateMyProfileRequest request, CancellationToken cancellationToken = default)
    {
        var member = await db.Members.FirstOrDefaultAsync(m => m.UserId == userId, cancellationToken);
        if (member is null)
        {
            member = new Member
            {
                UserId = userId,
                MembershipNo = await GenerateMembershipNoAsync(cancellationToken),
                Status = MembershipStatus.Pending
            };
            db.Members.Add(member);
        }
        else
        {
            member.UpdatedAt = DateTimeOffset.UtcNow;
        }

        member.FirstName = request.FirstName;
        member.MiddleName = request.MiddleName;
        member.LastName = request.LastName;
        member.Suffix = request.Suffix;
        member.Birthdate = request.Birthdate;
        member.Gender = request.Gender;
        member.Address = request.Address;
        member.PrcLicenseNo = request.PrcLicenseNo;
        member.Chapter = request.Chapter;
        member.Company = request.Company;

        await db.SaveChangesAsync(cancellationToken);
        return await GetByIdAsync(member.Id, cancellationToken) ?? throw new InvalidOperationException("Member was not persisted.");
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var member = await db.Members.FirstOrDefaultAsync(m => m.Id == id, cancellationToken);
        if (member is null)
        {
            return Result.NotFound($"Member '{id}' was not found.");
        }

        db.Members.Remove(member);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    /// <summary>
    /// Simple sequential scheme, zero-padded to 6 digits. Not perfectly race-safe under concurrent
    /// first-time self-registrations, but the unique index on MembershipNo guarantees no duplicate
    /// ever gets persisted - a rare collision just surfaces as a SaveChanges failure to retry.
    /// </summary>
    private async Task<string> GenerateMembershipNoAsync(CancellationToken cancellationToken)
    {
        var count = await db.Members.CountAsync(cancellationToken);
        return (count + 1).ToString("D6");
    }
}
