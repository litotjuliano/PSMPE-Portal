using PSMPE.Portal.Application.Common.Models;
using PSMPE.Portal.Application.Events;
using PSMPE.Portal.Application.Events.Dtos;
using PSMPE.Portal.Application.UnitTests.TestSupport;
using PSMPE.Portal.Domain.Entities;
using PSMPE.Portal.Domain.Enums;
using Xunit;

namespace PSMPE.Portal.Application.UnitTests.Events;

public class EventServiceTests
{
    private static CreateEventRequest ValidCreateRequest(string title = "Water Sanitation Workshop") =>
        new(title, "Cross-connection control", Chapters.Ncr, "PICC", DateTimeOffset.UtcNow.AddDays(10),
            DateTimeOffset.UtcNow.AddDays(10).AddHours(4), Capacity: 100, Fee: 500m);

    [Fact]
    public async Task CreateAsync_ValidRequest_StartsWithBothCpdUnitsNull()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new EventService(db);

        var result = await service.CreateAsync(ValidCreateRequest());

        Assert.True(result.Succeeded);
        Assert.Null(result.Value!.CpdUnitsOnsite);
        Assert.Null(result.Value.CpdUnitsOnline);
    }

    [Fact]
    public async Task CreateAsync_CreatesExactlyOneDefaultSessionSpanningTheWholeEvent()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new EventService(db);
        var request = ValidCreateRequest();

        var result = await service.CreateAsync(request);

        var session = Assert.Single(result.Value!.Sessions);
        Assert.Equal(request.StartsAt, session.StartsAt);
        Assert.Equal(request.EndsAt, session.EndsAt);
    }

    [Fact]
    public async Task CreateAsync_EndsAtBeforeStartsAt_Fails()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new EventService(db);
        var starts = DateTimeOffset.UtcNow.AddDays(10);
        var request = ValidCreateRequest() with { StartsAt = starts, EndsAt = starts.AddHours(-1) };

        var result = await service.CreateAsync(request);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task CreateAsync_BlankTitle_Fails()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new EventService(db);

        var result = await service.CreateAsync(ValidCreateRequest() with { Title = "  " });

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task UpdateAsync_SetsOneModalitysUnitsWhileTheOtherStaysTbd()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new EventService(db);
        var created = (await service.CreateAsync(ValidCreateRequest())).Value!;
        var updateRequest = ToUpdateRequest(created) with { CpdUnitsOnsite = 8m, CpdUnitsOnline = null };

        var result = await service.UpdateAsync(created.Id, updateRequest);

        Assert.True(result.Succeeded);
        Assert.Equal(8m, result.Value!.CpdUnitsOnsite);
        Assert.Null(result.Value.CpdUnitsOnline);
    }

    /// <summary>Matches spec.md's "CPD units are set after the event has already happened" -
    /// nothing about Update gates on StartsAt/EndsAt.</summary>
    [Fact]
    public async Task UpdateAsync_EventAlreadyEnded_CanStillSetCpdUnits()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new EventService(db);
        var pastRequest = ValidCreateRequest() with
        {
            StartsAt = DateTimeOffset.UtcNow.AddDays(-10),
            EndsAt = DateTimeOffset.UtcNow.AddDays(-9),
        };
        var created = (await service.CreateAsync(pastRequest)).Value!;
        var updateRequest = ToUpdateRequest(created) with { CpdUnitsOnsite = 8m, CpdUnitsOnline = 4m };

        var result = await service.UpdateAsync(created.Id, updateRequest);

        Assert.True(result.Succeeded);
        Assert.Equal(8m, result.Value!.CpdUnitsOnsite);
    }

    [Fact]
    public async Task UpdateAsync_AddsAndRemovesSessions()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new EventService(db);
        var created = (await service.CreateAsync(ValidCreateRequest())).Value!;
        var defaultSession = created.Sessions.Single();
        var sessions = new List<EventSessionRequest>
        {
            new(defaultSession.Id, "Day 1: Opening", defaultSession.StartsAt, defaultSession.StartsAt.AddHours(2), 1),
            new(null, "Day 1: Cross-Connection Control", defaultSession.StartsAt.AddHours(2), defaultSession.EndsAt, 2),
        };
        var updateRequest = ToUpdateRequest(created) with { Sessions = sessions };

        var result = await service.UpdateAsync(created.Id, updateRequest);

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Value!.Sessions.Count);
        Assert.Contains(result.Value.Sessions, s => s.Title == "Day 1: Cross-Connection Control");
    }

    /// <summary>
    /// A session Id that isn't actually one of this event's sessions - e.g. a stale payload or an
    /// Id copy-pasted from another event - must fail cleanly instead of throwing
    /// InvalidOperationException out of @event.Sessions.First(...).
    /// </summary>
    [Fact]
    public async Task UpdateAsync_SessionIdNotBelongingToEvent_FailsCleanly()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new EventService(db);
        var created = (await service.CreateAsync(ValidCreateRequest())).Value!;
        var updateRequest = ToUpdateRequest(created) with
        {
            Sessions = [new EventSessionRequest(Guid.NewGuid(), "Bogus Session", created.StartsAt, created.EndsAt, 1)],
        };

        var result = await service.UpdateAsync(created.Id, updateRequest);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task UpdateAsync_RemovingSessionWithRecordedAttendance_Fails()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new EventService(db);
        var created = (await service.CreateAsync(ValidCreateRequest())).Value!;
        var sessionId = created.Sessions.Single().Id;
        var member = await SeedMemberForEventTestsAsync(db);
        var registration = new EventRegistration { EventId = created.Id, MemberId = member.Id, Mode = EventMode.Onsite };
        db.EventRegistrations.Add(registration);
        await db.SaveChangesAsync();
        db.EventAttendances.Add(new EventAttendance { EventRegistrationId = registration.Id, EventSessionId = sessionId, RecordedBy = Guid.NewGuid() });
        await db.SaveChangesAsync();
        // Replaces the only session with a brand new one, which would drop the attended session.
        var updateRequest = ToUpdateRequest(created) with
        {
            Sessions = [new EventSessionRequest(null, "Replacement Session", created.StartsAt, created.EndsAt, 1)],
        };

        var result = await service.UpdateAsync(created.Id, updateRequest);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task UpdateAsync_NoSessions_Fails()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new EventService(db);
        var created = (await service.CreateAsync(ValidCreateRequest())).Value!;
        var updateRequest = ToUpdateRequest(created) with { Sessions = [] };

        var result = await service.UpdateAsync(created.Id, updateRequest);

        Assert.False(result.Succeeded);
    }

    /// <summary>
    /// GetAllAsync's RegisteredCount must reflect non-cancelled registrations only - a cancelled
    /// slot must free up capacity in what admins see, matching the "one non-cancelled registration
    /// per member per event" rule the registration flow enforces (Task 5).
    /// </summary>
    [Fact]
    public async Task GetAllAsync_RegisteredCount_ExcludesCancelledRegistrations()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new EventService(db);
        var created = (await service.CreateAsync(ValidCreateRequest())).Value!;
        var member = await SeedMemberForEventTestsAsync(db);
        db.EventRegistrations.Add(new EventRegistration
        {
            EventId = created.Id, MemberId = member.Id, Mode = EventMode.Onsite,
            Status = EventRegistrationStatus.Cancelled,
        });
        await db.SaveChangesAsync();

        var page = await service.GetAllAsync(1, 20, search: null, chapter: null, upcomingOnly: false);

        Assert.Equal(0, page.Items.Single().RegisteredCount);
    }

    [Fact]
    public async Task GetAllAsync_SearchFiltersByTitle()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new EventService(db);
        await service.CreateAsync(ValidCreateRequest("National Convention"));
        await service.CreateAsync(ValidCreateRequest("Plumbing Code Seminar"));

        var page = await service.GetAllAsync(1, 20, search: "seminar", chapter: null, upcomingOnly: false);

        Assert.Equal(["Plumbing Code Seminar"], page.Items.Select(e => e.Title));
    }

    private static UpdateEventRequest ToUpdateRequest(EventDto e) =>
        new(e.Title, e.Description, e.Chapter, e.Venue, e.StartsAt, e.EndsAt, e.Capacity, e.Fee,
            e.CpdUnitsOnsite, e.CpdUnitsOnline,
            e.Sessions.Select(s => new EventSessionRequest(s.Id, s.Title, s.StartsAt, s.EndsAt, s.Order)).ToList());

    [Fact]
    public async Task RegisterAsync_ValidMode_CreatesRegistrationInRegisteredStatus()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new EventService(db);
        var @event = (await service.CreateAsync(ValidCreateRequest())).Value!;
        var member = await SeedMemberForEventTestsAsync(db);

        var result = await service.RegisterAsync(member.UserId, @event.Id, "Online");

        Assert.True(result.Succeeded);
        Assert.Equal("Registered", result.Value!.Status);
        Assert.Equal("Online", result.Value.Mode);
    }

    [Fact]
    public async Task RegisterAsync_UnrecognizedMode_Fails()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new EventService(db);
        var @event = (await service.CreateAsync(ValidCreateRequest())).Value!;
        var member = await SeedMemberForEventTestsAsync(db);

        var result = await service.RegisterAsync(member.UserId, @event.Id, "InPerson");

        Assert.False(result.Succeeded);
    }

    /// <summary>Matches spec.md's "A member cannot register twice for the same event" - even under
    /// a different Mode.</summary>
    [Fact]
    public async Task RegisterAsync_Twice_SecondCallFailsEvenUnderADifferentMode()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new EventService(db);
        var @event = (await service.CreateAsync(ValidCreateRequest())).Value!;
        var member = await SeedMemberForEventTestsAsync(db);
        await service.RegisterAsync(member.UserId, @event.Id, "Onsite");

        var result = await service.RegisterAsync(member.UserId, @event.Id, "Online");

        Assert.False(result.Succeeded);
    }

    /// <summary>A cancelled registration frees the member to register again - this is what makes
    /// Cancelled a real off-ramp rather than a dead end.</summary>
    [Fact]
    public async Task RegisterAsync_AfterCancelling_Succeeds()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new EventService(db);
        var @event = (await service.CreateAsync(ValidCreateRequest())).Value!;
        var member = await SeedMemberForEventTestsAsync(db);
        var first = await service.RegisterAsync(member.UserId, @event.Id, "Onsite");
        await service.CancelRegistrationAsync(member.UserId, first.Value!.Id);

        var result = await service.RegisterAsync(member.UserId, @event.Id, "Onsite");

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task CancelRegistrationAsync_NotOwner_Forbidden()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new EventService(db);
        var @event = (await service.CreateAsync(ValidCreateRequest())).Value!;
        var member = await SeedMemberForEventTestsAsync(db);
        var registration = (await service.RegisterAsync(member.UserId, @event.Id, "Onsite")).Value!;
        var otherUserId = Guid.NewGuid();

        var result = await service.CancelRegistrationAsync(otherUserId, registration.Id);

        Assert.False(result.Succeeded);
        Assert.Equal(ResultErrorType.Forbidden, result.ErrorType);
    }

    [Fact]
    public async Task CancelRegistrationAsync_AfterPaymentVerified_Fails()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new EventService(db);
        var @event = (await service.CreateAsync(ValidCreateRequest())).Value!;
        var member = await SeedMemberForEventTestsAsync(db);
        var registration = (await service.RegisterAsync(member.UserId, @event.Id, "Onsite")).Value!;
        var entity = await db.EventRegistrations.FindAsync(registration.Id);
        entity!.Status = EventRegistrationStatus.PaymentVerified;
        await db.SaveChangesAsync();

        var result = await service.CancelRegistrationAsync(member.UserId, registration.Id);

        Assert.False(result.Succeeded);
    }

    internal static async Task<Member> SeedMemberForEventTestsAsync(TestDbContext db, string? email = null)
    {
        email ??= $"{Guid.NewGuid()}@example.com";
        var user = new ApplicationUser { UserName = email, Email = email };
        db.Add(user);
        var member = new Member { UserId = user.Id, User = user, FirstName = "Juan", LastName = "Dela Cruz", Chapter = Chapters.Ncr, MemberType = MemberTypes.Regular };
        db.Members.Add(member);
        await db.SaveChangesAsync();
        return member;
    }

    [Fact]
    public async Task RecordAttendanceAsync_BeforePaymentVerified_Fails()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new EventService(db);
        var @event = (await service.CreateAsync(ValidCreateRequest())).Value!;
        var member = await SeedMemberForEventTestsAsync(db);
        var registration = (await service.RegisterAsync(member.UserId, @event.Id, "Onsite")).Value!;
        var sessionId = @event.Sessions.Single().Id;

        var result = await service.RecordAttendanceAsync(
            @event.Id, [new RegistrantAttendanceRequest(registration.Id, [sessionId])], Guid.NewGuid());

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task RecordAttendanceAsync_RecordsSessions_MovesRegistrationToAttended()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new EventService(db);
        var @event = (await service.CreateAsync(ValidCreateRequest())).Value!;
        var member = await SeedMemberForEventTestsAsync(db);
        var registration = (await service.RegisterAsync(member.UserId, @event.Id, "Onsite")).Value!;
        await MarkPaymentVerifiedAsync(db, registration.Id);
        var sessionId = @event.Sessions.Single().Id;
        var adminUserId = Guid.NewGuid();

        var result = await service.RecordAttendanceAsync(
            @event.Id, [new RegistrantAttendanceRequest(registration.Id, [sessionId])], adminUserId);

        Assert.True(result.Succeeded);
        var updated = await db.EventRegistrations.FindAsync(registration.Id);
        Assert.Equal(EventRegistrationStatus.Attended, updated!.Status);
        var attendance = Assert.Single(db.EventAttendances.Where(a => a.EventRegistrationId == registration.Id));
        Assert.Equal(adminUserId, attendance.RecordedBy);
    }

    /// <summary>Matches spec.md's "A member attends only part of a multi-session event": 3 of 6
    /// sessions produces exactly 3 EventAttendance rows.</summary>
    [Fact]
    public async Task RecordAttendanceAsync_PartialAttendance_RecordsExactlyThatManySessions()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new EventService(db);
        var created = (await service.CreateAsync(ValidCreateRequest())).Value!;
        var sixSessions = Enumerable.Range(1, 6)
            .Select(i => new EventSessionRequest(i == 1 ? created.Sessions[0].Id : null, $"Lecture {i}", created.StartsAt.AddHours(i), created.StartsAt.AddHours(i + 1), i))
            .ToList();
        var @event = (await service.UpdateAsync(created.Id, ToUpdateRequest(created) with { Sessions = sixSessions })).Value!;
        var member = await SeedMemberForEventTestsAsync(db);
        var registration = (await service.RegisterAsync(member.UserId, @event.Id, "Onsite")).Value!;
        await MarkPaymentVerifiedAsync(db, registration.Id);
        var attendedSessionIds = @event.Sessions.Take(3).Select(s => s.Id).ToList();

        var result = await service.RecordAttendanceAsync(
            @event.Id, [new RegistrantAttendanceRequest(registration.Id, attendedSessionIds)], Guid.NewGuid());

        Assert.True(result.Succeeded);
        Assert.Equal(3, db.EventAttendances.Count(a => a.EventRegistrationId == registration.Id));
        var updated = await db.EventRegistrations.FindAsync(registration.Id);
        Assert.Equal(EventRegistrationStatus.Attended, updated!.Status);
    }

    [Fact]
    public async Task RecordAttendanceAsync_SessionFromDifferentEvent_Fails()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new EventService(db);
        var @event = (await service.CreateAsync(ValidCreateRequest())).Value!;
        var otherEvent = (await service.CreateAsync(ValidCreateRequest("Other Event"))).Value!;
        var member = await SeedMemberForEventTestsAsync(db);
        var registration = (await service.RegisterAsync(member.UserId, @event.Id, "Onsite")).Value!;
        await MarkPaymentVerifiedAsync(db, registration.Id);
        var otherEventSessionId = otherEvent.Sessions.Single().Id;

        var result = await service.RecordAttendanceAsync(
            @event.Id, [new RegistrantAttendanceRequest(registration.Id, [otherEventSessionId])], Guid.NewGuid());

        Assert.False(result.Succeeded);
        Assert.Empty(db.EventAttendances.Where(a => a.EventRegistrationId == registration.Id));
    }

    [Fact]
    public async Task RecordAttendanceAsync_CalledAgainWithCorrectedSet_ReplacesPreviousRows()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new EventService(db);
        var created = (await service.CreateAsync(ValidCreateRequest())).Value!;
        var twoSessions = new List<EventSessionRequest>
        {
            new(created.Sessions[0].Id, "Lecture 1", created.StartsAt, created.StartsAt.AddHours(1), 1),
            new(null, "Lecture 2", created.StartsAt.AddHours(1), created.EndsAt, 2),
        };
        var @event = (await service.UpdateAsync(created.Id, ToUpdateRequest(created) with { Sessions = twoSessions })).Value!;
        var member = await SeedMemberForEventTestsAsync(db);
        var registration = (await service.RegisterAsync(member.UserId, @event.Id, "Onsite")).Value!;
        await MarkPaymentVerifiedAsync(db, registration.Id);
        await service.RecordAttendanceAsync(@event.Id, [new RegistrantAttendanceRequest(registration.Id, [@event.Sessions[0].Id])], Guid.NewGuid());

        var result = await service.RecordAttendanceAsync(
            @event.Id, [new RegistrantAttendanceRequest(registration.Id, [@event.Sessions[0].Id, @event.Sessions[1].Id])], Guid.NewGuid());

        Assert.True(result.Succeeded);
        Assert.Equal(2, db.EventAttendances.Count(a => a.EventRegistrationId == registration.Id));
    }

    private static async Task MarkPaymentVerifiedAsync(TestDbContext db, Guid registrationId)
    {
        var registration = await db.EventRegistrations.FindAsync(registrationId);
        registration!.Status = EventRegistrationStatus.PaymentVerified;
        await db.SaveChangesAsync();
    }
}
