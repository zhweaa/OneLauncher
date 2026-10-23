using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using System.Threading.Tasks;

namespace OneLauncher.Interfaces;
internal interface IListTemplate<T>
{
    public Task RefList();
    public ObservableCollection<T> Items { get; set; }
}
