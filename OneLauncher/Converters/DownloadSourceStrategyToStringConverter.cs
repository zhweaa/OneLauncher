using Avalonia.Data.Converters;
using OneLauncher.Core.DefaultComponent.Downloader.DownloadMinecraftProviders;
using OneLauncher.Core.Helper.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OneLauncher.Views.Converters;

public class DownloadSourceStrategyToStringConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is DownloadSourceStrategy strategy)
        {
            return strategy switch
            {
                DownloadSourceStrategy.OfficialOnly => "官方源",
                DownloadSourceStrategy.RaceWithBmcl => "BMCLAPI源竞速",
                DownloadSourceStrategy.RaceWithOlan => "OLON源竞速",
                _ => value.ToString() // 作为备用，显示原始名称
            };
        }
        return value;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
