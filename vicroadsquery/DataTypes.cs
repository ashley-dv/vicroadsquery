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