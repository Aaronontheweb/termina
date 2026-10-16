// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the Apache 2.0 license. See LICENSE file in the project root for full license information.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Termina.Clipboard;
using Termina.Input;
using Termina.Notifications;
using Termina.Terminal;

namespace Termina.Hosting;

/// <summary>
/// Extension methods for registering Termina with dependency injection.
/// </summary>
public static class TerminaServiceCollectionExtensions
{
    /// <summary>
    /// Adds Termina TUI framework services to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Action to configure page registrations.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <example>
    /// <code>
    /// builder.Services.AddTermina(termina =>
    /// {
    ///     termina.RegisterPage&lt;MainMenuPage, MainMenuViewModel&gt;("main-menu");
    ///     termina.RegisterPage&lt;SettingsPage, SettingsViewModel&gt;("settings");
    /// });
    /// </code>
    /// </example>
    public static IServiceCollection AddTermina(
        this IServiceCollection services,
        Action<TerminaBuilder> configure)
    {
        return AddTermina(services, startPage: null, configure);
    }

    /// <summary>
    /// Adds Termina TUI framework services to the service collection with a start page.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="startPage">The page key to navigate to on startup.</param>
    /// <param name="configure">Action to configure page registrations.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddTermina(
        this IServiceCollection services,
        string? startPage,
        Action<TerminaBuilder> configure)
    {
        var builder = new TerminaBuilder(services);
        configure(builder);

        // Register IAnsiTerminal if not already registered
        services.TryAddSingleton<IAnsiTerminal>(_ => new AnsiTerminal(useAlternateScreen: false));
        services.TryAddSingleton<IToastService, ToastService>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IClipboardTransport, Osc52ClipboardTransport>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IClipboardTransport, TmuxClipboardTransport>());
        services.TryAddSingleton<IClipboardService, TerminalClipboardService>();
        services.TryAddSingleton<InlineOutputAdapter>();
        services.TryAddSingleton<IInlineOutput>(sp => sp.GetRequiredService<InlineOutputAdapter>());

        // Register TerminaApplication
        services.AddSingleton<TerminaApplication>(sp =>
        {
            var terminal = sp.GetRequiredService<IAnsiTerminal>();
            var app = new TerminaApplication(terminal, builder.RuntimeOptions, sp);
            sp.GetRequiredService<InlineOutputAdapter>().Attach(app);

            // Register all pages from the builder
            foreach (var descriptor in builder.PageDescriptors)
            {
                app.RegisterPageFromDescriptor(descriptor);
            }

            // Navigate to start page if specified
            if (!string.IsNullOrEmpty(startPage))
            {
                app.NavigateTo(startPage);
            }

            return app;
        });

        // Register hosted service to run Termina
        services.AddHostedService<TerminaHostedService>();

        return services;
    }

    /// <summary>
    /// Adds a console input source to Termina.
    /// This is the default if no other input source is configured.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddTerminaConsoleInput(this IServiceCollection services)
    {
        services.AddSingleton<IInputSource, ConsoleInputSource>();
        return services;
    }

    /// <summary>
    /// Adds a virtual input source to Termina for testing.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="inputSource">The virtual input source instance.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddTerminaVirtualInput(
        this IServiceCollection services,
        VirtualInputSource inputSource)
    {
        services.AddSingleton<IInputSource>(inputSource);
        return services;
    }
}
