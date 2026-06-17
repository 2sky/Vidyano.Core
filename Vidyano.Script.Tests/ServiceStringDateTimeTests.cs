using System;
using Vidyano;
using Xunit;

namespace Vidyano.Script.Tests;

/// <summary>
/// Coverage for <see cref="Client.FromServiceString"/> on Date and Time attributes. The server maps a
/// <c>DateOnly</c> property to a <c>Date</c> attribute (wire <c>"dd-MM-yyyy"</c>, no time) and a
/// <c>TimeOnly</c> property to a <c>Time</c> attribute (wire <c>"HH:mm"</c>/<c>"HH:mm:ss"</c>). The client
/// models both as <c>DateTime</c>/<c>TimeSpan</c>; it must parse the server's short forms (and still the
/// legacy full-<c>DateTime</c>/<c>"G"</c>-<c>TimeSpan</c> forms) instead of silently defaulting.
/// </summary>
public sealed class ServiceStringDateTimeTests
{
    [Theory]
    [InlineData(DataTypes.Date)]
    [InlineData(DataTypes.NullableDate)]
    public void Date_ParsesDayMonthYear(string type)
    {
        Assert.Equal(new DateTime(2024, 3, 15), (DateTime)Client.FromServiceString("15-03-2024", type));
    }

    [Fact]
    public void Date_ToleratesTrailingTime()
    {
        // A DateTime-backed Date (or any value the server stamps with a time) still resolves to the date.
        Assert.Equal(new DateTime(2024, 3, 15), (DateTime)Client.FromServiceString("15-03-2024 00:00:00.0000000", DataTypes.Date));
    }

    [Theory]
    [InlineData(DataTypes.Time)]
    [InlineData(DataTypes.NullableTime)]
    public void Time_ParsesHoursMinutes(string type)
    {
        Assert.Equal(new TimeSpan(14, 30, 0), (TimeSpan)Client.FromServiceString("14:30", type));
    }

    [Fact]
    public void Time_ParsesHoursMinutesSeconds()
    {
        Assert.Equal(new TimeSpan(9, 5, 45), (TimeSpan)Client.FromServiceString("09:05:45", DataTypes.Time));
    }

    [Theory]
    [InlineData("9:05")]
    [InlineData("9:05:45")]
    public void Time_ParsesSingleDigitHour(string wire)
    {
        // An unpadded single-digit hour must parse, not silently default to TimeSpan.Zero.
        var expected = wire.Length > 4 ? new TimeSpan(9, 5, 45) : new TimeSpan(9, 5, 0);
        Assert.Equal(expected, (TimeSpan)Client.FromServiceString(wire, DataTypes.Time));
    }

    [Fact]
    public void Time_StillParsesLegacyTimeSpanForm()
    {
        // A TimeSpan-backed Time serializes as the "G" form; the fallback must keep parsing it.
        Assert.Equal(new TimeSpan(14, 30, 0), (TimeSpan)Client.FromServiceString("0:14:30:00.0000000", DataTypes.Time));
    }

    [Fact]
    public void DateTime_FullFormUnaffected()
    {
        Assert.Equal(new DateTime(2024, 3, 15, 14, 30, 0), (DateTime)Client.FromServiceString("15-03-2024 14:30:00.0000000", DataTypes.DateTime));
    }
}
