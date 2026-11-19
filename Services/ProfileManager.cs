using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MapCrafter.Models;
using Newtonsoft.Json;

namespace MapCrafter.Services
{
    public static class ProfileManager
    {
        public static List<CraftingProfile> LoadProfiles(string basePluginDir)
        {
            var profiles = new List<CraftingProfile>();
            string profilesDir = Path.Combine(basePluginDir, "Profiles");

            if (!Directory.Exists(profilesDir))
                Directory.CreateDirectory(profilesDir);

            // Load all profile JSON files from local Profiles directory
            foreach (var file in Directory.GetFiles(profilesDir, "*.json"))
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var prof = JsonConvert.DeserializeObject<CraftingProfile>(json);
                    if (prof != null && !string.IsNullOrWhiteSpace(prof.Name))
                    {
                        profiles.Add(prof);
                    }
                }
                catch { }
            }

            // Ensure test profile exists
            if (!profiles.Any(p => p.Name.Contains("Test Profile", StringComparison.OrdinalIgnoreCase)))
            {
                var test = CreateTestProfile();
                profiles.Insert(0, test);
                SaveProfile(test, basePluginDir);
            }

            // Always prioritize Test Profile first in the list
            var testProfile = profiles.FirstOrDefault(p => p.Name.Contains("Test Profile", StringComparison.OrdinalIgnoreCase));
            if (testProfile != null)
            {
                profiles.Remove(testProfile);
                profiles.Insert(0, testProfile);
            }

            return profiles;
        }

        public static void SaveProfile(CraftingProfile profile, string basePluginDir)
        {
            try
            {
                var dirs = new List<string> { Path.Combine(basePluginDir, "Profiles") };
                string srcDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plugins", "Source", "MapCrafter", "Profiles");
                if (srcDir != dirs[0]) dirs.Add(srcDir);

                string safeName = string.Join("_", profile.Name.Split(Path.GetInvalidFileNameChars()));
                string json = JsonConvert.SerializeObject(profile, Formatting.Indented);

                foreach (var dir in dirs)
                {
                    if (!Directory.Exists(dir))
                        Directory.CreateDirectory(dir);
                    string path = Path.Combine(dir, $"{safeName}.json");
                    File.WriteAllText(path, json);
                }
            }
            catch { }
        }

        public static void DeleteProfile(CraftingProfile profile, string basePluginDir)
        {
            try
            {
                var dirs = new List<string> { Path.Combine(basePluginDir, "Profiles") };
                string srcDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plugins", "Source", "MapCrafter", "Profiles");
                if (srcDir != dirs[0]) dirs.Add(srcDir);

                string safeName = string.Join("_", profile.Name.Split(Path.GetInvalidFileNameChars()));
                foreach (var dir in dirs)
                {
                    string path = Path.Combine(dir, $"{safeName}.json");
                    if (File.Exists(path))
                        File.Delete(path);
                }
            }
            catch { }
        }

        public static CraftingProfile CreateTestProfile()
        {
            var prof = new CraftingProfile
            {
                Name = "Test Profile (Beyond / Mirage / Wildwood)",
                ChiselMaps = true,
                TargetQuality = 20,
                Method = RerollMethod.ChaosOrb,
                ExaltSlam = false,
                MaxAttemptsPerMap = 60,
                RequireAllRequiredMods = false
            };
            prof.BannedMods.Add("cannot Regenerate");
            prof.BannedMods.Add("less effect of flasks");
            prof.RequiredMods.Add("Beyond");
            prof.RequiredMods.Add("Viridian Wildwood");
            prof.RequiredMods.Add("Mirage");
            return prof;
        }
    }
}
