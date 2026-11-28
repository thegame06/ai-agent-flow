using System.Text.Json;
using AgentFlow.Api.Collections;
using AgentFlow.Api.Commerce;

namespace AgentFlow.Tests.Unit.Commerce;

/// <summary>Branch opening hours, holidays, booking data of services and what the assistant reads about the business.</summary>
public class BusinessHoursTests
{
    // Monday 2026-11-30 in Managua (UTC-6, no daylight saving).
    private static readonly DateOnly Monday = new(2026, 11, 30);

    private static CommerceBranchDocument Clinic(params (string Day, string Open, string Close)[] hours) => new()
    {
        TenantId = "tenant-a",
        Code = "CENTRO",
        Name = "Clinica Centro",
        Address = "Rotonda El Gueguense 2c al sur, Managua",
        Timezone = "America/Managua",
        OpeningHours = hours.Select(h => new CommerceOpeningInterval { Day = h.Day, Open = h.Open, Close = h.Close }).ToList()
    };

    private static CommerceBranchDocument WeekdaysWithLunch()
    {
        var hours = new List<(string, string, string)>();
        foreach (var day in new[] { "monday", "tuesday", "wednesday", "thursday", "friday" })
        {
            hours.Add((day, "08:00", "12:00"));
            hours.Add((day, "14:00", "18:00"));
        }
        hours.Add(("saturday", "08:00", "12:00"));
        return Clinic(hours.ToArray());
    }

    private static DateTime At(DateOnly date, int hour, int minute = 0) => date.ToDateTime(new TimeOnly(hour, minute));

    [Theory]
    [InlineData("08:00", 480)]
    [InlineData("00:00", 0)]
    [InlineData("24:00", 1440)]
    [InlineData("17:30", 1050)]
    public void Times_AreReadAsMinutesFromMidnight(string text, int minutes)
    {
        Assert.True(BusinessHours.TryParseTime(text, out var parsed));
        Assert.Equal(minutes, parsed);
    }

    [Theory]
    [InlineData("8")]
    [InlineData("8:0")]
    [InlineData("24:30")]
    [InlineData("12:60")]
    [InlineData("-1:00")]
    [InlineData("")]
    public void BadTimes_AreRejected(string text) => Assert.False(BusinessHours.TryParseTime(text, out _));

    [Fact]
    public void ValidHours_PassValidation()
    {
        var branch = WeekdaysWithLunch();
        branch.Closures = [new CommerceBranchClosure { Date = "2026-12-08", Reason = "Purisima" }];

        Assert.Null(BusinessHours.Validate(branch.OpeningHours, branch.Closures, branch.Timezone));
    }

    [Theory]
    [InlineData("lunes", "08:00", "12:00", "no es valido")]
    [InlineData("monday", "12:00", "08:00", "despues de la apertura")]
    [InlineData("monday", "22:00", "02:00", "medianoche")]
    [InlineData("monday", "08:00", "25:00", "cierre")]
    public void AnIntervalThatCannotWork_IsRejectedWithAReason(string day, string open, string close, string reason)
    {
        var error = BusinessHours.Validate([new CommerceOpeningInterval { Day = day, Open = open, Close = close }], null, null);

        Assert.NotNull(error);
        Assert.Contains(reason, error);
    }

    [Fact]
    public void OverlappingIntervals_OnTheSameDay_AreRejected()
    {
        var error = BusinessHours.Validate(
        [
            new CommerceOpeningInterval { Day = "monday", Open = "08:00", Close = "13:00" },
            new CommerceOpeningInterval { Day = "monday", Open = "12:00", Close = "18:00" }
        ], null, null);

        Assert.Contains("enciman", error);
    }

    [Fact]
    public void AnUnknownTimeZone_OrABadClosureDate_IsRejected()
    {
        Assert.Contains("zona horaria", BusinessHours.Validate(null, null, "Mars/Olympus"));
        Assert.Contains("yyyy-MM-dd", BusinessHours.Validate(null, [new CommerceBranchClosure { Date = "08/12/2026" }], null));
        Assert.Contains("repetida", BusinessHours.Validate(null,
            [new CommerceBranchClosure { Date = "2026-12-08" }, new CommerceBranchClosure { Date = "2026-12-08" }], null));
    }

    [Fact]
    public void Normalizing_TrimsSortsAndDropsBrokenIntervals()
    {
        var normalized = BusinessHours.NormalizeOpeningHours(
        [
            new CommerceOpeningInterval { Day = " Tuesday ", Open = "14:00", Close = "18:00" },
            new CommerceOpeningInterval { Day = "monday", Open = "08:00", Close = "12:00" },
            new CommerceOpeningInterval { Day = "tuesday", Open = "08:00", Close = "12:00" },
            new CommerceOpeningInterval { Day = "funday", Open = "08:00", Close = "12:00" }
        ]);

        Assert.Equal(["monday 08:00-12:00", "tuesday 08:00-12:00", "tuesday 14:00-18:00"],
            normalized.Select(h => $"{h.Day} {h.Open}-{h.Close}").ToArray());
    }

    [Fact]
    public void AHoliday_OverridesTheWeeklyHours()
    {
        var branch = WeekdaysWithLunch();
        branch.Closures =
        [
            new CommerceBranchClosure { Date = "2026-12-08", Reason = "Purisima" },
            new CommerceBranchClosure { Date = "2026-12-24", Intervals = [new CommerceTimeRange { Open = "08:00", Close = "11:00" }] }
        ];

        Assert.Equal("08:00-12:00, 14:00-18:00", BusinessHours.Describe(BusinessHours.HoursOn(branch, new DateOnly(2026, 12, 7))));
        Assert.Equal("cerrado", BusinessHours.Describe(BusinessHours.HoursOn(branch, new DateOnly(2026, 12, 8))));
        Assert.Equal("08:00-11:00", BusinessHours.Describe(BusinessHours.HoursOn(branch, new DateOnly(2026, 12, 24))));
        Assert.Equal("cerrado", BusinessHours.Describe(BusinessHours.HoursOn(branch, new DateOnly(2026, 12, 6))));
    }

    [Fact]
    public void DuringOpeningHours_ItIsOpen_AndClosesAtTheEndOfTheInterval()
    {
        var status = BusinessHours.Status(WeekdaysWithLunch(), At(Monday, 10, 30));

        Assert.True(status.IsOpen);
        Assert.Equal(At(Monday, 12), status.ClosesAt);
    }

    [Fact]
    public void AtLunch_ItIsClosed_AndOpensAgainInTheAfternoon()
    {
        var status = BusinessHours.Status(WeekdaysWithLunch(), At(Monday, 12, 30));

        Assert.False(status.IsOpen);
        Assert.Equal(At(Monday, 14), status.NextOpening);
    }

    [Fact]
    public void OnSaturdayAfternoon_ItOpensOnMonday()
    {
        var saturday = Monday.AddDays(5);

        var status = BusinessHours.Status(WeekdaysWithLunch(), At(saturday, 15));

        Assert.False(status.IsOpen);
        Assert.Equal(At(saturday.AddDays(2), 8), status.NextOpening);
    }

    [Fact]
    public void BackToBackIntervals_AndMidnight_CloseAtTheRealEnd()
    {
        var branch = Clinic(
            ("monday", "08:00", "12:00"), ("monday", "12:00", "18:00"),
            ("friday", "20:00", "24:00"), ("saturday", "00:00", "02:00"));

        Assert.Equal(At(Monday, 18), BusinessHours.Status(branch, At(Monday, 9)).ClosesAt);
        Assert.Equal(At(Monday.AddDays(5), 2), BusinessHours.Status(branch, At(Monday.AddDays(4), 23)).ClosesAt);
    }

    [Fact]
    public void ABranchWithoutHours_NeverOpens()
    {
        var status = BusinessHours.Status(Clinic(), At(Monday, 10));

        Assert.False(status.IsOpen);
        Assert.Null(status.NextOpening);
    }

    private static JsonElement Info(IReadOnlyList<CommerceBranchDocument> branches, DateTimeOffset nowUtc, string? branch = null, string? date = null)
    {
        var settings = new CommerceStoreSettingsDocument
        {
            TenantId = "tenant-a",
            StoreName = "Sonrisas Dental",
            BusinessDescription = "Clinica dental familiar",
            Timezone = "America/Managua"
        };
        var result = BusinessInfo.Build(settings, branches, nowUtc, branch, date);
        return JsonSerializer.SerializeToElement(result, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    [Fact]
    public void TheAssistant_SeesTheBusiness_ItsBranches_AndIfTheyAreOpenNow_InLocalTime()
    {
        // 16:30 UTC is 10:30 in Managua.
        var info = Info([WeekdaysWithLunch()], new DateTimeOffset(2026, 11, 30, 16, 30, 0, TimeSpan.Zero));

        Assert.Equal("Sonrisas Dental", info.GetProperty("business").GetProperty("name").GetString());
        Assert.Equal("lunes", info.GetProperty("business").GetProperty("weekday").GetString());
        Assert.Equal("10:30", info.GetProperty("business").GetProperty("localTime").GetString());
        var branch = info.GetProperty("branches")[0];
        Assert.True(branch.GetProperty("openNow").GetBoolean());
        Assert.Equal("hoy 12:00", branch.GetProperty("closesAt").GetString());
        var week = branch.GetProperty("weeklyHours").EnumerateArray().ToDictionary(d => d.GetProperty("day").GetString()!, d => d.GetProperty("hours").GetString());
        Assert.Equal("08:00-12:00, 14:00-18:00", week["lunes"]);
        Assert.Equal("cerrado", week["domingo"]);
    }

    [Fact]
    public void AskingForADate_ShowsTheHoursOfThatDate_IncludingHolidays()
    {
        var branch = WeekdaysWithLunch();
        branch.Closures = [new CommerceBranchClosure { Date = "2026-12-08", Reason = "Purisima" }];

        var info = Info([branch], new DateTimeOffset(2026, 11, 30, 16, 0, 0, TimeSpan.Zero), date: "2026-12-08");

        var shown = info.GetProperty("branches")[0];
        Assert.Equal("cerrado", shown.GetProperty("askedDate").GetProperty("hours").GetString());
        Assert.Equal("Purisima", shown.GetProperty("askedDate").GetProperty("reason").GetString());
        Assert.Equal("2026-12-08", shown.GetProperty("upcomingClosures")[0].GetProperty("date").GetString());
    }

    [Fact]
    public void ABranch_IsFoundByName_WithoutAccents_AndInactiveOnesAreHidden()
    {
        var leon = Clinic(("monday", "08:00", "17:00"));
        leon.Code = "LEON";
        leon.Name = "Clínica León";
        var closed = Clinic(("monday", "08:00", "17:00"));
        closed.Name = "Clinica Masaya";
        closed.Active = false;

        var found = Info([WeekdaysWithLunch(), leon, closed], DateTimeOffset.UtcNow, branch: "leon");
        var all = Info([WeekdaysWithLunch(), leon, closed], DateTimeOffset.UtcNow);
        var unknown = Info([WeekdaysWithLunch(), leon, closed], DateTimeOffset.UtcNow, branch: "Granada");

        Assert.Equal(1, found.GetProperty("branchesFound").GetInt32());
        Assert.Equal("Clínica León", found.GetProperty("branches")[0].GetProperty("name").GetString());
        Assert.Equal(2, all.GetProperty("branchesFound").GetInt32());
        Assert.Equal(2, unknown.GetProperty("branchesFound").GetInt32());
        Assert.Contains("Granada", unknown.GetProperty("note").GetString());
    }

    [Fact]
    public void ABranchWithoutHours_SaysSo_InsteadOfClaimingItIsClosed()
    {
        var info = Info([Clinic()], DateTimeOffset.UtcNow);

        var branch = info.GetProperty("branches")[0];
        Assert.False(branch.GetProperty("hoursConfigured").GetBoolean());
        Assert.Equal(JsonValueKind.Null, branch.GetProperty("openNow").ValueKind);
    }

    [Fact]
    public void BothAssistants_CanReadTheBusinessInfo()
    {
        Assert.Contains(BusinessInfo.ToolName, SalesAssistantAgent.Tools);
        Assert.Contains(BusinessInfo.ToolName, CollectionsAssistantAgent.Tools);
        Assert.Contains(BusinessInfo.ToolName, SalesAssistantAgent.SystemPrompt);
        Assert.Contains(BusinessInfo.ToolName, CollectionsAssistantAgent.SystemPrompt);
    }
}
