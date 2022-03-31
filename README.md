# vicroadsquery
 appointment alerter for vicroads license test


    PROGRAM: VicRoadsQuery
    WRITTEN BY ASHLEY DE VRIES
    CREATED ON 29/03/2022
    LAST MODIFIED ON 30/03/2022

Hey, this is just a little program I wrote to track VicRoads appointments, because of how messed up their backlog is.
Don't be surprised if this stops working, and I probably won't update it after I'm done with it.

## CONFIGURING OFFICES (vicroads.config.offices.json)
1. Go to the [VicRoads booking site](https://billing.vicroads.vic.gov.au/bookings/Manage/Details).
2. Enter all your details, and get to the section where you select the appointment day/time.
3. In this section, right click on the 'Step 3 of 4' text, and open Inspect Element.
4. A few lines below the element which opens and is selected, find the element with the id="Offices" and name="Offices" tags.
5. Just after, there should be a massive block of text, starting with ``[{"Id":``.
6. Double click on that text and copy it.
7. Delete any text in vicroads.config.offices.json, and paste the text you just copied.
8. Done!

## CONFIG OPTIONS (vicroadsquery.config.json)

- LogFilePath : string (default: "vicroadsquery.log.txt")
    - Path to the file which VicRoadsQuery will log all console messages to, as a rolling append.

- QueryDelayMs : int (default: 30000)
    - How long in milliseconds to wait between queries.
    - Even if you won't get rate limited (I haven't been so far), it's courtesy to just keep this at the highest amount of time you are willing to wait.

- MaxRetryAttempts : int (default: 5)
    - How many times to retry before giving up when VicRoads won't authenticate the program.

- PrintResponseSummaries : boolean (default: false)
    - Whether or not to print a summary of the dates received and the number of appointments available.
    - Can be somewhat interesting to track, but otherwise mostly pointless.

- LicenseNumber : string (default: irrelevant)
    - Your license number that you would use on the VicRoads booking site.

- LastName : string (default: irrelevant)
    - Your last name that you would use on the VicRoads booking site.

- MinAlertDate : DateTime (default: "2022-04-20")
    - The soonest date you want to find an appointment for.
    - In yyyy-MM-dd format.

- MaxAlertDate : DateTime (default: "2022-05-15")
    - The latest date you want to find an appointment for.
    - In yyyy-MM-dd format.

- MinAlertTime : TimeSpan (default: "09:15")
    - The soonest time you want to find an appointment for.
    - 24 hour hh:mm format.

- MaxAlertTime : TimeSpan (default: "15:00")
    - The latest time you want to find an appointment for.
    - 24 hour hh:mm format.

- TimeRangeExclusive : boolean (default: true)
    - Inverts the behaviour of Min and Max alert times.
    - Instead, the program will alert you to bookings outside of the specified time range.
    - However, it is still inclusive.
    - e.g., with defaults when true:
        - It will alert you to any appointment before or at 09:15.
        - It will alert you to any appointment after or at 15:00.

- OfficesToQuery : Array(string) (default: ["Coolaroo", "Bundoora"])
    - The program will cycle through the specified offices to look for appointments.
    - Offices are identified by their shortname in vicroadsquery.config.offices.json.
