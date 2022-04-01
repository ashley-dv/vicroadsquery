// POST TO: https://billing.vicroads.vic.gov.au/bookings/Appointment/GetAppointmentTimes

using System.Diagnostics.Contracts;
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
            if (Config.TryLoad(out Config) && !Config.IsDefault())
                Log($"Config successfully loaded.");
            else 
            {
                Log("It appears you have a default configuration, or it's your first time using the program.");
                Log($"Please setup your {Config.CONFIG_PATH} file. Instructions can be found in the readme.");
                Console.Write("Press any key to exit.");
                Console.ReadKey();
                return;
            }

            Log("Attempting to read offices config...");
            Offices = Config.LoadOffices();
            Log($"Successfully loaded {Offices.Count} offices.");

            List<(int, string)> queryOffices = new List<(int, string)>();

            string officeList = string.Empty;
            bool extractOffices = false;

            if (Offices.Count <= 0)
            {
                extractOffices = true;
                Log("No offices were loaded, so program will enter office configuration mode.");
            }
            else
            {
                // NOTE: Office shortnames are read in and made lowercase in Config.cs to remove case sensitivity.
                foreach (var i in Config.OfficesToQuery)
                {
                    if (Offices.TryGetValue(i.ToLowerInvariant(), out var office))
                    {
                        queryOffices.Add((office.Id, office.ShortName));
                        officeList += i + ", ";
                    }
                    else
                        Log($"WARNING: No office found by the shortname {i}. You might need to configure offices for your location.", LogLevel.Warning);
                }

                if (queryOffices.Count <= 0)
                {
                    Log(Config.OfficesToQuery.Length <= 0
                        ? "You haven't entered any offices to query."
                        : "We found office data in the configuration file, but none you listed were found.", LogLevel.Error);

                    Log($"The following offices were found in the configuration: {Offices.Values.Select(x => x.ShortName).MakeListString(", ")}.");
                    Log($"If you cannot find your office above, it is recommended to enter office configuration mode to correct this.");
                    Log($"However, if it is present, check your office list for typos.");
                    extractOffices = GetBooleanInput("Do you want to enter office configuration mode? (Y/N): ");

                    if (!extractOffices)
                    {
                        Log("Going to exit in 5 seconds. Ensure you correct your configuration.", LogLevel.Error);
                        await Task.Delay(5000);
                        return;
                    }
                }
                else
                    Log($"Going to query {Config.OfficesToQuery.Length} office(s): {officeList.Remove(officeList.Length - 2)}.");
            }

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
            client.DefaultRequestHeaders.Add("Accept", "application/json, text/javascript, */*; q=0.01,text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3;q=0.9");
            client.DefaultRequestHeaders.Add("Accept-Encoding", "gzip, deflate, br");
            client.DefaultRequestHeaders.Add("Accept-Language", "en-GB,en-US;q=0.9,en;q=0.8");

            client.DefaultRequestHeaders.Add("Origin", "https://billing.vicroads.vic.gov.au");
            client.DefaultRequestHeaders.Add("Host", "billing.vicroads.vic.gov.au");
            client.DefaultRequestHeaders.Add("Connection", "keep-alive");
            client.DefaultRequestHeaders.Add("Referrer",
                "https://billing.vicroads.vic.gov.au/bookings/Appointment/LocationSearch");
            client.DefaultRequestHeaders.Add("sec-ch-ua",
                "\"Not A;Brand\";v=\"99\", \"Chromium\";v=\"99\", \"Google Chrome\";v=\"99\"");
            client.DefaultRequestHeaders.Add("sec-ch-ua-mobile", "?0");
            client.DefaultRequestHeaders.Add("sec-ch-ua-platform", "\"Windows\"");
            client.DefaultRequestHeaders.Add("Sec-Fetch-Dest", "document");
            client.DefaultRequestHeaders.Add("Sec-Fetch-Mode", "navigate");
            client.DefaultRequestHeaders.Add("Sec-Fetch-Site", "same-origin");
            client.DefaultRequestHeaders.Add("Sec-Fetch-User", "?1");
            client.DefaultRequestHeaders.Add("Upgrade-Insecure-Requests", "1");

            int retryAttempts = 0;

            while (retryAttempts < Config.MaxRetryAttempts)
            {
                var auth = await DoAuthentication(client);

                bool verified = auth.Item1;

                if (!verified)
                {
                    retryAttempts++;
                    Log($"Retrying verification in {Config.RetryDelayMs}ms (Attempt {retryAttempts}/{Config.MaxRetryAttempts})");
                    await Task.Delay(Config.RetryDelayMs);
                    continue;
                }
                    
                string verificationToken = auth.Item2;

                bool looping = true;
                if (extractOffices)
                {
                    Log("Offices aren't configured, so we need to configure them. You should only have to do this once.");
                    
                    while (looping)
                    {
                        Log("You will need to enter your postcode, its longitude, and latitude. You can find these by looking up your suburb on Wikipedia or Google Maps.");

                        var postcode = GetPositiveIntegerInput("Postcode: ");
                        var latitude = GetFloatInput("Latitude: ");
                        var longitude = GetFloatInput("Longitude: ");

                        var officeQueryValues = new Dictionary<string, string>
                        {
                            {"VerificationToken", verificationToken},
                            {"postcode", postcode.ToString()},
                            {"latitude", latitude.ToString(CultureInfo.InvariantCulture)},
                            {"longitude", longitude.ToString(CultureInfo.InvariantCulture)}
                        };

                        var officeQueryContent = new FormUrlEncodedContent(officeQueryValues);
                        officeQueryContent.Headers.Clear();
                        officeQueryContent.Headers.Add("Content-Type",
                            "application/x-www-form-urlencoded; charset=UTF-8");
                        
                        Log($"Sending location search request...");
                        var officeQueryResponse = await client.PostAsync(
                            "https://billing.vicroads.vic.gov.au/bookings/Appointment/LocationSearch",
                            officeQueryContent);
                        Log($"Sending location search response received.");

                        var officeQueryResponseStr = officeQueryResponse.Content.ReadAsStringAsync().Result;
                        var officeResponseRaw =
                            JsonConvert.DeserializeObject<VicRoadsAppointmentRaw>(officeQueryResponseStr);

                        foreach (var i in officeResponseRaw.Errors)
                            Log($"VicRoads Error: {i.Message}.");

                        bool officeQuerySuccess = false;

                        switch (officeResponseRaw.Response)
                        {
                            case 1:
                                officeQuerySuccess = true;
                                break;
                            case 2:
                                Log("You might have entered something wrong. Please try again.", LogLevel.Error);
                                break;
                            default:
                                looping = false;
                                retryAttempts++;
                                Log($"Invalid response [code: {officeResponseRaw.Response}]. Resetting. (Attempt {retryAttempts}/{Config.MaxRetryAttempts}).", LogLevel.Error);
                                break;
                        }

                        if (!officeQuerySuccess)
                            continue;

                        retryAttempts = 0;
                        await Task.Delay(1000);

                        Log("Getting Appointment/AppointmentSearch to retrieve offices...");
                        var officesResponseHtml =
                            await client.GetAsync("https://billing.vicroads.vic.gov.au/bookings/Appointment/AppointmentSearch");
                        var officesResponseHtmlLines =
                            WebUtility.HtmlDecode(officesResponseHtml.Content.ReadAsStringAsync().Result).Split("\n");
                        Log("Offices response received.");

                        var officesJson = string.Empty;
                        foreach (var line in officesResponseHtmlLines)
                        {
                            // identify line containing offices
                            if (line.Contains("type=\"hidden\"") && line.Contains("Offices"))
                            {
                                var match = Regex.Match(line, "value=\"(.*)\"");
                                if (match.Groups.Count > 0)
                                    officesJson = match.Groups[0].Value.Substring("value=".Length).Trim('"');
                            }
                        }

                        if (string.IsNullOrWhiteSpace(officesJson))
                        {
                            Log(
                                "ERROR: Couldn't find office data in the received response. You might've entered something incorrect.", LogLevel.Error);
                            continue;
                        }

                        var extractedOffices = JsonConvert.DeserializeObject<VicRoadsOffice[]>(officesJson);
                        if (extractedOffices == null)
                        {
                            Log(
                                "ERROR: Couldn't parse office JSON. Was the correct string extracted?", LogLevel.Error);
                            Log("RAW JSON: " + officesJson);
                            continue;
                        }
                        
                        Log($"Found the following {extractedOffices.Length} offices: {extractedOffices.Select(x => x.ShortName).MakeListString(", ")}.");
                        looping = extractOffices = !GetBooleanInput($"Are all of the offices you want to query there? (Y/N): ");

                        if (extractOffices)
                        {
                            Log("Make sure you entered the correct longitude, latitude, and postcode. Retrying.", LogLevel.Error);
                            continue;
                        }
                            
                        Log("Saving offices to configuration file...");
                        Config.SaveOffices(extractedOffices);
                        Log("Offices saved successfully.");
                        Console.Write("Press any key to exit. Offices will load on next start.");
                        Console.ReadKey();
                        return;
                    }
                }
                
                while (looping && !extractOffices)
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
                        bool useRetryDelay = false;
                        
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
                                Log($"Invalid response [code: {apptsRaw.Response}]. Resetting. (Attempt {retryAttempts}/{Config.MaxRetryAttempts}).", LogLevel.Error);
                                looping = false;
                                useRetryDelay = true;
                                break;
                        }

                        if (!success)
                        {
                            await Task.Delay(useRetryDelay ? Config.RetryDelayMs : Config.QueryDelayMs);
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
                                    dateTime.ToString("D"), LogLevel.Success);
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
                            await DoAlert(Config.AlertBeep);
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
        }
        
        await DoAlert(Config.WarningBeep);
    }

    static async Task<(bool, string)> DoAuthentication(HttpClient client)
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
            Log($"Extracted TtyghsS: {ttyghss}", LogLevel.Success);

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
            Log($"ERROR: Non successful response received.");
            return (false, string.Empty);
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
            Log("ERROR: Verification token is empty! Will likely mean program will not work.", LogLevel.Error);
        else
            Log($"Extracted verification token: {verificationToken}", LogLevel.Success);
        
        var changeApptValues = new Dictionary<string, string>
        {
            {"VerificationToken", verificationToken},
            {"appointmentNumber", "167048747"}
        };
        
        var changeApptContent = new FormUrlEncodedContent(changeApptValues);
        changeApptContent.Headers.Clear();
        changeApptContent.Headers.Add("Content-Type", "application/x-www-form-urlencoded; charset=UTF-8");
        
        // agree to terms and conditions - Appointment/AppointmentSearch won't work unless we do
        await client.PostAsync("https://billing.vicroads.vic.gov.au/bookings/Manage/ChangeAppointment",
            changeApptContent);
        
        var termsAgreeValues = new Dictionary<string, string>
        {
            {"VerificationTokenForm", verificationToken},
            {"blank", "True"},
            {"Submit", "Continue"}
        };
        
        var termsAgreeContent = new FormUrlEncodedContent(termsAgreeValues);
        termsAgreeContent.Headers.Clear();
        termsAgreeContent.Headers.Add("Content-Type", "application/x-www-form-urlencoded; charset=UTF-8");
        
        // agree to terms and conditions - Appointment/AppointmentSearch won't work unless we do
        await client.PostAsync("https://billing.vicroads.vic.gov.au/bookings/Transfer/TermsAndConditions",
            termsAgreeContent);

        return (true, verificationToken);
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

    public static void Log(string msg, LogLevel level = LogLevel.Info)
    {
        string logMessage = $"[{DateTime.Now.ToString("s")}] {msg}";

        switch (level)
        {
            case LogLevel.Success:
                Console.ForegroundColor = ConsoleColor.Green;
                break;
            case LogLevel.Warning:
                Console.ForegroundColor = ConsoleColor.Yellow;
                break;
            case LogLevel.Error:
                Console.ForegroundColor = ConsoleColor.DarkRed;
                break;
            case LogLevel.Critical:
                Console.BackgroundColor = ConsoleColor.Red;
                break;
        }
        
        Console.WriteLine(logMessage);
        if (Config != null && !String.IsNullOrWhiteSpace(Config.LogFilePath))
            File.AppendAllText(Config.LogFilePath, logMessage + "\n");

        Console.ResetColor();
    }
    
    static DateTime UnixTimeStampToDateTime(string unixTimeStamp)
    {
        // Unix timestamp is seconds past epoch
        DateTime dateTime = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
        dateTime = dateTime.AddMilliseconds(Double.Parse(unixTimeStamp)).ToLocalTime();
        return dateTime;
    }

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

    static float GetFloatInput(string message)
    {
        while (true)
        {
            Console.Write(message);
        
            var input = Console.ReadLine();
            if (!float.TryParse(input, out var ret))
                Console.WriteLine("Please input a valid decimal.");
            else
                return ret;
        }
    }
    
    static int GetPositiveIntegerInput(string message)
    {
        while (true)
        {
            Console.Write(message);
        
            var input = Console.ReadLine();
            if (!int.TryParse(input, out var ret) || ret < 0)
                Console.WriteLine("Please input a valid positive integer.");
            else
                return ret;
        }
    }

    static bool GetBooleanInput(string message)
    {
        while (true)
        {
            Console.Write(message);
        
            var input = Console.ReadLine();
            switch (input.ToLowerInvariant())
            {
                case "yes":
                case "y":
                    return true;
                case "no":
                case "n":
                    return false;
                default:
                    Console.WriteLine("Please input Y/Yes or N/No.");
                    break;
            }
        }
    }
}

public static class Extensions
{
    /// <summary>
    ///     Makes a neat list string of a single per item iterable object.
    /// </summary>
    /// <param name="enumerable">Enumerable to string-ify.</param>
    /// <param name="delimiter">Sequence to put between each value.</param>
    /// <typeparam name="T"></typeparam>
    /// <returns>A string list.</returns>
    public static string MakeListString<T>(this IEnumerable<T> enumerable, string delimiter)
    {
        StringBuilder ret = new StringBuilder();

        var array = enumerable as T[] ?? enumerable.ToArray();
        for (int i = 0; i < array.Length; i++)
        {
            if (i > 0) ret.Append(delimiter);
            ret.Append(array[i]);
        }

        return ret.ToString();
    }
}

