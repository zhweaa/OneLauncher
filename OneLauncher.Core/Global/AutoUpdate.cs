using System;
using System.Collections.Generic;
using System.Text;

namespace OneLauncher.Core.Global;
internal class AutoUpdate
{
    const string CheckUpdateUrl = "https://raw.githubusercontent.com/zhweaa/OneLauncher/master/OneLauncher.Core/Global/Version.cs";
    /// <returns>需要更新返回true，否则返回false</returns>
    public static async Task<bool> CheckUpdate()
    {
        try
        {
            using var httpClient = new HttpClient();
            string response = await httpClient.GetStringAsync(CheckUpdateUrl);
            int start = -1, end = -1;
            for(int i  = 0; i < response.Length; i++)
            {
                if (response[i] == '\"')
                {
                    start = i;
                    break;
                }
            }
            if(start == -1) throw new Exception();
            for (int i = start; i < response.Length; i++)
            {
                if (response[i] == '\"')
                {
                    end = i;
                    break;
                }
            }
            if(end == -1) throw new Exception();
            var latestVersion = new Version(response[start..end]);
            if (latestVersion < new Version(Init.ApplicationVersoin))
                return false;
            else
                return true;
        }
        catch (Exception ex)
        {
            /*无关紧要，忽略就好*/
            return false;
        }
    }
}
