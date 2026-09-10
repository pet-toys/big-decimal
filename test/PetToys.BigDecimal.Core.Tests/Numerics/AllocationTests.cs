using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using AwesomeAssertions;
using PetToys.BigDecimal.Numerics.Harness;
using Xunit;

namespace PetToys.BigDecimal.Numerics;

/// <summary>
/// Holds the whole public surface to the zero-allocation guarantee, rather than sampling it.
/// </summary>
/// <remarks>
/// Deterministic by construction - a fixed inventory and a fixed reflection walk - so these run on
/// every leg of the continuous integration matrix. They are the tests whose answers differ by
/// platform and by runtime, which is exactly why they must not be run only on one machine.
/// </remarks>
/// <remarks>
/// Unparallelised, for the reason <see cref="SerialMeasurement"/> records: another thread cannot be
/// counted into a per-thread measurement, but it can trigger the collection that trims the shared
/// array pool inside one.
/// </remarks>
[Collection(SerialMeasurement.Name)]
public sealed class AllocationTests
{
    [Theory]
    [MemberData(nameof(CoveredOperations))]
    public void EveryCoveredOperation_AllocatesNothing(string label)
    {
        var measured = Allocations.Measure(AllocationInventory.Operation(label));

        measured.Should().Be(0, "{0} must not allocate", label);
    }

    [Fact]
    public void EveryPublicMember_IsCoveredOrExcusedWithAReason()
    {
        var members = typeof(BigDecimal)
            .GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(member => member.MemberType is MemberTypes.Method or MemberTypes.Property or MemberTypes.Field)
            .Select(member => member.Name)
            .Select(name => name.StartsWith("get_", StringComparison.Ordinal) ? name[4..] : name)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var unclassified = members
            .Where(name => !AllocationInventory.CoveredMembers.Contains(name))
            .Where(name => !AllocationInventory.Exclusions.ContainsKey(name))
            .ToArray();

        unclassified.Should().BeEmpty(
            "every public member must be measured or excused: {0}",
            string.Join(", ", unclassified));
    }

    [Fact]
    public void EveryExclusion_CarriesAReasonAndStillExists()
    {
        var members = typeof(BigDecimal)
            .GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Select(member => member.Name)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var (name, reason) in AllocationInventory.Exclusions)
        {
            members.Should().Contain(name, "an exclusion must name a member that exists");
            reason.Should().NotBeNullOrWhiteSpace("the exclusion of {0} must say why", name);
        }
    }

    [Fact]
    public void EveryPooledBuffer_IsDeclaredWithItsReason()
    {
        // A rent is measured at zero once the pool is warm, so nothing in the inventory can notice
        // one appearing. Declaring them is what makes a new rent a decision rather than an
        // accident, and it is where the stack bound that decides them is written down.
        AllocationInventory.PooledBuffers.Should().NotBeEmpty();

        foreach (var (site, reason) in AllocationInventory.PooledBuffers)
        {
            reason.Should().NotBeNullOrWhiteSpace("the buffer at {0} must say what it does and when", site);
        }

        AllocationInventory.PooledBuffers.Should().ContainKey(
            "TryFormat to UTF-8",
            "the UTF-8 overload formats through an intermediate, and sizing that intermediate from a "
            + "constant instead of from the text is the defect this change closed");
    }

    [Fact]
    public void AValueTooLargeToParse_FailsWithoutAllocating()
    {
        // TryParse called the throwing Pack inside a try/catch, so an input the type cannot hold
        // cost 464 bytes of exception on the one path whose contract is to fail quietly. The
        // inventory never saw it because every parse entry used a value that fits.
        var tooLarge = "1" + new string('0', 78);

        Allocations.Measure(() => Allocations.OtherSink =
                BigDecimal.TryParse(tooLarge, CultureInfo.InvariantCulture, out var value) ? value.Scale : -1)
            .Should().Be(0, "a parse that returns false allocates nothing");

        BigDecimal.TryParse(tooLarge, CultureInfo.InvariantCulture, out _).Should().BeFalse();
    }

    [Fact]
    public void ALongUtf8Payload_ParsesWithoutAllocating()
    {
        var utf8 = Encoding.UTF8.GetBytes("0." + new string('7', 500));

        Allocations.Measure(() => Allocations.Sink = BigDecimal.Parse(utf8, CultureInfo.InvariantCulture))
            .Should().Be(0, "a pooled buffer is not an allocation");
    }

    [Fact]
    public void ALongFormat_WritesWithoutAllocating()
    {
        var value = BigDecimal.Parse("0." + new string('9', 250), CultureInfo.InvariantCulture);
        var destination = new char[1024];

        Allocations.Measure(() =>
                Allocations.OtherSink = value.TryFormat(destination, out var written, "F250", CultureInfo.InvariantCulture) ? written : -1)
            .Should().Be(0);
    }

    [Fact]
    public void RepeatedRuns_StayAtZero()
    {
        // The pooled-buffer path measured many times over, which is where a stray collection or a
        // trimmed pool would show up.
        var utf8 = Encoding.UTF8.GetBytes("0." + new string('7', 500));

        Allocations.Measure(() => Allocations.Sink = BigDecimal.Parse(utf8, CultureInfo.InvariantCulture), 200)
            .Should().Be(0);
    }

    public static TheoryData<string> CoveredOperations()
    {
        var data = new TheoryData<string>();
        foreach (var label in AllocationInventory.Labels)
        {
            data.Add(label);
        }

        return data;
    }
}
