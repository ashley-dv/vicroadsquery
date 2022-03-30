Console.WriteLine("Use this program to test audibility of the beep tones.");
Console.Write("Enter the amount of time in seconds to wait before playing tones: ");

float waitSecs = float.Parse(Console.ReadLine() ?? string.Empty);
int waitMs = (int)(waitSecs * 1000);

Console.WriteLine("Going to wait " + waitSecs + " seconds...");
await Task.Delay(waitMs);

Console.WriteLine("Now playing warning tone. You will only hear this when VicRoadsQuery is about to shut down.");
await DoWarningAlert();
Console.WriteLine("Now playing alert tone. You will only hear this when a viable appointment is found.");
await DoSuccessAlert();
Console.Write("Press any key to exit.");
Console.ReadKey();

static async Task DoWarningAlert()
{
    for (int i = 0; i < 5; i++)
    {
        Console.Beep(250, 600);
        await Task.Delay(100);
    }
}

static async Task DoSuccessAlert()
{
    for (int i = 0; i < 5; i++)
    {
        for (int j = 0; j < 3; j++)
            Console.Beep(1500, 50);

        await Task.Delay(500);
    }
}