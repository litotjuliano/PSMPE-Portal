using PSMPE.Portal.Application.Common.Models;
using PSMPE.Portal.Application.Events;
using PSMPE.Portal.Application.Events.Dtos;
using PSMPE.Portal.Application.Payments;
using PSMPE.Portal.Application.Payments.Dtos;
using PSMPE.Portal.Application.UnitTests.TestSupport;
using PSMPE.Portal.Domain.Entities;
using PSMPE.Portal.Domain.Enums;
using Xunit;

namespace PSMPE.Portal.Application.UnitTests.Events;

public class EventServiceTests
{
    private static CreateEventRequest ValidCreateRequest(string title = "Water Sanitation Workshop") =>
        new(title, "Cross-connection control", Chapters.Ncr, "PICC", DateTimeOffset.UtcNow.AddDays(10),
            DateTimeOffset.UtcNow.AddDays(10).AddHours(4), Capacity: 100, FeeOnsite: 500m, FeeOnline: 200m);

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
        new(e.Title, e.Description, e.Chapter, e.Venue, e.StartsAt, e.EndsAt, e.Capacity, e.FeeOnsite, e.FeeOnline,
            e.CpdUnitsOnsite, e.CpdUnitsOnline,
            e.Sessions.Select(s => new EventSessionRequest(s.Id, s.Title, s.StartsAt, s.EndsAt, s.Order, s.Venue)).ToList());

    [Fact]
    public async Task CreateAsync_UnrecognizedType_Fails()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new EventService(db);

        var result = await service.CreateAsync(ValidCreateRequest() with { Type = "Not A Real Type" });

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task CreateAsync_RecognizedType_Succeeds()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new EventService(db);

        var result = await service.CreateAsync(ValidCreateRequest() with { Type = EventTypes.Seminar });

        Assert.True(result.Succeeded);
        Assert.Equal(EventTypes.Seminar, result.Value!.Type);
    }

    [Fact]
    public async Task UpdateAsync_SessionVenueOverride_PersistsAndFallsBackWhenCleared()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new EventService(db);
        var created = (await service.CreateAsync(ValidCreateRequest() with { Venue = "PICC" })).Value!;
        var defaultSession = created.Sessions.Single();
        var withOverride = ToUpdateRequest(created) with
        {
            Sessions = [new EventSessionRequest(defaultSession.Id, defaultSession.Title, defaultSession.StartsAt, defaultSession.EndsAt, 1, "Cebu IT Park")],
        };

        var overridden = (await service.UpdateAsync(created.Id, withOverride)).Value!;
        Assert.Equal("Cebu IT Park", overridden.Sessions.Single().Venue);

        var cleared = ToUpdateRequest(overridden) with
        {
            Sessions = [new EventSessionRequest(defaultSession.Id, defaultSession.Title, defaultSession.StartsAt, defaultSession.EndsAt, 1, null)],
        };
        var result = await service.UpdateAsync(created.Id, cleared);

        Assert.Null(result.Value!.Sessions.Single().Venue);
    }

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

    /// <summary>Guards the batched-and-grouped attendance lookup in RecordAttendanceAsync: with two
    /// registrants each having pre-existing attendance rows, a single call replacing both sets must
    /// isolate each registrant's rows correctly rather than cross-contaminating them.</summary>
    [Fact]
    public async Task RecordAttendanceAsync_MultipleRegistrantsWithExistingRows_ReplacesEachIndependently()
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
        var member1 = await SeedMemberForEventTestsAsync(db);
        var member2 = await SeedMemberForEventTestsAsync(db);
        var registration1 = (await service.RegisterAsync(member1.UserId, @event.Id, "Onsite")).Value!;
        var registration2 = (await service.RegisterAsync(member2.UserId, @event.Id, "Onsite")).Value!;
        await MarkPaymentVerifiedAsync(db, registration1.Id);
        await MarkPaymentVerifiedAsync(db, registration2.Id);
        await service.RecordAttendanceAsync(@event.Id,
            [
                new RegistrantAttendanceRequest(registration1.Id, [@event.Sessions[0].Id]),
                new RegistrantAttendanceRequest(registration2.Id, [@event.Sessions[0].Id]),
            ], Guid.NewGuid());

        var result = await service.RecordAttendanceAsync(@event.Id,
            [
                new RegistrantAttendanceRequest(registration1.Id, [@event.Sessions[1].Id]),
                new RegistrantAttendanceRequest(registration2.Id, [@event.Sessions[0].Id, @event.Sessions[1].Id]),
            ], Guid.NewGuid());

        Assert.True(result.Succeeded);
        var registration1Sessions = db.EventAttendances.Where(a => a.EventRegistrationId == registration1.Id).Select(a => a.EventSessionId).ToList();
        var registration2Sessions = db.EventAttendances.Where(a => a.EventRegistrationId == registration2.Id).Select(a => a.EventSessionId).ToList();
        Assert.Equal([@event.Sessions[1].Id], registration1Sessions);
        Assert.Equal(2, registration2Sessions.Count);
        Assert.Contains(@event.Sessions[0].Id, registration2Sessions);
        Assert.Contains(@event.Sessions[1].Id, registration2Sessions);
    }

    private static async Task MarkPaymentVerifiedAsync(TestDbContext db, Guid registrationId)
    {
        var registration = await db.EventRegistrations.FindAsync(registrationId);
        registration!.Status = EventRegistrationStatus.PaymentVerified;
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task SubmitEvaluationAsync_BeforeAttended_Fails()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new EventService(db);
        var @event = (await service.CreateAsync(ValidCreateRequest())).Value!;
        var member = await SeedMemberForEventTestsAsync(db);
        var registration = (await service.RegisterAsync(member.UserId, @event.Id, "Onsite")).Value!;

        var result = await service.SubmitEvaluationAsync(member.UserId, registration.Id, rating: 5, comments: "Great");

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task SubmitEvaluationAsync_AfterAttended_MovesToEvaluationSubmitted()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new EventService(db);
        var @event = (await service.CreateAsync(ValidCreateRequest())).Value!;
        var member = await SeedMemberForEventTestsAsync(db);
        var registration = (await service.RegisterAsync(member.UserId, @event.Id, "Onsite")).Value!;
        await MarkAttendedAsync(db, registration.Id);

        var result = await service.SubmitEvaluationAsync(member.UserId, registration.Id, rating: 4, comments: "Good session");

        Assert.True(result.Succeeded);
        var updated = await db.EventRegistrations.FindAsync(registration.Id);
        Assert.Equal(EventRegistrationStatus.EvaluationSubmitted, updated!.Status);
        Assert.NotNull(updated.EvaluationSubmittedAt);
    }

    [Fact]
    public async Task SubmitEvaluationAsync_NotOwner_Forbidden()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new EventService(db);
        var @event = (await service.CreateAsync(ValidCreateRequest())).Value!;
        var member = await SeedMemberForEventTestsAsync(db);
        var registration = (await service.RegisterAsync(member.UserId, @event.Id, "Onsite")).Value!;
        await MarkAttendedAsync(db, registration.Id);

        var result = await service.SubmitEvaluationAsync(Guid.NewGuid(), registration.Id, rating: 4, comments: null);

        Assert.False(result.Succeeded);
        Assert.Equal(ResultErrorType.Forbidden, result.ErrorType);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public async Task SubmitEvaluationAsync_RatingOutOfRange_Fails(int rating)
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new EventService(db);
        var @event = (await service.CreateAsync(ValidCreateRequest())).Value!;
        var member = await SeedMemberForEventTestsAsync(db);
        var registration = (await service.RegisterAsync(member.UserId, @event.Id, "Onsite")).Value!;
        await MarkAttendedAsync(db, registration.Id);

        var result = await service.SubmitEvaluationAsync(member.UserId, registration.Id, rating, comments: null);

        Assert.False(result.Succeeded);
    }

    private static async Task MarkAttendedAsync(TestDbContext db, Guid registrationId)
    {
        var registration = await db.EventRegistrations.FindAsync(registrationId);
        registration!.Status = EventRegistrationStatus.Attended;
        await db.SaveChangesAsync();
    }

    /// <summary>Matches spec.md's "A member's CPD total reflects only completed, credited
    /// registrations".</summary>
    [Fact]
    public async Task GetMyCpdAsync_SumsOnlyEvaluationSubmittedRegistrationsWithNonNullUnits()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new EventService(db);
        var member = await SeedMemberForEventTestsAsync(db);

        var creditedEvent = (await service.CreateAsync(ValidCreateRequest("Credited Event"))).Value!;
        await service.UpdateAsync(creditedEvent.Id, ToUpdateRequest(creditedEvent) with { CpdUnitsOnsite = 8m });
        var creditedRegistration = (await service.RegisterAsync(member.UserId, creditedEvent.Id, "Onsite")).Value!;
        await MarkPaymentVerifiedAsync(db, creditedRegistration.Id);
        await service.RecordAttendanceAsync(
            creditedEvent.Id, [new RegistrantAttendanceRequest(creditedRegistration.Id, [creditedEvent.Sessions.Single().Id])], Guid.NewGuid());
        await service.SubmitEvaluationAsync(member.UserId, creditedRegistration.Id, rating: 5, comments: null);

        var tbdEvent = (await service.CreateAsync(ValidCreateRequest("TBD Units Event"))).Value!;
        var tbdRegistration = (await service.RegisterAsync(member.UserId, tbdEvent.Id, "Onsite")).Value!;
        await MarkPaymentVerifiedAsync(db, tbdRegistration.Id);
        await service.RecordAttendanceAsync(
            tbdEvent.Id, [new RegistrantAttendanceRequest(tbdRegistration.Id, [tbdEvent.Sessions.Single().Id])], Guid.NewGuid());
        await service.SubmitEvaluationAsync(member.UserId, tbdRegistration.Id, rating: 5, comments: null);

        var notYetEvaluatedEvent = (await service.CreateAsync(ValidCreateRequest("Not Yet Evaluated Event"))).Value!;
        await service.UpdateAsync(notYetEvaluatedEvent.Id, ToUpdateRequest(notYetEvaluatedEvent) with { CpdUnitsOnsite = 6m });
        await service.RegisterAsync(member.UserId, notYetEvaluatedEvent.Id, "Onsite");

        var summary = await service.GetMyCpdAsync(member.UserId);

        Assert.Equal(8m, summary.TotalCreditUnits);
        Assert.Equal(3, summary.Registrations.Count);
    }

    [Fact]
    public async Task GetMyCpdAsync_NoMemberProfile_ReturnsEmptySummary()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new EventService(db);

        var summary = await service.GetMyCpdAsync(Guid.NewGuid());

        Assert.Equal(0m, summary.TotalCreditUnits);
        Assert.Empty(summary.Registrations);
    }

    [Fact]
    public async Task GetRosterAsync_UnknownEvent_NotFound()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new EventService(db);

        var result = await service.GetRosterAsync(Guid.NewGuid());

        Assert.False(result.Succeeded);
        Assert.Equal(ResultErrorType.NotFound, result.ErrorType);
    }

    [Fact]
    public async Task GetRosterAsync_ReturnsPerSessionAttendancePaymentAndEvaluationState()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new EventService(db);
        var @event = (await service.CreateAsync(ValidCreateRequest())).Value!;
        var member = await SeedMemberForEventTestsAsync(db);
        var registration = (await service.RegisterAsync(member.UserId, @event.Id, "Onsite")).Value!;
        var paymentService = new PaymentService(db);
        var submitted = await paymentService.SubmitForEventRegistrationAsync(
            member.UserId, registration.Id, new SubmitPaymentRequest(500m, "REF-1", DateOnly.FromDateTime(DateTime.UtcNow)));
        db.Payments.First(p => p.Id == submitted.Value!.Id).ProofStorageKey = "proof/key.jpg";
        await db.SaveChangesAsync();
        await paymentService.VerifyAsync(submitted.Value!.Id, Guid.NewGuid());
        var sessionId = @event.Sessions.Single().Id;
        await service.RecordAttendanceAsync(@event.Id, [new RegistrantAttendanceRequest(registration.Id, [sessionId])], Guid.NewGuid());
        await service.SubmitEvaluationAsync(member.UserId, registration.Id, rating: 5, comments: "Excellent");

        var result = await service.GetRosterAsync(@event.Id);

        Assert.True(result.Succeeded);
        var entry = Assert.Single(result.Value!.Registrants);
        Assert.Equal("EvaluationSubmitted", entry.Status);
        Assert.Equal([sessionId], entry.AttendedSessionIds);
        Assert.Equal(1, entry.TotalSessions);
        Assert.Equal("Verified", entry.PaymentStatus);
        Assert.False(entry.PaymentIsCash);
        Assert.Equal(5, entry.EvaluationRating);
    }

    [Fact]
    public async Task GetRosterAsync_CashPayment_ReportsPaymentIsCashTrue()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new EventService(db);
        var @event = (await service.CreateAsync(ValidCreateRequest())).Value!;
        var member = await SeedMemberForEventTestsAsync(db);
        var registration = (await service.RegisterAsync(member.UserId, @event.Id, "Onsite")).Value!;
        var paymentService = new PaymentService(db);
        await paymentService.RecordEventCashPaymentAsync(registration.Id, 500m, Guid.NewGuid());

        var result = await service.GetRosterAsync(@event.Id);

        var entry = Assert.Single(result.Value!.Registrants);
        Assert.True(entry.PaymentIsCash);
    }

    [Fact]
    public async Task GetRosterAsync_ExcludesCancelledRegistrations()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new EventService(db);
        var @event = (await service.CreateAsync(ValidCreateRequest())).Value!;
        var member = await SeedMemberForEventTestsAsync(db);
        var registration = (await service.RegisterAsync(member.UserId, @event.Id, "Onsite")).Value!;
        await service.CancelRegistrationAsync(member.UserId, registration.Id);

        var result = await service.GetRosterAsync(@event.Id);

        Assert.Empty(result.Value!.Registrants);
    }

    private async Task<(EventDto Event, EventRegistrationDto Registration, Member Member)> SeedCreditedRegistrationAsync(
        TestDbContext db, EventService service, decimal onsiteUnits = 8m)
    {
        var created = (await service.CreateAsync(ValidCreateRequest())).Value!;
        var @event = (await service.UpdateAsync(created.Id, ToUpdateRequest(created) with { CpdUnitsOnsite = onsiteUnits })).Value!;
        var member = await SeedMemberForEventTestsAsync(db);
        var registration = (await service.RegisterAsync(member.UserId, @event.Id, "Onsite")).Value!;
        await MarkPaymentVerifiedAsync(db, registration.Id);
        await service.RecordAttendanceAsync(@event.Id, [new RegistrantAttendanceRequest(registration.Id, [@event.Sessions.Single().Id])], Guid.NewGuid());
        await service.SubmitEvaluationAsync(member.UserId, registration.Id, rating: 5, comments: null);
        return (@event, registration, member);
    }

    /// <summary>Matches spec.md's "Certificate request before credit is earned is refused" - not
    /// yet EvaluationSubmitted.</summary>
    [Fact]
    public async Task GetCertificateDataAsync_BeforeEvaluationSubmitted_Fails()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new EventService(db);
        var @event = (await service.CreateAsync(ValidCreateRequest())).Value!;
        await service.UpdateAsync(@event.Id, ToUpdateRequest(@event) with { CpdUnitsOnsite = 8m });
        var member = await SeedMemberForEventTestsAsync(db);
        var registration = (await service.RegisterAsync(member.UserId, @event.Id, "Onsite")).Value!;

        var result = await service.GetCertificateDataAsync(member.UserId, registration.Id, isAdmin: false);

        Assert.False(result.Succeeded);
    }

    /// <summary>Matches spec.md's other "not yet available" case - unit value still null.</summary>
    [Fact]
    public async Task GetCertificateDataAsync_ApplicableUnitsStillNull_Fails()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new EventService(db);
        var (@event, registration, member) = await SeedCreditedRegistrationAsync(db, service, onsiteUnits: 8m);
        // Correct the units back to null to simulate "never set for this modality".
        await service.UpdateAsync(@event.Id, ToUpdateRequest(@event) with { CpdUnitsOnsite = null });

        var result = await service.GetCertificateDataAsync(member.UserId, registration.Id, isAdmin: false);

        Assert.False(result.Succeeded);
    }

    /// <summary>Matches spec.md's "Certificate lists only attended sessions".</summary>
    [Fact]
    public async Task GetCertificateDataAsync_ListsOnlyAttendedSessions()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new EventService(db);
        var created = (await service.CreateAsync(ValidCreateRequest())).Value!;
        var sixSessions = Enumerable.Range(1, 6)
            .Select(i => new EventSessionRequest(i == 1 ? created.Sessions[0].Id : null, $"Lecture {i}", created.StartsAt.AddHours(i), created.StartsAt.AddHours(i + 1), i))
            .ToList();
        var @event = (await service.UpdateAsync(created.Id, ToUpdateRequest(created) with { Sessions = sixSessions, CpdUnitsOnsite = 8m })).Value!;
        var member = await SeedMemberForEventTestsAsync(db);
        var registration = (await service.RegisterAsync(member.UserId, @event.Id, "Onsite")).Value!;
        await MarkPaymentVerifiedAsync(db, registration.Id);
        var attendedIds = @event.Sessions.Take(3).Select(s => s.Id).ToList();
        await service.RecordAttendanceAsync(@event.Id, [new RegistrantAttendanceRequest(registration.Id, attendedIds)], Guid.NewGuid());
        await service.SubmitEvaluationAsync(member.UserId, registration.Id, rating: 5, comments: null);

        var result = await service.GetCertificateDataAsync(member.UserId, registration.Id, isAdmin: false);

        Assert.True(result.Succeeded);
        Assert.Equal(3, result.Value!.AttendedSessionTitles.Count);
        Assert.Equal(4m, result.Value.CreditUnits); // 8 x 3/6
    }

    /// <summary>Matches spec.md's "Certificate reflects a corrected unit count" - computed at read
    /// time, so a later correction is visible on the very next call with no other action taken.</summary>
    [Fact]
    public async Task GetCertificateDataAsync_AfterUnitCorrection_ReflectsNewValue()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new EventService(db);
        var (@event, registration, member) = await SeedCreditedRegistrationAsync(db, service, onsiteUnits: 8m);
        var before = await service.GetCertificateDataAsync(member.UserId, registration.Id, isAdmin: false);
        Assert.Equal(8m, before.Value!.CreditUnits);

        await service.UpdateAsync(@event.Id, ToUpdateRequest(@event) with { CpdUnitsOnsite = 6m });
        var after = await service.GetCertificateDataAsync(member.UserId, registration.Id, isAdmin: false);

        Assert.Equal(6m, after.Value!.CreditUnits);
    }

    [Fact]
    public async Task GetCertificateDataAsync_NotOwnerAndNotAdmin_Forbidden()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new EventService(db);
        var (_, registration, _) = await SeedCreditedRegistrationAsync(db, service);

        var result = await service.GetCertificateDataAsync(Guid.NewGuid(), registration.Id, isAdmin: false);

        Assert.False(result.Succeeded);
        Assert.Equal(ResultErrorType.Forbidden, result.ErrorType);
    }

    [Fact]
    public async Task GetCertificateDataAsync_Admin_BypassesOwnershipCheck()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new EventService(db);
        var (_, registration, _) = await SeedCreditedRegistrationAsync(db, service);

        var result = await service.GetCertificateDataAsync(Guid.NewGuid(), registration.Id, isAdmin: true);

        Assert.True(result.Succeeded);
    }

    /// <summary>Matches the same Mode-based selection CpdCredit.For uses for CreditUnits (Task 11) -
    /// an Onsite registration's certificate must carry Event.CpdCodeOnsite, not CpdCodeOnline, plus
    /// the shared (non-modality-split) Type/Hours metadata.</summary>
    [Fact]
    public async Task GetCertificateDataAsync_OnsiteRegistration_UsesOnsiteCpdCodeAndSharedEventMetadata()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new EventService(db);
        var created = (await service.CreateAsync(ValidCreateRequest())).Value!;
        var @event = (await service.UpdateAsync(created.Id, ToUpdateRequest(created) with
        {
            CpdUnitsOnsite = 8m,
            CpdUnitsOnline = 3m,
            CpdCodeOnsite = "PRC-ONSITE-001",
            CpdCodeOnline = "PRC-ONLINE-001",
            Type = EventTypes.Seminar,
            Hours = 8m,
        })).Value!;
        var member = await SeedMemberForEventTestsAsync(db);
        var registration = (await service.RegisterAsync(member.UserId, @event.Id, "Onsite")).Value!;
        await MarkPaymentVerifiedAsync(db, registration.Id);
        await service.RecordAttendanceAsync(@event.Id, [new RegistrantAttendanceRequest(registration.Id, [@event.Sessions.Single().Id])], Guid.NewGuid());
        await service.SubmitEvaluationAsync(member.UserId, registration.Id, rating: 5, comments: null);

        var result = await service.GetCertificateDataAsync(member.UserId, registration.Id, isAdmin: false);

        Assert.True(result.Succeeded);
        Assert.Equal("PRC-ONSITE-001", result.Value!.CpdCode);
        Assert.Equal(EventTypes.Seminar, result.Value.EventType);
        Assert.Equal(8m, result.Value.Hours);
    }

    /// <summary>Mirror of the Onsite case above for the Online modality - CpdCredit.CodeFor must
    /// select CpdCodeOnline, matching how CpdCredit.For selects CpdUnitsOnline for the same Mode.</summary>
    [Fact]
    public async Task GetCertificateDataAsync_OnlineRegistration_UsesOnlineCpdCode()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new EventService(db);
        var created = (await service.CreateAsync(ValidCreateRequest())).Value!;
        var @event = (await service.UpdateAsync(created.Id, ToUpdateRequest(created) with
        {
            CpdUnitsOnsite = 8m,
            CpdUnitsOnline = 3m,
            CpdCodeOnsite = "PRC-ONSITE-001",
            CpdCodeOnline = "PRC-ONLINE-001",
        })).Value!;
        var member = await SeedMemberForEventTestsAsync(db);
        var registration = (await service.RegisterAsync(member.UserId, @event.Id, "Online")).Value!;
        await MarkPaymentVerifiedAsync(db, registration.Id);
        await service.RecordAttendanceAsync(@event.Id, [new RegistrantAttendanceRequest(registration.Id, [@event.Sessions.Single().Id])], Guid.NewGuid());
        await service.SubmitEvaluationAsync(member.UserId, registration.Id, rating: 5, comments: null);

        var result = await service.GetCertificateDataAsync(member.UserId, registration.Id, isAdmin: false);

        Assert.True(result.Succeeded);
        Assert.Equal("PRC-ONLINE-001", result.Value!.CpdCode);
    }
}
