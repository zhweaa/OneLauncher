using Avalonia.Data.Converters;
using OneLauncher.Core.Helper.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OneLauncher.Views.Converters;
public class ModEnumToStringConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is ModEnum modEnum)
        {
            return modEnum switch
            {
                ModEnum.none => "原版",
                ModEnum.fabric => "Fabric",
                ModEnum.quilt => "Quilt",
                ModEnum.neoforge => "NeoForge",
                ModEnum.forge => "Forge",
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