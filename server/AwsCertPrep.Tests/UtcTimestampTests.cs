using System.Text.Json;
using AwsCertPrep.Api.Data;

namespace AwsCertPrep.Tests;

/// <summary>
/// Timestamps must reach the browser saying which zone they are in.
///
/// This exists because of a bug that made the timed mock exam unusable outside UTC. Every
/// DateTime in the model is UTC, but SQL Server's datetime2 does not record that, so a value
/// read back arrived as <see cref="DateTimeKind.Unspecified"/>, System.Text.Json wrote it with
/// no trailing "Z", and the browser - following the spec - read an offsetless timestamp as
/// LOCAL time. At UTC+7 the exam's start time therefore looked seven hours old, the countdown
/// computed a deadline that had already passed, and the exam auto-submitted the instant it
/// opened. The same JSON was correct on the POST that created the session, because that value
/// had never been through the database: only the round trip lost the kind.
/// </summary>
public class UtcTimestampTests
{
    [Fact]
    public void A_timestamp_read_from_the_database_comes_back_as_utc()
    {
        // What SQL Server hands back: the right instant, with no zone attached.
        var fromDatabase = new DateTime(2026, 9, 17, 3, 45, 23, DateTimeKind.Unspecified);

        var converted = (DateTime)new AppDbContext.UtcDateTimeConverter()
            .ConvertFromProvider(fromDatabase)!;

        Assert.Equal(DateTimeKind.Utc, converted.Kind);
        Assert.Equal(fromDatabase.Ticks, converted.Ticks);   // the instant itself must not shift
    }

    [Fact]
    public void A_null_timestamp_stays_null()
    {
        var converter = new AppDbContext.NullableUtcDateTimeConverter();

        Assert.Null(converter.ConvertFromProvider(null));

        var converted = (DateTime?)converter.ConvertFromProvider(
            new DateTime(2026, 9, 17, 3, 45, 23, DateTimeKind.Unspecified));

        Assert.Equal(DateTimeKind.Utc, converted!.Value.Kind);
    }

    [Fact]
    public void The_serialized_timestamp_carries_a_zone_the_browser_can_read()
    {
        // The actual regression: without the "Z", new Date(...) in a browser means local time.
        var unspecified = new DateTime(2026, 9, 17, 3, 45, 23, DateTimeKind.Unspecified);
        var utc = (DateTime)new AppDbContext.UtcDateTimeConverter().ConvertFromProvider(unspecified)!;

        Assert.DoesNotContain("Z", JsonSerializer.Serialize(unspecified));
        Assert.Contains("Z", JsonSerializer.Serialize(utc));
    }

    [Fact]
    public void Writing_a_timestamp_does_not_rewrite_it()
    {
        // Only the read side pins the kind. Converting on the way in as well would be a second
        // place for a value to be shifted, and everything written here is already UtcNow.
        var utc = new DateTime(2026, 9, 17, 3, 45, 23, DateTimeKind.Utc);

        var stored = (DateTime)new AppDbContext.UtcDateTimeConverter().ConvertToProvider(utc)!;

        Assert.Equal(utc, stored);
    }
}
