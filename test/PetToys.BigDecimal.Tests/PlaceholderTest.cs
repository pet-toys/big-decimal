using AwesomeAssertions;
using Xunit;

namespace PetToys.BigDecimal.Tests;

/// <summary>
/// Keeps the test project from reporting zero tests while the real suites are
/// still being written. Delete it once the first real test lands.
/// </summary>
public sealed class PlaceholderTest
{
    [Fact]
    public void Placeholder_AlwaysPasses()
    {
        true.Should().BeTrue();
    }
}
