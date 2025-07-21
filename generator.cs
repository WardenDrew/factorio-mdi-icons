#!/usr/bin/dotnet run
#:property PublishAot=false
#:package Magick.NET-Q8-x64@14.7.0

/* * * * * * * * * * * * * *
 *  DEPENDENCIES
 * * * * * * * * * * * * * *
*/

using System.Text.Json;
using System.IO.Compression;
using ImageMagick;

/* * * * * * * * * * * * * *
 *  SETTINGS
 * * * * * * * * * * * * * *
*/

const string DEFAULT_SETTINGS_FILE = "generator.json";
const string LUA_TAB = "    ";
const string OPEN_BRACE = "{";
const string CLOSE_BRACE = "}";

GeneratorSettings? settings = null;
try
{
    string jsonGeneratorSettings = File.ReadAllText(DEFAULT_SETTINGS_FILE);
    settings = JsonSerializer.Deserialize<GeneratorSettings>(jsonGeneratorSettings);
    if (settings is null)
    {
        throw new NullReferenceException("Desrializing Failed!");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"Failed to load the settings file: {ex.Message}");
    return -1;
}

/* * * * * * * * * * * * * *
 *  PROCESS ICONS
 * * * * * * * * * * * * * *
*/

string? tempDirectory = null;
try
{
    tempDirectory = "temp";
    if (Directory.Exists(tempDirectory))
    {
        Directory.Delete(tempDirectory, true);
    }

    Directory.CreateDirectory(tempDirectory);
    Console.WriteLine($"Created Temporary Directory: {tempDirectory}");

    /* * * * * * * * * * * * * *
    *  SETUP DIST OUTPUT
    * * * * * * * * * * * * * *
    */

    string distPath = "dist";
    if (Directory.Exists(distPath))
    {
        Directory.Delete(distPath, true);
    }
    Directory.CreateDirectory(distPath);
    CopyFilesRecursively("src", distPath);


    /* * * * * * * * * * * * * *
    *  DOWNLOAD MDI LIBRARY
    * * * * * * * * * * * * * *
    */

    string cachePath = "cache";
    string cacheZipPath = Path.Join(cachePath, $"{settings.tag}.zip");
    string sourceZipPath = Path.Join(tempDirectory, "source.zip");

    if (!Directory.Exists(cachePath))
    {
        Console.WriteLine("Creating new Cache");
        Directory.CreateDirectory(cachePath);
    }

    if (File.Exists(cacheZipPath))
    {
        Console.WriteLine($"Loading from Cache: {cacheZipPath} -> {sourceZipPath}");
        File.Copy(cacheZipPath, sourceZipPath);
    }
    else
    {
        string downloadUri = $"{settings.repo}/archive/refs/tags/{settings.tag}.zip";
        Console.WriteLine($"Downloading: {downloadUri} -> {sourceZipPath}");
        using (HttpClient httpClient = new())
        {
            using var downloadStream = await httpClient.GetStreamAsync(downloadUri);
            using var saveStream = new FileStream(
                sourceZipPath,
                FileMode.Create,
                FileAccess.Write);

            await downloadStream.CopyToAsync(saveStream);
            await saveStream.FlushAsync();
            saveStream.Close();
        }
        Console.WriteLine("Download Complete! Copying to Cache");

        File.Copy(sourceZipPath, cacheZipPath);
    }


    /* * * * * * * * * * * * * *
    *  EXTRACT MDI LIBRARY
    * * * * * * * * * * * * * *
    */

    string sourcePath = Path.Join(tempDirectory, "source");
    Console.WriteLine($"Extracting: {sourceZipPath} -> {sourcePath}");
    ZipFile.ExtractToDirectory(sourceZipPath, sourcePath);
    Console.WriteLine("Extract Complete!");

    /* * * * * * * * * * * * * *
    *  CONVERT SVG to PNG
    * * * * * * * * * * * * * *
    */

    List<string> zipPathParts = new();
    zipPathParts.Add(sourcePath);
    zipPathParts.AddRange(settings.zipPath.ToList());

    string svgFolderPath = Path.Join(zipPathParts.ToArray());
    string rasterizedFolderPath = Path.Join(distPath, "graphics", "signal");
    Directory.CreateDirectory(rasterizedFolderPath);
    List<string> svgFilePaths = Directory.GetFiles(svgFolderPath, "*.svg")
        .ToList();

    string langFileFolderPath = Path.Join(distPath, "locale", "en");
    Directory.CreateDirectory(langFileFolderPath);

    Console.WriteLine($"Converting {svgFilePaths.Count} SVG Files");

    var svgReadSettings = new MagickReadSettings
    {
        Format = MagickFormat.Svg,
        Width = 1024,
        Height = 1024,
        Density = new Density(300)
    };

    HashSet<string> subGroups = [];
    string signalsFile = "data:extend({";
    string langFile = "[item-group-name]\nmdi-signals=Material Design Icon Signals\n[virtual-signal-name]";
    foreach (string svgFilePath in svgFilePaths)
    {

        string svgFileName = Path.GetFileName(svgFilePath);
        string iconName = "mdi-" + svgFileName[..^4];
        string subGroup = string.Join("-", iconName.Split('-').Take(2).ToArray());
        string pngFilePath = Path.Join(rasterizedFolderPath, iconName + ".png");
        Console.WriteLine($"Converting: [{subGroup}] {svgFileName} -> {pngFilePath}");

        subGroups.Add(subGroup);

        using var svg = new MagickImage(svgFilePath, svgReadSettings);
        svg.BackgroundColor = MagickColors.White;

        using var image = svg.CloneArea(1024, 1024);
        image.Negate(Channels.RGB);

        var imageCollection = new MagickImageCollection();

        using var image64 = image.Clone();
        image64.Resize(64, 64);
        image64.Format = MagickFormat.Png;
        imageCollection.Add(image64);

        using var image32 = image.Clone();
        image32.Resize(32, 32);
        image32.Format = MagickFormat.Png;
        imageCollection.Add(image32);

        using var image16 = image.Clone();
        image16.Resize(16, 16);
        image16.Format = MagickFormat.Png;
        imageCollection.Add(image16);

        using var image8 = image.Clone();
        image8.Resize(8, 8);
        image8.Format = MagickFormat.Png;
        imageCollection.Add(image8);

        using var imageFinal = imageCollection.AppendHorizontally();
        imageFinal.Write(pngFilePath);

        signalsFile += $"\n" +
            $"{LUA_TAB}{OPEN_BRACE}\n" +
            $"{LUA_TAB}{LUA_TAB}type = \"virtual-signal\",\n" +
            $"{LUA_TAB}{LUA_TAB}name = \"signal-{iconName}\",\n" +
            $"{LUA_TAB}{LUA_TAB}icon = \"__factorio-mdi-signals__/graphics/signal/{iconName}.png\",\n" +
            $"{LUA_TAB}{LUA_TAB}subgroup = \"{subGroup}\"\n" +
            $"{LUA_TAB}{CLOSE_BRACE},";

        langFile += $"\nsignal-{iconName}={iconName}";
    }

    signalsFile = signalsFile[..^1] + "\n})";
    File.WriteAllText(Path.Join(distPath, "signals.lua"), signalsFile);

    File.WriteAllText(Path.Join(langFileFolderPath, "mdi_signals.cfg"), langFile);

    string groupsFile = "data:extend({";
    foreach (string subGroup in subGroups)
    {
        groupsFile += $"\n" +
            $"{LUA_TAB}{OPEN_BRACE}\n" +
            $"{LUA_TAB}{LUA_TAB}type = \"item-subgroup\",\n" +
            $"{LUA_TAB}{LUA_TAB}name = \"{subGroup}\",\n" +
            $"{LUA_TAB}{LUA_TAB}group = \"mdi-signals\"\n" +
            $"{LUA_TAB}{CLOSE_BRACE},";
    }
    groupsFile = groupsFile[..^1] + "\n})";
    File.WriteAllText(Path.Join(distPath, "groups.lua"), groupsFile);
}
catch (Exception ex)
{
    Console.WriteLine(ex);
}
finally
{
    if (tempDirectory is not null)
    {
        Directory.Delete(tempDirectory, true);
        Console.WriteLine($"Deleted Temporary Directory: {tempDirectory}");
    }
}


return 0;

/* * * * * * * * * * * * * *
 *  END OF PROGRAM
 * * * * * * * * * * * * * *
*/

/* * * * * * * * * * * * * *
 *  METHODS
 * * * * * * * * * * * * * *
*/

void CopyFilesRecursively(string sourcePath, string targetPath)
{
    foreach (string dirPath in Directory.GetDirectories(sourcePath, "*", SearchOption.AllDirectories))
        Directory.CreateDirectory(dirPath.Replace(sourcePath, targetPath));

    foreach (string newPath in Directory.GetFiles(sourcePath, "*.*", SearchOption.AllDirectories))
        File.Copy(newPath, newPath.Replace(sourcePath, targetPath), true);
}

/* * * * * * * * * * * * * *
 *  TYPES
 * * * * * * * * * * * * * *
*/

record GeneratorSettings(string repo, string tag, List<string> zipPath);
