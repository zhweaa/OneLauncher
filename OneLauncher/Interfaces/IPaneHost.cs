using System;
using System.Collections.Generic;
using System.Text;

namespace OneLauncher.Interfaces;
internal interface IPaneHost
{
    public Action OnPaneClose { get; init; }
}
