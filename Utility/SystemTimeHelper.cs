using System;
using System.Runtime.InteropServices;

namespace Utility
{
    public static class SystemTimeHelper
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetSystemTime(ref SYSTEMTIME st);

        [StructLayout(LayoutKind.Sequential)]
        private struct SYSTEMTIME
        {
            public short wYear;
            public short wMonth;
            public short wDayOfWeek;
            public short wDay;
            public short wHour;
            public short wMinute;
            public short wSecond;
            public short wMilliseconds;
        }

        /// <summary>
        /// 設定 Windows 系統時間
        /// </summary>
        /// <param name="dateTime">要設定的時間（本地時間）</param>
        /// <returns>是否設定成功</returns>
        public static bool SetWindowsSystemTime(DateTime dateTime)
        {
            try
            {
                // 轉換為 UTC 時間（Windows API 使用 UTC）
                DateTime utcDateTime = dateTime.ToUniversalTime();

                SYSTEMTIME st = new SYSTEMTIME
                {
                    wYear = (short)utcDateTime.Year,
                    wMonth = (short)utcDateTime.Month,
                    wDayOfWeek = (short)utcDateTime.DayOfWeek,
                    wDay = (short)utcDateTime.Day,
                    wHour = (short)utcDateTime.Hour,
                    wMinute = (short)utcDateTime.Minute,
                    wSecond = (short)utcDateTime.Second,
                    wMilliseconds = (short)utcDateTime.Millisecond
                };

                bool result = SetSystemTime(ref st);
                if (result)
                {
                }
                else
                {
                }
                return result;
            }
            catch (Exception ex)
            {
                return false;
            }
        }
    }
}