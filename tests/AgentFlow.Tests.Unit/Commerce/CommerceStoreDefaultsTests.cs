using AgentFlow.Api.Commerce;

namespace AgentFlow.Tests.Unit.Commerce;

public class CommerceStoreDefaultsTests
{
    [Theory]
    [InlineData(null, "Ventas y facturación")]
    [InlineData("  ", "Ventas y facturación")]
    [InlineData("Ventas y cobros", "Ventas y facturación")]
    [InlineData(" ventas y cobros ", "Ventas y facturación")]
    [InlineData(" Tienda Carlos ", "Tienda Carlos")]
    public void TheModulesOldDefaultName_IsReadAsTheNewOne_AndCustomNamesAreKept(string? stored, string expected)
        => Assert.Equal(expected, CommerceStoreDefaults.NormalizeStoreName(stored));
}
