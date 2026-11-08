using System.Globalization;
using WinLight.Core;

namespace WinLight.Tests;

public sealed class CalculatorProviderTests
{
    public static TheoryData<string, string?> Cases => new()
    {
        { "2+2", "4" }, { "2 + 3", "5" }, { "2+3*4", "14" },
        { "7-4", "3" }, { "3*5", "15" },
        { "12/4", "3" }, { "2×3", "6" }, { "8÷2", "4" }, { "10 mod 3", "1" },
        { "2^3", "8" }, { "2^3^2", "512" }, { "-2^2", "-4" }, { "(-2)^2", "4" },
        { "+2+3", "5" }, { "--2+1", "3" }, { "2*-3", "-6" }, { "2^-2", "0.25" },
        { "(2+3)*4", "20" }, { "2*(3+4)", "14" }, { "2(3+4)", "14" },
        { "(1+2)(3+4)", "21" }, { "(2)3", "6" }, { "3sqrt(4)", "6" },
        { "2pi", "6.28318530718" }, { "2e", "5.43656365692" },
        { "0.1+0.2", "0.3" }, { "1/3", "0.333333333333" },
        { ".5+3.5", "4" }, { "1e3+2", "1002" }, { "1e-3*2", "0.002" },
        { "1.25e2+5", "130" }, { "10.000+0", "10" }, { "0-0.25", "-0.25" },
        { "1000000000000+1", "1000000000000" },
        { "50%", "0.5" }, { "200+10%", "220" }, { "100+10%", "110" },
        { "200-10%", "180" },
        { "10% of 200", "20" }, { "200+10% of 50", "205" },
        { "25%*200", "50" }, { "100*(1+5%)", "105" },
        { "sqrt(4)", "2" }, { "sqrt(2)", "1.41421356237" },
        { "abs(-5)", "5" }, { "round(2.5)", "3" }, { "round(-2.5)", "-3" },
        { "floor(2.9)", "2" }, { "floor(-2.1)", "-3" },
        { "ceil(2.1)", "3" }, { "ceil(-2.9)", "-2" },
        { "ln(e)", "1" }, { "log(100)", "2" }, { "exp(0)", "1" },
        { "sin(0)", "0" }, { "cos(0)", "1" }, { "tan(0)", "0" },
        { "sin(pi/2)", "1" }, { "cos(pi)", "-1" },
        { "sqrt(9)+1", "4" }, { "2log(100)", "4" },
        { "=2", "2" }, { "= 2", "2" }, { "=pi", "3.14159265359" },
        { "=e", "2.71828182846" }, { "=1e3", "1000" },
        { "SIN(0)", "0" }, { "2PI", "6.28318530718" }, { "LOG(100)", "2" },
        { "2", null }, { "3.5", null }, { ".5", null }, { "1e3", null },
        { "e", null }, { "pi", null }, { "(2)", null },
        { "chrome", null }, { "vsc", null }, { "note", null },
        { "7-zip", null }, { "1password", null }, { "3ds max", null },
        { "xyz", null }, { "=chrome", null }, { "2+", null },
        { "sqrt(", null }, { "2*(3+", null }, { "1/0", null },
        { "10 mod 0", null }, { "sqrt(-1)", null }, { "log(0)", null },
        { "ln(-1)", null }, { "0^(-1)", null }, { "1e100+1", null },
        { "2..3", null }, { "2+bad", null }, { "sin", null },
        { "2 3 4+", null }, { "2^", null }, { "()", null },
        { "1e", "2.71828182846" }, { "1e+", null }, { "1e-", null },
        { "1,5+2", null },
        { "=", null }, { "", null }
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task EvaluatesInvariantlyUnderCommaDecimalCultures(string expression, string? expected)
    {
        foreach (string cultureName in new[] { "tr-TR", "fr-FR" })
        {
            CultureInfo previous = CultureInfo.CurrentCulture;
            CultureInfo previousUi = CultureInfo.CurrentUICulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(cultureName);
                var provider = new CalculatorProvider();
                IReadOnlyList<SearchResult> results = await provider.QueryAsync(expression, default);
                if (expected is null) { Assert.Empty(results); continue; }
                SearchResult result = Assert.Single(results);
                Assert.Equal(expected, result.Title);
                Assert.Equal("calculator", result.ProviderId);
                Assert.Equal(1, result.Score);
                Assert.Equal(expected, Assert.IsType<ResultAction.CopyText>(result.Action).Text);
                Assert.EndsWith(" =", result.Subtitle);
            }
            finally
            {
                CultureInfo.CurrentCulture = previous;
                CultureInfo.CurrentUICulture = previousUi;
            }
        }
    }

    [Fact]
    public async Task RejectsLengthAndDepthLimits()
    {
        var provider = new CalculatorProvider();
        Assert.Empty(await provider.QueryAsync(new string('1', 257) + "+1", default));
        Assert.Empty(await provider.QueryAsync(new string('(', 65) + "1+1" + new string(')', 65), default));
    }

    [Fact]
    public async Task RejectsCancellation()
    {
        var provider = new CalculatorProvider();
        using var stop = new CancellationTokenSource();
        stop.Cancel();
        Assert.Empty(await provider.QueryAsync("2+2", stop.Token));
    }

    [Fact]
    public async Task SubtitleShowsNormalizedExpressionAndEquals()
    {
        var provider = new CalculatorProvider();
        SearchResult result = Assert.Single(await provider.QueryAsync(" 12×8 ", default));
        Assert.Equal("12*8 =", result.Subtitle);
    }

    [Fact]
    public async Task EngineKeepsAppQueriesAndRanksCalculatorResultFirst()
    {
        var search = new AppSearch();
        search.SetItems([new AppItem("Chrome", "chrome", "chrome")]);
        var engine = new SearchEngine(
            [(new AppsProvider(search), 1d), (new CalculatorProvider(), 1.1d)], (_, _) => { });

        IReadOnlyList<SearchResult> app = await engine.QueryAsync("chrome");
        IReadOnlyList<SearchResult> calculation = await engine.QueryAsync("2+2");

        Assert.Equal("Chrome", Assert.Single(app).Title);
        Assert.Equal("4", calculation[0].Title);
        Assert.IsType<ResultAction.CopyText>(calculation[0].Action);
    }

    [Fact]
    public async Task CalculatorWeightBeatsMaxFrecencyApp()
    {
        var search = new AppSearch();
        search.SetItems([new AppItem("2+2", "app", "app")]);
        search.LoadUsage(new Dictionary<string, UsageRecord>
        {
            ["app"] = new(20, new DateTimeOffset(2100, 1, 1, 0, 0, 0, TimeSpan.Zero))
        });
        var engine = new SearchEngine(
            [(new AppsProvider(search), 1d), (new CalculatorProvider(), 1.1d)], (_, _) => { });

        IReadOnlyList<SearchResult> results = await engine.QueryAsync("2+2");

        Assert.Equal("4", results[0].Title);
        Assert.Equal("calculator", results[0].ProviderId);
        Assert.Equal("2+2", results[1].Title);
        Assert.Equal("apps", results[1].ProviderId);
    }

    [Fact]
    public void IrrelevantQueriesAllocateNothing()
    {
        var provider = new CalculatorProvider();
        string[] queries = ["chrome", "vsc", "note", "7-zip"];
        foreach (string query in queries)
            _ = provider.QueryAsync(query, default);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++)
            foreach (string query in queries)
                _ = provider.QueryAsync(query, default);
        long after = GC.GetAllocatedBytesForCurrentThread();
        Assert.Equal(before, after);
    }
}
