namespace vicroadsquery;

public class VicRoadsVerifyResponse
{
    public int Response;
    public VicRoadsError[] Errors;
}

public class VicRoadsError
{
    public string Message;
    public string PropertyName;
}

public class VicRoadsAppointmentRaw
{
    public int Response;
    public VicRoadsBookingDateRaw[] Data;
    public VicRoadsError[] Errors;
}

public class VicRoadsBookingDateRaw
{
    public VicRoadsBookingTimeRaw[] Slots;
    public string Date;
    public string DateDisplay;
    public bool IsInitialSearchDate;
}

public class VicRoadsBookingTimeRaw
{
    public string DisplayDate;
    public string DisplayTime;
    public string SlotDate;
    public string DateFormatted;
}

public class VicRoadsOffice
{
    public int Id;
    public string Name;
    public string ShortName;
    public string Address;
    public string Suburb;
    public string State;
    public int Postcode;
    public double Latitude;
    public double Longitude;
}

public class BeepInfo
{
    public int Repeats;
    public int Frequency;
    public int DurationMs;
    public int Burst;

    public int RepeatDelayMs;
    public int BurstDelayMs;

    public BeepInfo()
    {
        
    }

    public BeepInfo(int reps, int freq, int durMs, int burst, int repDelayMs, int burstDelayMs)
    {
        Repeats = reps;
        Frequency = freq;
        DurationMs = durMs;
        Burst = burst;
        RepeatDelayMs = repDelayMs;
        BurstDelayMs = burstDelayMs;
    }
}