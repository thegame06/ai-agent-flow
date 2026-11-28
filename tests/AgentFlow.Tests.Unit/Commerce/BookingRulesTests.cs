using AgentFlow.Api.Commerce;

namespace AgentFlow.Tests.Unit.Commerce;

/// <summary>
/// Anything can be reserved: a doctor, a restaurant table, a hotel room, a house, a machine.
/// The catalog item says how time is measured, what the price is for and for how many people.
/// </summary>
public class BookingRulesTests
{
    private static CommerceBookingDocument Doctor() => new()
    {
        Bookable = true, Mode = "appointment", DurationMinutes = 45, BufferMinutes = 10,
        ResourceKinds = ["Dentist", " chair ", "dentist", ""], Instructions = "  Venir sin comer  "
    };

    private static CommerceBookingDocument RestaurantTable() => new()
    {
        Bookable = true, Mode = "appointment", DurationMinutes = 120, MinPartySize = 1, MaxPartySize = 6, ResourceKinds = ["table"]
    };

    private static CommerceBookingDocument HotelRoom() => new()
    {
        Bookable = true, Mode = "nightly", MinUnits = 2, MaxPartySize = 3, ResourceKinds = ["double-room"]
    };

    private static CommerceBookingDocument ExcavatorByHour() => new()
    {
        Bookable = true, Mode = "hourly", MinMinutes = 120, MaxMinutes = 600, ResourceKinds = ["excavator"]
    };

    private static CommerceBookingDocument ExcavatorByDay() => new()
    {
        Bookable = true, Mode = "daily", MinUnits = 1, MaxUnits = 30, StartTime = "7:30"
    };

    [Fact]
    public void EveryKindOfReservation_IsValid()
    {
        Assert.Null(BookingRules.Validate(Doctor()));
        Assert.Null(BookingRules.Validate(RestaurantTable()));
        Assert.Null(BookingRules.Validate(HotelRoom()));
        Assert.Null(BookingRules.Validate(ExcavatorByHour()));
    }

    [Fact]
    public void TheDescription_SaysHowEachOneIsReserved()
    {
        Assert.Equal("cita de 45 min", BookingRules.Describe(BookingRules.Normalize(Doctor())));
        Assert.Equal("cita de 2 h, hasta 6 personas", BookingRules.Describe(BookingRules.Normalize(RestaurantTable())));
        Assert.Equal("por noches (minimo 2 noches), entrada 15:00 y salida 12:00, hasta 3 personas",
            BookingRules.Describe(BookingRules.Normalize(HotelRoom())));
        Assert.Equal("por horas, de 2 h a 10 h", BookingRules.Describe(BookingRules.Normalize(ExcavatorByHour())));
        Assert.Equal("por dias (1 a 30 dias), entrega 07:30 y devolucion 17:00", BookingRules.Describe(BookingRules.Normalize(ExcavatorByDay())));
    }

    [Fact]
    public void Normalizing_FillsThePriceUnitOfTheMode_AndCleansTheLists()
    {
        var doctor = BookingRules.Normalize(Doctor());
        var room = BookingRules.Normalize(HotelRoom());
        var hourly = BookingRules.Normalize(ExcavatorByHour());

        Assert.Equal("booking", doctor.PriceUnit);
        Assert.Equal(["dentist", "chair"], doctor.ResourceKinds.ToArray());
        Assert.Equal("Venir sin comer", doctor.Instructions);
        Assert.Equal("night", room.PriceUnit);
        Assert.Equal(("15:00", "12:00"), (room.StartTime, room.EndTime));
        Assert.Equal("hour", hourly.PriceUnit);
        Assert.Equal(30, hourly.StepMinutes);
    }

    [Fact]
    public void AnExplicitPriceUnit_IsKept()
    {
        var perPerson = BookingRules.Normalize(new CommerceBookingDocument { Mode = "appointment", DurationMinutes = 90, PriceUnit = "Person" });

        Assert.Equal("person", perPerson.PriceUnit);
        Assert.Equal("por persona", PriceUnits.Spanish(perPerson.PriceUnit));
    }

    [Fact]
    public void FieldsOfOtherModes_AreDropped()
    {
        var normalized = BookingRules.Normalize(new CommerceBookingDocument
        {
            Bookable = true, Mode = "nightly", DurationMinutes = 45, MinMinutes = 60, MinUnits = 1
        });

        Assert.Equal(0, normalized.DurationMinutes);
        Assert.Equal(0, normalized.MinMinutes);
    }

    [Theory]
    [InlineData("weekly", 0, 0, 0, 0, "modo de reserva")]
    [InlineData("appointment", 3, 0, 0, 0, "al menos 5 minutos")]
    [InlineData("hourly", 0, 0, 0, 0, "minimo de al menos 5")]
    [InlineData("hourly", 0, 240, 120, 0, "menor que el minimo")]
    [InlineData("nightly", 0, 0, 0, 400, "entre 0 y 365")]
    public void SettingsThatCannotWork_AreRejected(string mode, int duration, int minMinutes, int maxMinutes, int maxUnits, string reason)
        => Assert.Contains(reason, BookingRules.Validate(new CommerceBookingDocument
        {
            Bookable = true,
            Mode = mode,
            DurationMinutes = duration,
            MinMinutes = minMinutes,
            MaxMinutes = maxMinutes,
            MaxUnits = maxUnits
        }));

    [Fact]
    public void PartySizeAndTimes_AreChecked()
    {
        Assert.Contains("mayor que el maximo", BookingRules.Validate(new CommerceBookingDocument { Mode = "appointment", MinPartySize = 8, MaxPartySize = 4 }));
        Assert.Contains("no es valida", BookingRules.Validate(new CommerceBookingDocument { Mode = "nightly", StartTime = "3pm" }));
        Assert.Contains("unidad de precio", BookingRules.Validate(new CommerceBookingDocument { Mode = "daily", PriceUnit = "week" }));
    }

    [Fact]
    public void ANotBookableDraft_CanBeSavedIncomplete()
        => Assert.Null(BookingRules.Validate(new CommerceBookingDocument { Bookable = false, Mode = "appointment" }));

    [Theory]
    [InlineData(45, "45 min")]
    [InlineData(60, "1 h")]
    [InlineData(90, "1 h 30 min")]
    public void Lengths_ReadNaturally(int minutes, string text) => Assert.Equal(text, BookingRules.Length(minutes));
}
