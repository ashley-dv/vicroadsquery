// POST TO: https://billing.vicroads.vic.gov.au/bookings/Appointment/GetAppointmentTimes

using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Mime;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using vicroadsquery;

public class Program
{
    private static Config Config;
    private static Dictionary<string, VicRoadsOffice> Offices;
    private static DateTime UtcStartTime;

    public static async Task Main(string[] args)
    {
        try
        {
            Log($"Attempting to read config...");
            Config = Config.Load();
            Log($"Config successfully loaded.");
            
            Log("Attempting to read offices config...");
            Offices = Config.LoadOffices();
            Log($"Successfully loaded {Offices.Count} offices.");

            List<(int, string)> queryOffices = new List<(int, string)>();

            string officeList = string.Empty;

            // NOTE: Office shortnames are read in and made lowercase in Config.cs to remove case sensitivity.
            foreach (var i in Config.OfficesToQuery)
            {
                if (Offices.TryGetValue(i.ToLowerInvariant(), out var office))
                {
                    queryOffices.Add((office.Id, office.ShortName));
                    officeList += i + ", ";
                }
                else
                    Log($"WARNING: No office found by the shortname {i}.");
            }

            if (queryOffices.Count <= 0)
            {
                Log("ERROR: No offices read in. Please ensure offices are configured correctly.");
                Log("Program will exit in 5 seconds.");
                await Task.Delay(5000);
                return;
            }

            Log($"Going to query {Config.OfficesToQuery.Length} office(s): {officeList.Remove(officeList.Length - 2)}.");

            Log($"Using license number {Config.LicenseNumber} and last name {Config.LastName}.");
            Log(
                $"You will be alerted to any appointments from {Config.MinAlertDate.ToString("d")} - {Config.MaxAlertDate.ToString("d")}.");

            Log(Config.TimeRangeExclusive
                ? $"You will only be alerted to appointments taking place before or at {Config.MinAlertTime.ToString("h\\:mm")} and after or at {Config.MaxAlertTime.ToString("h\\:mm")}."
                : $"You will only be alerted to appointments taking place between {Config.MinAlertTime.ToString("h\\:mm")} and {Config.MaxAlertTime.ToString("h\\:mm")}, inclusive.");

            UtcStartTime = DateTime.UtcNow;
            UpdateTitleLoop();

            HttpClientHandler handler = new HttpClientHandler()
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                AllowAutoRedirect = true,
                UseCookies = true,
                CookieContainer = new CookieContainer()
            };

            HttpClient client = new HttpClient(handler);

            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Add("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/99.0.4844.84 Safari/537.36");
            client.DefaultRequestHeaders.Add("Accept", "application/json, text/javascript, */*; q=0.01");
            client.DefaultRequestHeaders.Add("Accept-Encoding", "gzip, deflate, br");
            client.DefaultRequestHeaders.Add("Accept-Language", "en-GB,en-US;q=0.9,en;q=0.8");

            client.DefaultRequestHeaders.Add("Origin", "https://billing.vicroads.vic.gov.au");
            client.DefaultRequestHeaders.Add("Host", "billing.vicroads.vic.gov.au");
            client.DefaultRequestHeaders.Add("Connection", "keep-alive");
            client.DefaultRequestHeaders.Add("Referrer",
                "https://billing.vicroads.vic.gov.au/bookings/Appointment/AppointmentSearch");
            client.DefaultRequestHeaders.Add("sec-ch-ua",
                "\"Not A;Brand\";v=\"99\", \"Chromium\";v=\"99\", \"Google Chrome\";v=\"99\"");
            client.DefaultRequestHeaders.Add("sec-ch-ua-mobile", "?0");
            client.DefaultRequestHeaders.Add("sec-ch-ua-platform", "\"Windows\"");
            client.DefaultRequestHeaders.Add("Sec-Fetch-Dest", "empty");
            client.DefaultRequestHeaders.Add("Sec-Fetch-Mode", "cors");
            client.DefaultRequestHeaders.Add("Sec-Fetch-Site", "same-origin");

            int retryAttempts = 0;

            while (retryAttempts < Config.MaxRetryAttempts)
            {
                Log("Getting Manage/Details to retrieve TtyghsS...");
                var ttyghssThingHtml =
                    await client.GetAsync("https://billing.vicroads.vic.gov.au/bookings/Manage/Details");
                var ttyghssThingHtmlResponseLines =
                    WebUtility.HtmlDecode(ttyghssThingHtml.Content.ReadAsStringAsync().Result).Split("\n");
                Log("TtyghsS response received.");

                string ttyghss = string.Empty;
                // extract ttyghss from html response.
                foreach (var i in ttyghssThingHtmlResponseLines)
                {
                    if (i.Contains("TtyghsS"))
                    {
                        if (ttyghss != string.Empty)
                            Log("WARNING: Duplicate TtyghsS?");

                        var match = Regex.Match(i, "value=\"([0-9]*)\"");
                        if (match.Groups.Count > 0)
                            ttyghss = match.Groups[0].Value.Substring("value=".Length).Trim('"');
                    }
                }

                if (String.IsNullOrWhiteSpace(ttyghss))
                    Log("WARNING: TtyghsS is empty! Will likely mean program will not work.");
                else
                    Log($"Extracted TtyghsS: {ttyghss}");

                var verifyValues = new Dictionary<string, string>
                {
                    {"VerificationToken", "0"},
                    {"clientId", Config.LicenseNumber},
                    {"familyNameOne", Config.LastName},
                    {"TtyghsS", ttyghss}
                };

                var verifyContent = new FormUrlEncodedContent(verifyValues);
                verifyContent.Headers.Clear();
                verifyContent.Headers.Add("Content-Type", "application/x-www-form-urlencoded; charset=UTF-8");

                Log("Sending details...");
                var verifyResponse = await client.PostAsync(
                    "https://billing.vicroads.vic.gov.au/bookings/Manage/Details",
                    verifyContent);
                var verifyResponseStr = verifyResponse.Content.ReadAsStringAsync().Result;
                var vicRoadsVerifyResponse = JsonConvert.DeserializeObject<VicRoadsVerifyResponse>(verifyResponseStr);
                Log("Details response received.");

                if (vicRoadsVerifyResponse == null || vicRoadsVerifyResponse.Response != 1)
                {
                    // print errors, maybe beep
                    retryAttempts++;
                    Log(
                        $"ERROR: Non successful response received. Retrying in 5 seconds (Attempt {retryAttempts}/{Config.MaxRetryAttempts}).");
                    await Task.Delay(5000);
                    if (retryAttempts >= 5)
                        await DoWarningAlert();
                    continue;
                }

                Log("Getting Manage/Appointments to retrieve verification token...");
                var verificationTokenHtml =
                    await client.GetAsync("https://billing.vicroads.vic.gov.au/bookings/Manage/Appointments");
                var verificationTokenHtmlResponseLines =
                    WebUtility.HtmlDecode(verificationTokenHtml.Content.ReadAsStringAsync().Result).Split("\n");
                string verificationToken = string.Empty;
                Log("Verification token response received.");

                // extract verification token from html response.
                foreach (var i in verificationTokenHtmlResponseLines)
                {
                    if (i.Contains("VerificationToken"))
                    {
                        var match = Regex.Match(i, "value=\"([0-9]*)\"");
                        if (match.Groups.Count > 0)
                            verificationToken = match.Groups[0].Value.Substring("value=".Length).Trim('"');
                    }
                }

                if (String.IsNullOrWhiteSpace(verificationToken))
                    Log("WARNING: Verification token is empty! Will likely mean program will not work.");
                else
                    Log($"Extracted verification token: {verificationToken}");

                bool looping = true;
                while (looping)
                {
                    foreach (var office in queryOffices)
                    {
                        var appointmentQueryValues = new Dictionary<string, string>
                        {
                            {"VerificationToken", verificationToken},
                            {"startDate", Config.MinAlertDate.ToString("yyyy-MM-dd")},
                            // uncomment to not care about a min alert date
                            //{ "startDate", DateTime.Now.AddDays(2).ToString("yyyy-MM-dd") },
                            {"officeId", office.Item1.ToString()}
                        };

                        var content = new FormUrlEncodedContent(appointmentQueryValues);
                        content.Headers.Clear();
                        content.Headers.Add("Content-Type", "application/x-www-form-urlencoded; charset=UTF-8");

                        Log($"Sending appointment times request for {office.Item2}...");
                        var response = await client.PostAsync(
                            "https://billing.vicroads.vic.gov.au/bookings/Appointment/GetAppointmentTimes",
                            content);
                        Log("Appointment times response received.");

                        var responseStr = response.Content.ReadAsStringAsync().Result;

                        var apptsRaw = JsonConvert.DeserializeObject<VicRoadsAppointmentRaw>(responseStr);
                        
                        // KNOWN RESPONSE CODES:
                        // 1: Appointments found
                        // 2: No bookings for the specified time
                        // 3: Booking session expired
                        foreach (var i in apptsRaw.Errors)
                            Log($"VicRoads Error: {i.Message}.");

                        bool success = false;
                        
                        switch (apptsRaw.Response)
                        {
                            case 1:
                                success = true;
                                break;
                            case 2:
                                Log($"No bookings found for the specified times. Skipping.");
                                break;
                            default:
                                retryAttempts++;
                                Log($"Invalid response [code: {apptsRaw.Response}]. Resetting. (Attempt {retryAttempts}/{Config.MaxRetryAttempts}).");
                                if (retryAttempts >= 5)
                                    await DoWarningAlert();

                                looping = false;
                                break;
                            
                        }

                        if (!success)
                        {
                            await Task.Delay(Config.QueryDelayMs);
                            continue;
                        }

                        retryAttempts = 0;

                        bool successAlert = false;

                        StringBuilder apptsSummary = new StringBuilder();
                        int appointmentsTotal = 0;

                        foreach (var bookingDate in apptsRaw.Data)
                        {
                            var unixTimeStamp = bookingDate.Date.Substring(6);
                            unixTimeStamp = unixTimeStamp.Substring(0, unixTimeStamp.Length - 2);
                            var dateTime = UnixTimeStampToDateTime(unixTimeStamp);

                            var slots = bookingDate.Slots;
                            if (slots.Length <= 0)
                                continue;

                            appointmentsTotal += slots.Length;

                            if (Config.PrintResponseSummaries)
                                apptsSummary.Append($"{bookingDate.DateDisplay}: {bookingDate.Slots.Length}, ");

                            if (dateTime > Config.MaxAlertDate)
                                continue;

                            bool goodApptFound = false;

                            List<string> outputs = new List<string>();
                            foreach (var slot in slots)
                            {
                                DateTime timeParseDt = DateTime.ParseExact(slot.DisplayTime,
                                    "h:mm tt", CultureInfo.InvariantCulture);
                                TimeSpan span = timeParseDt.TimeOfDay;
                                if (Config.TimeRangeExclusive && (span <= Config.MinAlertTime || span >= Config.MaxAlertTime)
                                    || !Config.TimeRangeExclusive && span >= Config.MinAlertTime && span <= Config.MaxAlertTime)
                                {
                                    outputs.Add("VIABLE: " + slot.DisplayTime + " on " + slot.DisplayDate);
                                    successAlert = goodApptFound = true;
                                }
                            }

                            if (goodApptFound)
                            {
                                Log($"Found {outputs.Count} viable appointment(s) for {office.Item2} on " +
                                    dateTime.ToString("D"));
                                foreach (var i in outputs)
                                    Log(i);
                            }
                        }

                        if (Config.PrintResponseSummaries)
                        {
                            Log("-------- RESPONSE SUMMARY BELOW --------");
                            Log(apptsSummary.ToString());
                            Log("-------- RESPONSE SUMMARY ENDED --------");
                        }



                        if (successAlert)
                            await DoSuccessAlert();
                        else
                            Log($"No viable appointments found (out of {appointmentsTotal}).");

                        await Task.Delay(Config.QueryDelayMs);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log("ERROR: " + ex);
            await DoWarningAlert();
        }
    }

    static async Task UpdateTitleLoop()
    {
        while (true)
        {
            var uptime = DateTime.UtcNow - UtcStartTime;
            Console.Title = $"VicRoadsQuery | Uptime: {Math.Floor(uptime.TotalHours)}{uptime:\\:mm\\:ss}";

            await Task.Delay(1000);
        }
    }

    public static void Log(string msg)
    {
        string logMessage = $"[{DateTime.Now.ToString("s")}] {msg}";
        Console.WriteLine(logMessage);
        if (Config != null && !String.IsNullOrWhiteSpace(Config.LogFilePath))
            File.AppendAllText(Config.LogFilePath, logMessage + "\n");
    }
    
    static DateTime UnixTimeStampToDateTime(string unixTimeStamp)
    {
        // Unix timestamp is seconds past epoch
        DateTime dateTime = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
        dateTime = dateTime.AddMilliseconds(Double.Parse(unixTimeStamp)).ToLocalTime();
        return dateTime;
    }

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
    
    static string GetStringInput(string message)
    {
        while (true)
        {
            Console.Write(message);
        
            var ret = Console.ReadLine();
            if (ret != null)
                return ret;
            else
                Console.WriteLine("Please input something.");
        }
    }
}

