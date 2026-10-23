using Avalonia.Controls;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OneLauncher.Codes;
using OneLauncher.Core.DefaultComponent.Resource.ModService.Modrinth;
using OneLauncher.Core.DefaultComponent.Mod.ModService.Modrinth;
using OneLauncher.Core.Helper;
using OneLauncher.Core.Helper.Models;
using OneLauncher.Interfaces;
using OneLauncher.ViewModel.Page;
using OneLauncher.ViewModel.PaneViewModels.Factories;
using OneLauncher.Views.Panes;
using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace OneLauncher.Views.ViewModels;

internal sealed record ResourceTypeOption(ResourceType Type, string Name);

internal partial class ModsBrowserViewModel : BaseViewModel, IPageHost, IListTemplate<ResourceItem>
{
    private readonly InstallModPaneViewModelFactory _paneVMFactory;
    private readonly InstallPackPaneViewModelFactory _packPaneVMFactory;
    private CancellationTokenSource? _searchCancellation;
    private long _searchGeneration;

    public ResourceTypeOption[] ResourceTypes { get; } =
    [
        new(ResourceType.Mod, "模组"),
        new(ResourceType.ShaderPack, "光影包"),
        new(ResourceType.Modpack, "整合包")
    ];

    public ModsBrowserViewModel(InstallModPaneViewModelFactory installModPaneViewModelFactory,
        InstallPackPaneViewModelFactory installPackPaneViewModelFactory)
    {
        _paneVMFactory = installModPaneViewModelFactory;
        _packPaneVMFactory = installPackPaneViewModelFactory;
        selectedResourceType = ResourceTypes[0];
        _ = RefList();
    }

    [ObservableProperty] private bool isPaneShow;
    [ObservableProperty] private UserControl? paneContent;
    [ObservableProperty] private string searchContent = string.Empty;
    [ObservableProperty] private ResourceTypeOption selectedResourceType;
    [ObservableProperty] private ObservableCollection<ResourceItem> items = new();
    [ObservableProperty] private bool isSearching;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasSearchStatus))]
    private string searchStatus = string.Empty;

    public bool CanClear => !string.IsNullOrEmpty(SearchContent);
    public bool HasSearchStatus => !string.IsNullOrWhiteSpace(SearchStatus);
    public string SearchWatermark => SelectedResourceType?.Type switch
    {
        ResourceType.ShaderPack => "搜索光影包...",
        ResourceType.Modpack => "搜索整合包...",
        _ => "搜索模组..."
    };

    partial void OnSearchContentChanged(string value) => OnPropertyChanged(nameof(CanClear));

    partial void OnSelectedResourceTypeChanged(ResourceTypeOption value)
    {
        OnPropertyChanged(nameof(SearchWatermark));
        if (value == null) return;
        Items.Clear();
        _ = RefList();
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    public async Task RefList()
    {
        var generation = Interlocked.Increment(ref _searchGeneration);
        _searchCancellation?.Cancel();
        using var cancellation = new CancellationTokenSource();
        _searchCancellation = cancellation;
        var type = SelectedResourceType?.Type ?? ResourceType.Mod;
        string keyword = SearchContent;
        IsSearching = true;
        SearchStatus = "正在搜索...";
        try
        {
            var result = await new SearchModrinth().ToSearch(keyword, type, cancellation.Token);
            var resources = ResourceItem.Create(result, type);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (generation != _searchGeneration || cancellation.IsCancellationRequested) return;
                Items = new ObservableCollection<ResourceItem>(resources);
                SearchStatus = resources.Count == 0 ? "没有找到匹配的资源，试试其他关键词。" : string.Empty;
            });
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // 被后续搜索替代的请求不显示错误。
        }
        catch (Exception ex)
        {
            if (generation != _searchGeneration) return;
            SearchStatus = "搜索失败，请重试。";
            if (ex is OneLauncher.Core.Global.OlanException known)
                await OlanExceptionWorker.ForOlanException(known);
            else
                await OlanExceptionWorker.ForUnknowException(ex);
        }
        finally
        {
            if (generation == _searchGeneration)
            {
                IsSearching = false;
                _searchCancellation = null;
            }
        }
    }

    [RelayCommand]
    public void ClearTextBox() => SearchContent = string.Empty;

    [RelayCommand]
    public void ToInstallResource(ResourceItem item)
    {
        if (item.Type == ResourceType.Modpack)
        {
            var packPane = new InstallPackPane();
            var viewModel = _packPaneVMFactory.Create(item, () =>
            {
                if (ReferenceEquals(PaneContent, packPane)) IsPaneShow = false;
            });
            packPane.DataContext = viewModel;
            PaneContent = packPane;
            IsPaneShow = true;
            _ = viewModel.LoadInfo();
            return;
        }
        var pane = new InstallModPane();
        pane.DataContext = _paneVMFactory.Create(item, () =>
        {
            // 通知可能已替换为任务中心；旧侧栏不能关闭当前侧栏。
            if (ReferenceEquals(PaneContent, pane)) IsPaneShow = false;
        });
        PaneContent = pane;
        IsPaneShow = true;
    }
}

/// <summary>搜索结果携带资源类型，供安装命令选择对应侧栏。</summary>
internal sealed class ResourceItem : BaseViewModel
{
    public string Title { get; init; } = string.Empty;
    public Uri? IconUrl { get; init; }
    public string Description { get; init; } = string.Empty;
    public string ID { get; init; } = string.Empty;
    public List<string> SupportVersions { get; init; } = new();
    public ModType SupportModType { get; init; }
    public ResourceType Type { get; init; }
    public DateTime CreationTime { get; init; }

    public static List<ResourceItem> Create(ModrinthSearch info, ResourceType resourceType)
        => (info.Hits ?? []).Select(hit =>
        {
            var categories = hit.Categories ?? [];
            return new ResourceItem
            {
                Title = hit.Title ?? string.Empty,
                Description = hit.Description ?? string.Empty,
                ID = hit.ProjectId,
                Type = resourceType,
                CreationTime = hit.DateCreated,
                IconUrl = Uri.TryCreate(hit.IconUrl, UriKind.Absolute, out var icon) ? icon : null,
                SupportVersions = resourceType == ResourceType.Mod
                    ? Tools.McVsFilter(hit.Versions)
                    : new List<string>(hit.Versions ?? []),
                SupportModType = new ModType
                {
                    IsFabric = categories.Contains("fabric"),
                    IsNeoForge = categories.Contains("neoforge"),
                    IsForge = categories.Contains("forge"),
                    IsQuilt = categories.Contains("quilt")
                }
            };
        }).ToList();
}

