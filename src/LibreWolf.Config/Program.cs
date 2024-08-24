using System.Text.RegularExpressions;

namespace LibreWolf.Config;

public static class Program
{
    // defaultPref, pref, lockPref
    private static readonly Regex PrefRegex = new(@"^(?<type>.+ref)\(""(?<key>.+)"",", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly int EpochSeconds = (int)(DateTime.Now - DateTime.UnixEpoch).TotalSeconds;
    
    public static async Task<int> Main(string[] args)
    {
        Console.WriteLine("Update Firefox Profile preferences with default LibreWolf configuration");

        var profileDirs = GetProfileDirs(args);
        if (profileDirs == null)
            return 1;

        var libreWolfConfig = await DownloadLibreWolfConfigAsync();
        if (string.IsNullOrEmpty(libreWolfConfig))
            return 1;

        var selectedProfileDirs = SelectFirefoxProfile(profileDirs.Value.ProfileDirs);
        if (selectedProfileDirs == null)
            return 1;
        
        var isDryRun = args.Contains("--dry-run");
        var libreWolfPreferences = ParseLibreWolfPreferences(libreWolfConfig);
        
        foreach (var selectedProfileDir in selectedProfileDirs)
        {
            var libreWolfPreferencesClone = new Dictionary<string, string>(libreWolfPreferences);
            if (!UpdateFirefoxProfile(libreWolfPreferencesClone, profileDirs.Value.Root, selectedProfileDir, isDryRun))
                return 1;
        }

        return 0;
    }

    private static (string Root, string[] ProfileDirs)? GetProfileDirs(string[] args)
    {
        var rootProfileDir = args.FirstOrDefault();
        if (!Directory.Exists(rootProfileDir))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("First argument must be a valid profile directory.");
            return null;
        }

        var profileDirs = Directory.GetDirectories(rootProfileDir);
        if (profileDirs.Length != 0)
            return (rootProfileDir, profileDirs);
        
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("No profiles found in the specified directory.");
        return null;
    }

    private static async Task<string> DownloadLibreWolfConfigAsync()
    {
        using var httpClient = new HttpClient();
        var response = await httpClient.GetAsync(
            "https://codeberg.org/librewolf/settings/raw/branch/master/librewolf.cfg");

        if (response.IsSuccessStatusCode) 
            return await response.Content.ReadAsStringAsync();
        
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("Error downloading librewolf.cfg");
        Console.WriteLine($"Status code: {response.StatusCode}");
        Console.WriteLine($"Status description: {response.ReasonPhrase}");
        return string.Empty;
    }

    private static string[]? SelectFirefoxProfile(string[] profileDirs)
    {
        Console.WriteLine("Which profile would you like to update?");
        Console.WriteLine("0. All profiles");
        for (var i = 0; i < profileDirs.Length; i++)
        {
            Console.WriteLine($"{i + 1}. {Path.GetFileName(profileDirs[i])}");
        }

        var input = Console.ReadLine();
        if (!int.TryParse(input, out var profileIndex) || profileIndex < 0 || profileIndex > profileDirs.Length)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Invalid input.");
            return null;
        }
        
        if (profileIndex != 0)
            profileDirs = new []{ profileDirs[profileIndex - 1] };

        return profileDirs;
    }
    
    private static IReadOnlyDictionary<string, string> ParseLibreWolfPreferences(string libreWolfConfig)
    {
        var libreWolfPreferences = new Dictionary<string, string>();

        var lines = libreWolfConfig.Split(Environment.NewLine);
        foreach (var line in lines)
        {
            var match = PrefRegex.Match(line);
            if (!match.Success) 
                continue;
            
            var key = match.Groups["key"].Value;
            if (key.Contains("librewolf", StringComparison.InvariantCultureIgnoreCase))
                continue;
            
            libreWolfPreferences[key] = line.Replace(match.Groups["type"].Value, "pref");
        }

        return libreWolfPreferences;
    }

    private static bool UpdateFirefoxProfile(Dictionary<string, string> libreWolfPreferences, string profileRootDir, string profileDir, bool isDryRun)
    {
        var preferencesFile = Path.Combine(profileRootDir, profileDir, "prefs.js");
        if (!File.Exists(preferencesFile))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"prefs.js not found in {profileDir}");
            return false;
        }
        
        if (!isDryRun)
        {
            var backupFile = $"{preferencesFile}.{EpochSeconds}.bak";
            File.Copy(preferencesFile, backupFile, true);
            Console.WriteLine("backed up prefs.js to {backupFile}");
        }
        
        var preferencesLines = File.ReadAllLines(preferencesFile).ToList();
        var overrideCount = 0;

        for (var i = 0; i < preferencesLines.Count; i++)
        {
            var preferenceLine = preferencesLines[i];
            
            var match = PrefRegex.Match(preferenceLine);
            if (!match.Success)
                continue;
            
            var key = match.Groups["key"].Value;
            if (!libreWolfPreferences.TryGetValue(key, out var pref)) 
                continue;
            
            preferencesLines[i] = pref;
            overrideCount++;
            libreWolfPreferences.Remove(key);
        }

        preferencesLines.AddRange(libreWolfPreferences.Values);

        if (!isDryRun)
            File.WriteAllLines(preferencesFile, preferencesLines);

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"Updated {profileDir}");
        Console.ResetColor();
        Console.WriteLine($"Overridden {overrideCount} preferences");
        Console.WriteLine($"Inserted {libreWolfPreferences.Count} new preferences");
        
        return true;
    }
}
