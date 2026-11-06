using System.Reflection;

namespace WinLight.Tests;

public sealed class ArchitectureTests
{
    [Fact]
    public void CoreDoesNotReferenceUiFrameworks()
    {
        Assembly core = Assembly.Load("WinLight.Core");
        string[] forbiddenPrefixes = ["Microsoft.UI", "Microsoft.WindowsAppSDK", "H.NotifyIcon"];

        foreach (AssemblyName reference in core.GetReferencedAssemblies())
        {
            foreach (string prefix in forbiddenPrefixes)
                Assert.False(reference.Name?.StartsWith(prefix, StringComparison.Ordinal) == true,
                    $"WinLight.Core must not reference {reference.Name}.");
        }
    }
}
