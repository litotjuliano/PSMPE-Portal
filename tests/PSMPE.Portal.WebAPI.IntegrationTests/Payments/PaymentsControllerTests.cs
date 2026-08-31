using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using PSMPE.Portal.Application.Members;
using PSMPE.Portal.Domain.Entities;
using PSMPE.Portal.Domain.Enums;
using PSMPE.Portal.Infrastructure.Persistence;
using PSMPE.Portal.WebAPI.Controllers;
using Xunit;
using IPaymentServiceType = PSMPE.Portal.Application.Payments.IPaymentService;

namespace PSMPE.Portal.WebAPI.IntegrationTests.Payments;

/// <summary>
/// Exercises PaymentsController.GetProof directly, same convention as MembersControllerTests -
/// bypasses the HTTP/auth pipeline via a fake ControllerContext, real services from
/// CustomWebApplicationFactory's InMemory database.
/// </summary>
public class PaymentsControllerTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly IServiceScope _scope;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IMemberService _memberService;
    private readonly IMemberUploadService _memberUploadService;
    private readonly IPaymentServiceType _paymentService;

    public PaymentsControllerTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _scope = factory.Services.CreateScope();
        _userManager = _scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        _memberService = _scope.ServiceProvider.GetRequiredService<IMemberService>();
        _memberUploadService = _scope.ServiceProvider.GetRequiredService<IMemberUploadService>();
        _paymentService = _scope.ServiceProvider.GetRequiredService<IPaymentServiceType>();
    }

    public Task InitializeAsync() => _factory.InitializeAsync();

    public Task DisposeAsync()
    {
        _scope.Dispose();
        return Task.CompletedTask;
    }

    private PaymentsController CreateController(Guid callerId)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, callerId.ToString()) };
        var httpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth")) };
        return new PaymentsController(_paymentService, _memberService, _memberUploadService)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };
    }

    private async Task<Member> SeedApprovedMemberAsync()
    {
        var user = new ApplicationUser { UserName = $"{Guid.NewGuid()}@example.com", Email = $"{Guid.NewGuid()}@example.com" };
        await _userManager.CreateAsync(user, "Password123!");
        await _userManager.AddToRoleAsync(user, RoleNames.Member);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var member = new Member
        {
            UserId = user.Id,
            FirstName = "Juan",
            LastName = "Dela Cruz",
            Chapter = Chapters.Ncr,
            MemberType = MemberTypes.Regular,
            Status = MembershipStatus.Active,
        };
        db.Members.Add(member);
        await db.SaveChangesAsync();
        return member;
    }

    /// <summary>proofStorageKey mirrors MembersControllerTests.ApproveWithPayment's own pattern of
    /// pointing a Payment at a key that was never actually written to disk - the exact "recorded
    /// but missing from storage" shape this test is verifying GetProof handles distinctly.</summary>
    private async Task<Payment> SeedPaymentAsync(Member member, string? proofStorageKey)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var payment = new Payment
        {
            MemberId = member.Id,
            Kind = PaymentKind.Renewal,
            Status = PaymentStatus.Submitted,
            Amount = 600m,
            PaidOn = DateOnly.FromDateTime(DateTime.UtcNow),
            ProofStorageKey = proofStorageKey,
        };
        db.Payments.Add(payment);
        await db.SaveChangesAsync();
        return payment;
    }

    [Fact]
    public async Task GetProof_NeverSubmitted_ReturnsNotFound()
    {
        var member = await SeedApprovedMemberAsync();
        var payment = await SeedPaymentAsync(member, proofStorageKey: null);
        var controller = CreateController(member.UserId);

        var result = await controller.GetProof(payment.Id, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task GetProof_RecordedButFileMissingFromStorage_ReturnsGone()
    {
        var member = await SeedApprovedMemberAsync();
        // No file is ever written for this key via IFileStorageService - simulating a proof that
        // was recorded on the Payment row but whose physical file is no longer on disk.
        var payment = await SeedPaymentAsync(member, proofStorageKey: "test/never-actually-written.jpg");
        var controller = CreateController(member.UserId);

        var result = await controller.GetProof(payment.Id, CancellationToken.None);

        var statusResult = Assert.IsType<StatusCodeResult>(result);
        Assert.Equal(StatusCodes.Status410Gone, statusResult.StatusCode);
    }

    [Fact]
    public async Task GetProof_FileActuallyOnDisk_ReturnsIt()
    {
        var member = await SeedApprovedMemberAsync();
        // .pdf rather than .jpg - ProofOfPayment accepts PDFs and they're stored raw (no image
        // decode/validation), so arbitrary bytes are enough to exercise the "file really is there"
        // path without needing a real JPEG.
        await using var content = new MemoryStream([1, 2, 3, 4]);
        var stored = await _memberUploadService.UploadPaymentProofAsync(member.UserId, content, "proof.pdf", content.Length);
        Assert.True(stored.Succeeded, stored.Error);
        var payment = await SeedPaymentAsync(member, proofStorageKey: stored.Value);
        var controller = CreateController(member.UserId);

        var result = await controller.GetProof(payment.Id, CancellationToken.None);

        Assert.IsType<FileStreamResult>(result);
    }
}
