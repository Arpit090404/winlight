using WinLight.Core;

namespace WinLight.Tests;

public sealed class AppSearchGoldenTests
{
    private static readonly string[] Queries =
    [
        "a", "b", "c", "d", "m", "p", "s", "v", "z",
        "al", "app", "calc", "cal", "cam", "code", "cont", "doc",
        "draw", "edit", "file", "graph", "mail", "map", "music",
        "note", "photo", "play", "power", "project", "search",
        "set", "studio", "term", "video", "win", "wsl",
        "cafe", "resume", "vsc", "msw", "xyz", "zzzz", "calclator",
        "npt", "aplication", "omega", "alpha"
    ];

    private static readonly string[] Expected =
    [
        "a|Alpha;Alpha Manager 240;Alpha Calendar 070;Alpha Calendar 170;Alpha Calendar 270;Alpha Camera 060;Alpha Camera 160;Alpha Camera 260",
        "b|Beta Editor 001;Beta Calendar 071;Beta Calendar 171;Beta Calendar 271;Beta Camera 061;Beta Camera 161;Beta Camera 261;Beta Editor 101",
        "c|Calculator;Cafe;Café;Calculator;Calculator Pro;Calendar;Camera;Code Editor",
        "d|Delta Calendar 073;Delta Calendar 173;Delta Calendar 273;Delta Camera 063;Delta Camera 163;Delta Camera 263;Delta Editor 003;Delta Editor 103",
        "m|Microsoft Store;Mail;Maps;Microsoft Word;Music Player;My Calculator;Alpha Manager 240;Alpha Manager 040",
        "p|Photo Editor;Photos;Pixel Calendar 076;Pixel Calendar 176;Pixel Calendar 276;Pixel Camera 066;Pixel Camera 166;Pixel Camera 266",
        "s|Search Everything;Studio Calendar 078;Studio Calendar 178;Studio Calendar 278;Studio Camera 068;Studio Camera 168;Studio Camera 268;Studio Editor 008",
        "v|Visual Studio Code;VS Code;Vector Calendar 077;Vector Calendar 177;Vector Calendar 277;Vector Camera 067;Vector Camera 167;Vector Camera 267",
        "z|",
        "al|Alpha;Alpha Manager 240;Alpha Calendar 070;Alpha Calendar 170;Alpha Calendar 270;Alpha Camera 060;Alpha Camera 160;Alpha Camera 260",
        "app|App Installer;Application Manager;Alpha Player 020;Alpha Player 120;Alpha Player 220",
        "calc|Calculator;Calculator;Calculator Pro;My Calculator",
        "cal|Calculator;Calculator;Calculator Pro;Calendar;Alpha Calendar 070;Alpha Calendar 170;Alpha Calendar 270;Beta Calendar 071",
        "cam|Camera;Alpha Camera 060;Alpha Camera 160;Alpha Camera 260;Beta Camera 061;Beta Camera 161;Beta Camera 261;Delta Camera 063",
        "code|Code Editor;Visual Studio Code;VS Code",
        "cont|Contact Manager;Vector Notes 037;Vector Notes 137;Vector Notes 237",
        "doc|Document Viewer;Visual Studio Code;Studio Calendar 078;Studio Calendar 178;Studio Calendar 278;Studio Camera 068;Studio Camera 168;Studio Camera 268",
        "draw|Drawing Pad",
        "edit|Beta Editor 001;Alpha Editor 000;Alpha Editor 100;Alpha Editor 200;Beta Editor 101;Beta Editor 201;Code Editor;Delta Editor 003",
        "file|File Explorer",
        "graph|Graph Viewer",
        "mail|Mail;Gamma Terminal 052;Gamma Terminal 152;Gamma Terminal 252;Omega Terminal 054;Omega Terminal 154;Omega Terminal 254",
        "map|Maps;Gamma Player 022;Gamma Player 122;Gamma Player 222;Omega Player 024;Omega Player 124;Omega Player 224",
        "music|Music Player",
        "note|Note Pad;Notepad;Alpha Notes 030;Alpha Notes 130;Alpha Notes 230;Beta Notes 031;Beta Notes 131;Beta Notes 231",
        "photo|Photo Editor;Photos",
        "play|Alpha Player 020;Alpha Player 120;Alpha Player 220;Beta Player 021;Beta Player 121;Beta Player 221;Delta Player 023;Delta Player 123",
        "power|PowerPoint",
        "project|Project Planner",
        "search|Search Everything;Alpha Search 080;Alpha Search 180;Beta Search 081;Beta Search 181;Delta Search 083;Delta Search 183;Gamma Search 082",
        "set|Windows Settings;Search Everything;Studio Editor 008;Studio Editor 108;Studio Editor 208;Windows Editor 009;Windows Editor 109;Windows Editor 209",
        "studio|Studio Calendar 078;Studio Calendar 178;Studio Calendar 278;Studio Camera 068;Studio Camera 168;Studio Camera 268;Studio Editor 008;Studio Editor 108",
        "term|Alpha Terminal 050;Alpha Terminal 150;Alpha Terminal 250;Beta Terminal 051;Beta Terminal 151;Beta Terminal 251;Delta Terminal 053;Delta Terminal 153",
        "video|Video Player",
        "win|Windows Calendar 079;Windows Calendar 179;Windows Calendar 279;Windows Camera 069;Windows Camera 169;Windows Camera 269;Windows Editor 009;Windows Editor 109",
        "wsl|WSL;Windows Calendar 079;Windows Calendar 179;Windows Calendar 279;Windows Player 029;Windows Player 129;Windows Player 229;Windows Terminal",
        "cafe|Cafe;Café",
        "resume|Resume Builder;Résumé Builder",
        "vsc|Visual Studio Code;VS Code;Nova Search 085;Nova Search 185;Vector Search 087;Vector Search 187",
        "msw|Microsoft Word",
        "xyz|",
        "zzzz|",
        "calclator|Calculator;Calculator;Calculator Pro;My Calculator",
        "npt|",
        "aplication|Application Manager",
        "omega|Omega;Omega Calendar 074;Omega Calendar 174;Omega Calendar 274;Omega Camera 064;Omega Camera 164;Omega Camera 264;Omega Editor 004",
        "alpha|Alpha;Alpha Manager 240;Alpha Calendar 070;Alpha Calendar 170;Alpha Calendar 270;Alpha Camera 060;Alpha Camera 160;Alpha Camera 260",
    ];

    [Fact]
    public void CurrentTopEightNamesAndOrderAreStable()
    {
        AppSearch search = CreateSearch();
        var lines = new List<string>(Queries.Length);
        foreach (string query in Queries)
            lines.Add($"{query}|{string.Join(";", search.Find(query).Select(item => item.Name))}");
        Assert.Equal(Expected, lines);
    }

    private static AppSearch CreateSearch()
    {
        var search = new AppSearch();
        var items = new List<AppItem>(320);
        string[] prefixes = ["Alpha", "Beta", "Gamma", "Delta", "Omega", "Nova", "Pixel", "Vector", "Studio", "Windows"];
        string[] nouns = ["Editor", "Viewer", "Player", "Notes", "Manager", "Terminal", "Camera", "Calendar", "Search", "Tools"];
        for (int i = 0; i < 280; i++)
        {
            string name = $"{prefixes[i % prefixes.Length]} {nouns[(i / prefixes.Length) % nouns.Length]} {i:000}";
            items.Add(new AppItem(name, $"synthetic-{i}", $"synthetic-{i}"));
        }
        string[] special =
        [
            "Calculator", "Calculator", "Calendar", "Camera", "Café", "Cafe", "Résumé Builder",
            "Resume Builder", "Visual Studio Code", "Visual Studio", "VS Code", "Windows Terminal",
            "Windows Settings", "Microsoft Word", "Microsoft Store", "Mail", "Maps", "Music Player",
            "Photo Editor", "Photos", "PowerPoint", "Project Planner", "Search Everything",
            "App Installer", "Application Manager", "Code Editor", "Contact Manager", "Document Viewer",
            "Drawing Pad", "File Explorer", "Graph Viewer", "Notepad", "Note Pad", "Video Player",
            "WSL", "Alpha", "Omega", "WinLight", "Calculator Pro", "My Calculator"
        ];
        for (int i = 0; i < special.Length; i++)
            items.Add(new AppItem(special[i], $"special-{i}", $"special-{i}"));
        items.Add(new AppItem("Calculator Clone", "duplicate", "special-0"));
        search.SetItems(items);
        var usage = new Dictionary<string, UsageRecord>(StringComparer.OrdinalIgnoreCase)
        {
            ["special-1"] = new(20, new DateTimeOffset(2100, 1, 1, 0, 0, 0, TimeSpan.Zero)),
            ["special-8"] = new(12, new DateTimeOffset(2100, 1, 1, 0, 0, 0, TimeSpan.Zero)),
            ["special-10"] = new(8, new DateTimeOffset(2100, 1, 1, 0, 0, 0, TimeSpan.Zero)),
            ["special-14"] = new(6, new DateTimeOffset(2100, 1, 1, 0, 0, 0, TimeSpan.Zero)),
            ["special-35"] = new(18, new DateTimeOffset(2100, 1, 1, 0, 0, 0, TimeSpan.Zero)),
            ["synthetic-1"] = new(15, new DateTimeOffset(2100, 1, 1, 0, 0, 0, TimeSpan.Zero)),
            ["synthetic-240"] = new(10, new DateTimeOffset(2100, 1, 1, 0, 0, 0, TimeSpan.Zero))
        };
        search.LoadUsage(usage);
        return search;
    }
}
