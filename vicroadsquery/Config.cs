using System.Data;

namespace vicroadsquery;

using System;
using System.IO;
using Newtonsoft.Json;

public class Config
{
    public const string CONFIG_PATH = "vicroadsquery.config.json";
    public const string OFFICES_CONFIG_PATH = "vicroadsquery.config.offices.json";
    
    public string LogFilePath = "vicroadsquery.log.txt";
    public int QueryDelayMs = 30000;
    public int RetryDelayMs = 5000;
    public int MaxRetryAttempts = 5;
    public bool PrintResponseSummaries = false;
    public bool AlreadyBooked = false;
    public string LicenseNumber = "012345678";
    public string LastName = "LE SMITH";

    public DateTime MinAlertDate = new DateTime(2022, 4, 20);
    public DateTime MaxAlertDate = new DateTime(2022, 5, 15);
    public TimeSpan MinAlertTime = new TimeSpan(9, 15, 0);
    public TimeSpan MaxAlertTime = new TimeSpan(15, 0, 0);
    public bool TimeRangeExclusive = true;

    public string[] OfficesToQuery = Array.Empty<string>();

    public BeepInfo AlertBeep = new BeepInfo(5, 2500, 75, 3, 500, 0);
    public BeepInfo WarningBeep = new BeepInfo(5, 250, 600, 1, 100, 0);

    public static Config GetDefaults()
    {
        return new Config();
    }

    public bool IsDefault()
    {
        var comparison = GetDefaults();
        return LicenseNumber == comparison.LicenseNumber
               && LastName == comparison.LastName;
    }

    public static bool TryLoad(out Config cfg)
    {
        cfg = GetDefaults();

        if (!File.Exists(CONFIG_PATH))
            File.WriteAllText(CONFIG_PATH, JsonConvert.SerializeObject(cfg, Formatting.Indented));
        else
        {
            try
            {
                var file = File.ReadAllText(CONFIG_PATH);
                var jsonCfg = JsonConvert.DeserializeObject<Config>(file);
                cfg = jsonCfg?? cfg;
                return jsonCfg != null;
            }
            catch (Exception ex)
            {
                // Error parsing config - defaults will be used.
            }
        }

        return false;
    }
    
    public static Config Load()
    {
        var cfg = GetDefaults();

        if (!File.Exists(CONFIG_PATH))
            File.WriteAllText(CONFIG_PATH, JsonConvert.SerializeObject(cfg, Formatting.Indented));
        else
        {
            try
            {
                var file = File.ReadAllText(CONFIG_PATH);
                cfg = JsonConvert.DeserializeObject<Config>(file) ?? cfg;
            }
            catch (Exception ex)
            {
                // Error parsing config - defaults will be used.
            }
        }

        return cfg;
    }

    public static Dictionary<string, VicRoadsOffice> LoadOffices()
    {
        Dictionary<string, VicRoadsOffice> ret = new Dictionary<string, VicRoadsOffice>();

        if (!File.Exists(OFFICES_CONFIG_PATH))
        {
            File.WriteAllText(OFFICES_CONFIG_PATH, string.Empty);
            Program.Log("Offices file not found, creating one.");
        }
        else
        {
            try
            {
                var file = File.ReadAllText(OFFICES_CONFIG_PATH);
                var raw = JsonConvert.DeserializeObject<VicRoadsOffice[]>(file);

                if (raw != null)
                    foreach (var i in raw)
                        ret.Add(i.ShortName.ToLowerInvariant(), i);
            }
            catch (Exception ex)
            {
                // Error parsing config - defaults will be used.
            }
        }

        return ret;
    }

    public static void Save(Config config)
    {
        File.WriteAllText(CONFIG_PATH, JsonConvert.SerializeObject(config, Formatting.Indented));
    }
    
    public static void SaveOffices(VicRoadsOffice[] offices)
    {
        File.WriteAllText(OFFICES_CONFIG_PATH, JsonConvert.SerializeObject(offices, Formatting.Indented));
    }
}