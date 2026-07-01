using Microsoft.Extensions.DependencyInjection;
using Quotix.Common;
using Quotix.Repositories;
using Quotix.Services;
using Quotix.ViewModels;

namespace Quotix;

/// <summary>
/// DI 容器配置 — 统一注册所有依赖
/// 
/// 生命周期原则：
///   Singleton  — 无状态 / 全局唯一（DatabaseProvider, Repository, Service, MainWindow）
///   Transient — 有状态 UI（ViewModel），避免状态共享 Bug
/// </summary>
public static class DiConfig
{
    public static ServiceProvider Build()
    {
        var services = new ServiceCollection();

        // ===== 基础设施 (Singleton — 无状态) =====
        services.AddSingleton<DatabaseProvider>();
        services.AddSingleton<MigrationService>();

        // ===== Repositories (Singleton — 无状态，仅依赖 DatabaseProvider) =====
        services.AddSingleton<ProductRepository>();
        services.AddSingleton<QuotationRepository>();
        services.AddSingleton<HeaderRepository>();

        // ===== Services (Singleton — 无状态业务逻辑) =====
        services.AddSingleton<AppSettingsService>();
        services.AddSingleton<ThemeService>();
        services.AddSingleton<DialogService>();
        services.AddSingleton<CacheService>();
        services.AddSingleton<ExportService>();
        services.AddSingleton<ProductImportService>();
        services.AddSingleton<ProductService>();
        services.AddSingleton<QuotationService>();
        services.AddSingleton<HeaderService>();

        // ===== ViewModels (Transient — 每次解析新实例，防止状态共享) =====
        services.AddTransient<MainViewModel>();
        services.AddTransient<NewQuotationViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<ProductDatabaseViewModel>();
        services.AddTransient<HeaderDatabaseViewModel>();
        services.AddTransient<HistoryViewModel>();

        // ===== Window (Singleton — 单窗口应用) =====
        services.AddSingleton<MainWindow>();

        return services.BuildServiceProvider();
    }
}
