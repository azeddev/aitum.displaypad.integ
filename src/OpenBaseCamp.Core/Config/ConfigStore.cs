using System.Text.Json;
using System.Text.Json.Serialization;
using OpenBaseCamp.Core.Model;

namespace OpenBaseCamp.Core.Config;

/// <summary>Loads and saves <see cref="AppConfig"/> and owns the on-disk layout.</summary>
public sealed class ConfigStore
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
        PropertyNameCaseInsensitive = true,
    };

    private readonly object _saveLock = new();

    public ConfigStore(string? rootDirectory = null)
    {
        RootDirectory = rootDirectory ?? DefaultRootDirectory();
        ImageLibraryDirectory = Path.Combine(RootDirectory, "images");
        ConfigFile = Path.Combine(RootDirectory, "config.json");
        LogDirectory = Path.Combine(RootDirectory, "logs");
    }

    public string RootDirectory { get; }

    public string ImageLibraryDirectory { get; }

    public string ConfigFile { get; }

    public string LogDirectory { get; }

    public static string DefaultRootDirectory() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.Create),
        "OpenBaseCamp");

    public AppConfig Load()
    {
        Directory.CreateDirectory(RootDirectory);
        Directory.CreateDirectory(ImageLibraryDirectory);

        AppConfig? config = null;
        if (File.Exists(ConfigFile))
        {
            try
            {
                config = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(ConfigFile), JsonOptions);
            }
            catch (Exception)
            {
                // Keep the unreadable file around rather than silently overwriting the user's work.
                TryBackupBrokenConfig();
            }
        }

        config ??= CreateDefault();
        config.Normalize();
        return config;
    }

    public void Save(AppConfig config)
    {
        config.Normalize();
        lock (_saveLock)
        {
            Directory.CreateDirectory(RootDirectory);
            var json = JsonSerializer.Serialize(config, JsonOptions);
            var temp = ConfigFile + ".tmp";
            File.WriteAllText(temp, json);

            // Atomic-ish replace so a crash mid-save cannot truncate the config.
            if (File.Exists(ConfigFile))
            {
                File.Replace(temp, ConfigFile, null);
            }
            else
            {
                File.Move(temp, ConfigFile);
            }
        }
    }

    public static AppConfig CreateDefault()
    {
        var profile = DefaultProfileFactory.CreateStarterProfile();
        return new AppConfig
        {
            Profiles = { profile },
            ActiveProfileId = profile.Id,
        };
    }

    /// <summary>Copies an image into the library and returns the file name to store in a key.</summary>
    public string ImportImage(string sourcePath)
    {
        Directory.CreateDirectory(ImageLibraryDirectory);

        var extension = Path.GetExtension(sourcePath);
        var stem = Path.GetFileNameWithoutExtension(sourcePath);
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            stem = stem.Replace(invalid, '_');
        }

        if (stem.Length > 40)
        {
            stem = stem[..40];
        }

        var name = $"{stem}-{Guid.NewGuid():N}{extension}";
        var destination = Path.Combine(ImageLibraryDirectory, name);
        File.Copy(sourcePath, destination, overwrite: true);
        return name;
    }

    public string? ResolveImage(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        if (Path.IsPathRooted(fileName))
        {
            return fileName;
        }

        var path = Path.Combine(ImageLibraryDirectory, fileName);
        return File.Exists(path) ? path : null;
    }

    public string ExportProfile(Profile profile, string destinationPath)
    {
        var json = JsonSerializer.Serialize(profile, JsonOptions);
        File.WriteAllText(destinationPath, json);
        return destinationPath;
    }

    public Profile ImportProfile(string path)
    {
        var profile = JsonSerializer.Deserialize<Profile>(File.ReadAllText(path), JsonOptions)
                      ?? throw new InvalidDataException("The file does not contain a profile.");

        // Re-key so importing the same profile twice cannot collide.
        var reIdentified = profile.Clone();
        reIdentified.Name = profile.Name;
        reIdentified.Normalize();
        return reIdentified;
    }

    private void TryBackupBrokenConfig()
    {
        try
        {
            var backup = ConfigFile + ".broken";
            File.Copy(ConfigFile, backup, overwrite: true);
        }
        catch (Exception)
        {
            // Best effort only.
        }
    }
}
