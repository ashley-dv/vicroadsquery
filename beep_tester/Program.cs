using vicroadsquery;

var cfg = Config.Load();

Console.WriteLine($"Using beep configuration from {Config.CONFIG_PATH}.");

var warnBeep = cfg.WarningBeep;
var alertBeep = cfg.AlertBeep;

Console.WriteLine($"Warning Beep Configuration: {warnBeep.Repeats}x {warnBeep.Burst} beep(s) at {warnBeep.Frequency}Hz " +
                  $"for {warnBeep.DurationMs}ms each, with {warnBeep.BurstDelayMs} between burst beeps and {warnBeep.RepeatDelayMs} between bursts.");
Console.WriteLine($"Alert Beep Configuration: {alertBeep.Repeats}x {alertBeep.Burst} beep(s) at {alertBeep.Frequency}Hz " +
                  $"for {alertBeep.DurationMs}ms each, with {alertBeep.BurstDelayMs} between burst beeps and {alertBeep.RepeatDelayMs} between bursts.");

Console.WriteLine("Use this program to test audibility of the beep tones.");
Console.Write("Enter the amount of time in seconds to wait before playing tones: ");

var input = Console.ReadLine();
float waitSecs = 0;
if (!float.TryParse(input, out waitSecs))
    Console.WriteLine("Nothing or invalid float entered, playing immediately.");
else
{
    int waitMs = (int)(waitSecs * 1000);
    Console.WriteLine("Going to wait " + waitSecs + " seconds...");
    await Task.Delay(waitMs); 
}

Console.WriteLine("Now playing warning tone. You will only hear this when VicRoadsQuery is about to shut down.");
await DoAlert(cfg.WarningBeep);
Console.WriteLine("Now playing alert tone. You will only hear this when a viable appointment is found.");
await DoAlert(cfg.AlertBeep);
Console.Write("Press any key to exit.");
Console.ReadKey();

static async Task DoAlert(BeepInfo info)
{
    for (int i = 0; i < info.Repeats; i++)
    {
        for (int j = 0; j < info.Burst; j++)
        {
            Console.Beep(info.Frequency, info.DurationMs);
            await Task.Delay(info.BurstDelayMs);
        }

        await Task.Delay(info.RepeatDelayMs);
    }
}