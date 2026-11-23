using System;
using System.Runtime.Serialization;



namespace ScheduleData
{
[Serializable]
public class ScheduleData
{
    public enum Place : byte
    {
        // 0: 미정, 1: 본당 대강당, 2: 본당 중강당, 3: 본당 소강당, 4: 교육원 대강당, 5: 교육원 중강당, 6: 교육원 소강당, 7: 교육원 농선부실, 8: 교육원 시선부실, 9: 교육원 고2실, 10: 기타
        None = 0,
        Main_Large, Main_Medium, Main_Small,
        Edu_Large, Edu_Medium, Edu_Small,
        Edu_Nongseon, Edu_Siseon, Edu_Go2, Etc
    }

    [Flags]
    public enum Preparation
    {
        None = 0,   
        Recording = 1,
        Praise = 1<<1,
        Streaming = 1<<2,
        OtherDisplay = 1<<3
    }

    public ScheduleTime scheduleDate;
    public ScheduleTime releaseDate;
    public int startTime;
    public string scheduleTitle;
    public string accessoryInfo;
    public Place place;
    public string sermonSpeaker;
    public Preparation preparation;
    public string preaparationNote;


    public ScheduleData(string jsonData)
    {
        var scheduleData = System.Text.Json.JsonSerializer.Deserialize<ScheduleData>(jsonData);
        if (scheduleData != null)
        {
            scheduleDate = scheduleData.scheduleDate;
            releaseDate = scheduleData.releaseDate;
            startTime = scheduleData.startTime;
            scheduleTitle = scheduleData.scheduleTitle;
            accessoryInfo = scheduleData.accessoryInfo;
            place = scheduleData.place;
            sermonSpeaker = scheduleData.sermonSpeaker;
            preparation = scheduleData.preparation;
            preaparationNote = scheduleData.preaparationNote;
        }
    }



}

[Serializable]
public struct ScheduleTime
{
    [System.Text.Json.Serialization.JsonInclude]
    byte yearData;
    [System.Text.Json.Serialization.JsonInclude]
    byte monthData;
    [System.Text.Json.Serialization.JsonInclude]
    byte dayData;
    [System.Text.Json.Serialization.JsonInclude]
    byte timeData;

    public ScheduleData(int rawDataValue)
    {
        this.yearData = (rawDataValue / 1_00_00_000) + 2000;
        this.monthData = (rawDataValue % 1_00_00_000) / 1_00_000;
        this.dayData = (rawDataValue % 1_00_000) / 1_000;
        this.timeData = rawDataValue % 1_000;
        
        return this;
    }

    public void SetTimeData(DateTime dateTimeData)
    {
        timeData = dateTimeData.TimeToByte();
        yearData = (byte)(dateTimeData.Year - 2000);
        monthData = (byte)dateTimeData.Month;
        dayData = (byte)dateTimeData.Day;
    }
    public void SetTimeDate(int year, int month, int day, int hour, int minute)
    {
        yearData = (byte)(year - 2000);
        monthData = (byte)month;
        dayData = (byte)day;
        timeData = (byte)((hour * 10) + (minute / 10));
    }

    public System.DateTime GetDateTime()
    {
        var (hour, minuate) = this.timeData.TimeDataToHourAndMinute();
            return new System.DateTime(2000+yearData, (int)monthData, (int)dayData, hour, minuate);
    }

    public readonly int GetRawValueData()
    {
        return (yearData * 1_00_00_000) + (monthData * 1_00_000) + (dayData * 1_000) + timeData;
    }
    public readonly bool UseTime()
    {
        return (timeData % 1000) > 0;
    }


}

public static class ScheduleTimeExtension
{
    readonly static byte[] MinuteArray = [0, 10, 20, 30, 40, 50];
    readonly static System.Text.StringBuilder stringBuilder = new();

    public static byte TimeToByte(this DateTime dateTime)
    {
        var minuteIndex = dateTime.Minute / 10;
        var hour = dateTime.Hour*10;
        return (byte)(hour + MinuteArray[minuteIndex]);
    }

        public static (int hour, int minuate) TimeDataToHourAndMinute(this byte timeData)
        {
            int H = timeData/10;
            int M = timeData%10 * 10;
            return (H, M);
        }

    public static string GetScheduleDateString(this ScheduleTime scheduleTime, bool includeTime = false)
    {
        stringBuilder.Clear();
        stringBuilder.Append($"{scheduleTime.yearData + 2000:D4}-{scheduleTime.monthData:00}-{scheduleTime.dayData:00}");
        if (includeTime)
        {
            stringBuilder.Append(" ");
            if (scheduleTime.timeData > 0)
            {
                var hour = scheduleTime.timeData / 10;
                var minute = scheduleTime.timeData % 10 * 10;
                //* 에러숫자 픽스
                if (hour >= 24)
                {
                    hour = 23;
                    minute = 59;
                }
                else if (hour < 0)
                {
                    hour = 0;
                    minute = 0;
                }

                if (minute >= 60)
                {
                    minute = 59;
                }
                else if (minute < 0)
                {
                    minute = 0;
                }
                //* 오전/오후 표시
                if (hour >= 12)
                {
                    stringBuilder.Append("오후");
                }
                else
                {
                    stringBuilder.Append("오전");
                }

                stringBuilder.Append($" {hour:00}:{minute:00}");
            }
            else
            {
                stringBuilder.Append("<No Time>");
            }
        }
        return stringBuilder.ToString();
    }

    public string ToJson(ScheduleData scheduleData)
    {
        return System.Text.Json.JsonSerializer.Serialize(scheduleData);
    }
}

}


