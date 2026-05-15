using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using PosSystem.Services;
using PosSystem.ViewModels.Admin.Modules;

namespace PosSystem.ViewModels.Admin;

// Root view-model for the v1 admin shell. Owns the sidebar nav, the
// currently active module's view-model, and the topbar status data.
public partial class AdminShellViewModel : BaseViewModel
{
    private readonly IServiceProvider _services;
    private readonly AuthService      _auth;
    private readonly System.Windows.Threading.DispatcherTimer _clockTimer;

    public ObservableCollection<NavigationItem> NavItems { get; }

    [ObservableProperty] private NavigationItem? _selectedItem;
    [ObservableProperty] private object?         _currentPage;

    [ObservableProperty] private string _branchInfo  = "Terminal 01 · Chilonzor filiali";
    [ObservableProperty] private string _syncLabel   = "Sinxron · 24s oldin";
    [ObservableProperty] private string _onlineLabel = "Onlayn";
    [ObservableProperty] private string _userName    = "admin";
    [ObservableProperty] private string _clockText   = DateTime.Now.ToString("HH:mm:ss");

    public AdminShellViewModel(IServiceProvider services, AuthService auth)
    {
        _services = services;
        _auth     = auth;

        NavItems = new ObservableCollection<NavigationItem>
        {
            new() { Module = AdminModule.Sotuv,       Title = "Sotuv",                 Glyph = "", ScreenLabel = "01 Sotuv"                 },
            new() { Module = AdminModule.Mahsulotlar, Title = "Mahsulotlar",           Glyph = "", ScreenLabel = "02 Mahsulotlar"           },
            new() { Module = AdminModule.Ombor,       Title = "Ombor",                 Glyph = "", ScreenLabel = "03 Ombor"                 },
            new() { Module = AdminModule.Hisobotlar,  Title = "Hisobotlar",            Glyph = "", ScreenLabel = "04 Hisobotlar"            },
            new() { Module = AdminModule.Mijozlar,    Title = "Mijozlar",              Glyph = "", ScreenLabel = "05 Mijozlar"              },
            new() { Module = AdminModule.Yetkazib,    Title = "Yetkazib beruvchilar",  Glyph = "", ScreenLabel = "06 Yetkazib beruvchilar"  },
            new() { Module = AdminModule.Xodimlar,    Title = "Xodimlar",              Glyph = "", ScreenLabel = "07 Xodimlar"              },
            new() { Module = AdminModule.Sozlamalar,  Title = "Sozlamalar",            Glyph = "", ScreenLabel = "08 Sozlamalar"            },
        };

        _clockTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _clockTimer.Tick += (_, _) => ClockText = DateTime.Now.ToString("HH:mm:ss");
        _clockTimer.Start();

        // v1 lands on Mahsulotlar by default (matches app-wpf.jsx initial section).
        SelectedItem = NavItems[1];
    }

    partial void OnSelectedItemChanged(NavigationItem? value)
    {
        if (value is null) return;
        CurrentPage = Resolve(value.Module);
    }

    private object Resolve(AdminModule module) => module switch
    {
        AdminModule.Sotuv       => _services.GetRequiredService<SotuvViewModel>(),
        AdminModule.Mahsulotlar => _services.GetRequiredService<MahsulotlarViewModel>(),
        AdminModule.Ombor       => _services.GetRequiredService<OmborViewModel>(),
        AdminModule.Hisobotlar  => _services.GetRequiredService<HisobotlarViewModel>(),
        AdminModule.Mijozlar    => _services.GetRequiredService<MijozlarViewModel>(),
        AdminModule.Yetkazib    => _services.GetRequiredService<YetkazibViewModel>(),
        AdminModule.Xodimlar    => _services.GetRequiredService<XodimlarViewModel>(),
        AdminModule.Sozlamalar  => _services.GetRequiredService<SozlamalarViewModel>(),
        _                       => new object(),
    };

    [RelayCommand]
    private void Logout()
    {
        _auth.Logout();
        WeakReferenceMessenger.Default.Send(new LogoutMessage());
    }
}
