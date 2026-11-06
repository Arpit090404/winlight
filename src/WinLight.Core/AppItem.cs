using System.Globalization;
using System.Text;

namespace WinLight.Core;

public sealed class AppItem
{
    public AppItem(string name, string launchPath, string identity,
        AppSource source = AppSource.Unknown, string? targetPath = null,
        string? appUserModelId = null, string? arguments = null)
    {
        Name = name;
        LaunchPath = launchPath;
        Identity = identity;
        Source = source;
        TargetPath = targetPath;
        AppUserModelId = appUserModelId;
        Arguments = arguments ?? string.Empty;
        NormalizedName = Normalize(name);
    }

    public string Name { get; }
    public string LaunchPath { get; }
    public string Identity { get; }
    public AppSource Source { get; }
    public string? TargetPath { get; }
    public string? AppUserModelId { get; }
    public string Arguments { get; }
    public string NormalizedName { get; }

    public static string Normalize(string value)
    {
        string decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (char character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
                builder.Append(char.ToUpperInvariant(character));
        }
        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}

public enum AppSource { Unknown, AppsFolder, UserStartMenu, CommonStartMenu }
