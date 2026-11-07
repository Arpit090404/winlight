using WinLight.Core;

namespace WinLight.Tests;

public sealed class WindowActivationGateTests
{
    [Fact]
    public void TransientDeactivationBeforeActivationDoesNotDismissButFocusLossDoes()
    {
        var gate = new WindowActivationGate();
        gate.BeginShow();
        Assert.False(gate.ShouldHideOnDeactivated);

        gate.Activated();
        Assert.True(gate.ShouldHideOnDeactivated);

        gate.BeginShow();
        Assert.False(gate.ShouldHideOnDeactivated);
    }
}
