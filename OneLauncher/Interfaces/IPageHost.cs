using Avalonia.Controls;
using System;
using System.Collections.Generic;
using System.Text;

namespace OneLauncher.Interfaces;

internal interface IPageHost
{
    public bool IsPaneShow { get; set; }
    public UserControl? PaneContent { get; set; }
}
