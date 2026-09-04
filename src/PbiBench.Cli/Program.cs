using System.Text.Json;
using PbiBench.Workspace;

if (args.Length == 0)
{
    Console.WriteLine("PbiBench CLI V5\n  scan <folder>\n  doctor\n  capabilities");
    return 2;
}

switch(args[0].ToLowerInvariant())
{
    case "scan" when args.Length > 1:
        Console.WriteLine(JsonSerializer.Serialize(new PbipWorkspaceScanner().Scan(args[1]), new JsonSerializerOptions{WriteIndented=true}));
        return 0;
    case "doctor":
        foreach(var exe in new[]{"git","node","npm","npx","daxstudio.exe","dscmd.exe"}) Console.WriteLine($"{exe,-16} {(Find(exe) ?? "not found on PATH")}");
        return 0;
    case "capabilities":
        Console.WriteLine("local-pbip     scaffold\nfabric-rest    scaffold\npowerbi-rest   scaffold\nxmla-tom       contract-only\ndaxstudio      scaffold\ngit            scaffold\nui             scaffold");
        return 0;
    default:
        return 2;
}

static string? Find(string exe)
{
    var path=Environment.GetEnvironmentVariable("PATH")??"";
    foreach(var dir in path.Split(Path.PathSeparator,StringSplitOptions.RemoveEmptyEntries))
    {
        try{var p=Path.Combine(dir,exe);if(File.Exists(p))return p;}catch{}
    }
    return null;
}
