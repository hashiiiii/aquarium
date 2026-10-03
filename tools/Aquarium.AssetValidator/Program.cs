using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

var root = FindRepositoryRoot();
var assets = Path.Combine(root, "unity", "Assets");
var aquarium = Path.Combine(assets, "Aquarium");
var errors = new List<string>();
var guids = new Dictionary<string, string>(StringComparer.Ordinal);
var builtInGuids = new HashSet<string>(StringComparer.Ordinal)
{
    "0000000000000000f000000000000000",
    "0000000000000000e000000000000000"
};

foreach (var meta in Directory.EnumerateFiles(assets, "*.meta", SearchOption.AllDirectories))
{
    var contents = File.ReadAllText(meta);
    var match = Regex.Match(contents, "^guid: ([0-9a-f]{32})$", RegexOptions.Multiline);
    if (!match.Success)
    {
        errors.Add($"Invalid GUID: {Relative(meta)}");
        continue;
    }

    var guid = match.Groups[1].Value;
    if (guids.TryGetValue(guid, out var previous))
        errors.Add($"Duplicate GUID: {Relative(meta)} and {Relative(previous)}");
    else
        guids.Add(guid, meta);
}

foreach (var path in new[] { aquarium }.Concat(Directory.EnumerateFileSystemEntries(aquarium, "*", SearchOption.AllDirectories)))
{
    if (path.EndsWith(".meta", StringComparison.Ordinal)) continue;
    if (!File.Exists(path + ".meta")) errors.Add($"Missing .meta: {Relative(path)}");
}

foreach (var meta in Directory.EnumerateFiles(aquarium, "*.meta", SearchOption.AllDirectories))
    if (!File.Exists(meta[..^5]) && !Directory.Exists(meta[..^5]))
        errors.Add($"Orphan .meta: {Relative(meta)}");

var assemblies = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
foreach (var path in Directory.EnumerateFiles(aquarium, "*.asmdef", SearchOption.AllDirectories))
{
    using var document = JsonDocument.Parse(File.ReadAllText(path));
    var name = document.RootElement.GetProperty("name").GetString()!;
    if (!assemblies.TryAdd(name, document.RootElement.Clone())) errors.Add($"Duplicate assembly: {name}");
}

foreach (var (name, data) in assemblies)
{
    if (!data.TryGetProperty("references", out var references)) continue;
    foreach (var reference in references.EnumerateArray().Select(item => item.GetString() ?? ""))
        if (reference.StartsWith("Aquarium.", StringComparison.Ordinal) && !assemblies.ContainsKey(reference))
            errors.Add($"Unresolved assembly: {name} -> {reference}");
}

var sceneNames = new[] { "Title.unity", "Home.unity", "AquariumOnline.unity" };
foreach (var sceneName in sceneNames)
{
    var scene = Path.Combine(aquarium, "Scenes", sceneName);
    if (!File.Exists(scene))
    {
        errors.Add($"Missing scene: {sceneName}");
        continue;
    }

    foreach (Match match in Regex.Matches(File.ReadAllText(scene), "guid: ([0-9a-f]{32})"))
    {
        var guid = match.Groups[1].Value;
        if (!guids.ContainsKey(guid) && !builtInGuids.Contains(guid))
            errors.Add($"Unresolved {sceneName} GUID: {guid}");
    }
}

var buildSettings = File.ReadAllText(Path.Combine(root, "unity", "ProjectSettings", "EditorBuildSettings.asset"));
if (!buildSettings.Contains("- enabled: 1\n    path: Assets/Aquarium/Scenes/Title.unity", StringComparison.Ordinal))
    errors.Add("Title scene is not enabled as the first build scene");

foreach (var shader in new[] { "ReefSolid", "ReefSprite" })
    if (!File.Exists(Path.Combine(aquarium, "Presentation", "Resources", shader + ".shader")))
        errors.Add($"Missing runtime-retained shader: {shader}");

var linker = XDocument.Load(Path.Combine(aquarium, "Presentation", "link.xml"));
var preservedTypes = linker.Descendants("type").Select(node => (string?)node.Attribute("fullname")).ToHashSet(StringComparer.Ordinal);
foreach (var component in new[] { "MeshFilter", "MeshRenderer", "BoxCollider", "SphereCollider", "CapsuleCollider", "MeshCollider" })
    if (!preservedTypes.Contains("UnityEngine." + component))
        errors.Add($"Missing primitive stripping protection: {component}");

if (errors.Count > 0)
{
    Console.Error.WriteLine(string.Join(Environment.NewLine, errors));
    return 1;
}

Console.WriteLine($"PASS: {guids.Count} asset GUIDs, {assemblies.Count} assemblies, title/home/aquarium scenes and shader resources");
return 0;

string Relative(string path) => Path.GetRelativePath(root, path);

static string FindRepositoryRoot()
{
    for (var directory = new DirectoryInfo(Environment.CurrentDirectory); directory != null; directory = directory.Parent)
        if (Directory.Exists(Path.Combine(directory.FullName, "unity", "Assets"))) return directory.FullName;
    throw new DirectoryNotFoundException("Could not find repository root containing unity/Assets.");
}
