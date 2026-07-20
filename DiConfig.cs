using Microsoft.Extensions.DependencyInjection;
using Quotix.Common;
using Quotix.Repositories;
using Quotix.Services;
using Quotix.ViewModels;

namespace Quotix;

/// <summary>
/// 依赖注入容器配置。
/// 统一注册所有服务、仓库和 ViewModel。
/// </summary>
/// <remarks>
/// 生命周期原则：
/// - Singleton：无状态或全局唯一（DatabaseProvider、Repository、Service、MainWindow）
/// - Transient：有状态 UI（ViewModel），避免状态共享导致 Bug
/// </remarks>
public static class DiConfig
{
    /// <summary>构建并返回 DI 容器</summary>
    public static ServiceProvider Build()
    {
        var services = new ServiceCollection();

        // ===== 基础设施（Singleton — 无状态）=====
        services.AddSingleton<DatabaseProvider>();
        services.AddSingleton<MigrationService>();

        // ===== Repository 层（Singleton — 无状态，仅依赖 DatabaseProvider）=====
        services.AddSingleton<ProductRepository>();
        services.AddSingleton<QuotationRepository>();
        services.AddSingleton<HeaderRepository>();

        // ===== Service 层（Singleton — 无状态业务逻辑）=====
        services.AddSingleton<AppSettingsService>();
        services.AddSingleton<ThemeService>();
        services.AddSingleton<DialogService>();
        services.AddSingleton<CacheService>();
        services.AddSingleton<ExportService>();
        services.AddSingleton<ProductImportService>();
        services.AddSingleton<ProductService>();
        services.AddSingleton<QuotationService>();
        services.AddSingleton<HeaderService>();
        services.AddSingleton<UpdatePipeline>();  // 更新流水线（状态机引擎）

        // ===== ViewModel 层（Transient — 每次解析新实例，防止状态共享）=====
        services.AddTransient<MainViewModel>();
        services.AddTransient<DashboardViewModel>();
        services.AddTransient<NewQuotationViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddTransient<ProductDatabaseViewModel>();
        services.AddTransient<HeaderDatabaseViewModel>();
        services.AddTransient<HistoryViewModel>();

        // ===== 主窗口（Singleton — 单窗口应用）=====
        services.AddSingleton<MainWindow>();

        return services.BuildServiceProvider();
    }
}
