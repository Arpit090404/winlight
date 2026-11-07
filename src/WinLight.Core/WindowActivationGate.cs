namespace WinLight.Core;

// A show can receive a transient deactivation before its first activation.
public sealed class WindowActivationGate
{
    private bool _activated;

    public void BeginShow() => _activated = false;

    public void Activated() => _activated = true;

    public bool ShouldHideOnDeactivated => _activated;
}
