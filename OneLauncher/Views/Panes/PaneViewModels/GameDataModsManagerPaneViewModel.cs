using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OneLauncher.Codes;
using OneLauncher.Core.Global;
using OneLauncher.Core.Helper;
using OneLauncher.Core.Helper.Models;
using OneLauncher.Core.Mod.ModManager;
using OneLauncher.Views.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace OneLauncher.Views.Panes.PaneViewModels;
internal partial class InstanceModItem : BaseViewModel
{
    private readonly InstanceModService _instanceModService;
    [ObservableProperty]
    public ModInfo info;
    [ObservableProperty]
    public Bitmap icon;
    public bool ModEnabledManager
    {
        get => Info.IsEnabled;
        set
        {
            try
            {
                Info.IsEnabled = value;
                if (value)
                    _instanceModService.EnableModAsync(Info.fileName);
                else
                    _instanceModService.DisableModAsync(Info.fileName);
            }
            catch(OlanException e)
            {
                OlanExceptionWorker.ForOlanException(e);
            }
            catch(Exception e)
            {
                OlanExceptionWorker.ForUnknowException(e);
            }
        }
    }
    public InstanceModItem(ModInfo info,InstanceModService modService)
    {
        _instanceModService = modService;
        Info = info;
        if(info.Icon != null)
            using (var stream = new MemoryStream(info.Icon))
                Icon = new Bitmap(stream);
        else
            Icon = new Bitmap(AssetLoader.Open(new Uri("avares://OneLauncher/Assets/Imgs/basic.png"))); // 默认图标
    }
}
internal partial class GameDataModsManagerPaneViewModel : BaseViewModel
{
    // 存放包装后的 Mod 列表
    [ObservableProperty]
    private List<InstanceModItem> mods;

    private readonly GameData _gameData;
    private readonly InstanceModService _modService;
    private readonly Action _onCloseCallback = () => { };

    public GameDataModsManagerPaneViewModel(GameData gameData,Action onCloseCallback)
    {
#if DEBUG
        if(Design.IsDesignMode)
        {
            // 设计时数据
            Mods = new List<InstanceModItem>
            {
                //new InstanceModItem(new ModInfo { Id = "mod1", Name = "Mod 1", Version = "1.0", Description = "这是一个测试Mod", IsEnabled = true }),
                //new InstanceModItem(new ModInfo { Id = "mod2", Name = "Mod 2", Version = "1.1", Description = "这是另一个测试Mod", IsEnabled = false }),
            };
        }
        else
#endif
        {
            _gameData = gameData;
            _modService = new InstanceModService(gameData);
            _onCloseCallback = onCloseCallback;
            _ = RefreshModsAsync(); // 初始化时加载
        }
    }

    [RelayCommand]
    private async Task RefreshModsAsync()
    {
        Mods = (await _modService.GetModsAsync()).Select(x => new InstanceModItem(x,_modService)).ToList();
    }

    [RelayCommand]
    private void OpenModsFolder()
    {
        Tools.OpenFolder(Path.Combine(_gameData.InstancePath, "mods"));
    }

    [RelayCommand]
    private void ClosePane() => _onCloseCallback();
}
