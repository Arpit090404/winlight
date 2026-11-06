using WinLight.Core;

namespace WinLight.Tests;

public sealed class AppSearchTests
{
    [Fact]
    public void EmptyQueryReturnsAlphabeticalResultsWithoutHistory()
    {
        var search = new AppSearch();
        search.SetItems([new AppItem("Zulu", "z", "z"), new AppItem("Alpha", "a", "a")]);

        Assert.Equal(["Alpha", "Zulu"], search.Find("").Select(item => item.Name));
    }

    [Fact]
    public void PrefixBeatsSubstringAndSubsequence()
    {
        var search = new AppSearch();
        search.SetItems([
            new AppItem("Calculator", "calc", "calc"),
            new AppItem("My Calculator", "mine", "mine"),
            new AppItem("Cool Apps Linger Calmly", "other", "other")]);

        Assert.Equal("Calculator", search.Find("calc")[0].Name);
    }

    [Fact]
    public void MatchingIgnoresCaseAndDiacritics()
    {
        var search = new AppSearch();
        search.SetItems([new AppItem("Café", "cafe", "cafe")]);

        Assert.Single(search.Find("CAFE"));
    }

    [Fact]
    public void LaunchHistoryBoostsEmptyQuery()
    {
        var search = new AppSearch();
        var zulu = new AppItem("Zulu", "z", "z");
        search.SetItems([new AppItem("Alpha", "a", "a"), zulu]);
        search.RecordLaunch(zulu, DateTimeOffset.UtcNow);

        Assert.Equal("Zulu", search.Find("")[0].Name);
    }

    [Fact]
    public void DuplicateIdentityIsIndexedOnce()
    {
        var search = new AppSearch();
        search.SetItems([new AppItem("First", "first", "same"), new AppItem("Second", "second", "same")]);

        Assert.Equal(1, search.Count);
    }
}
