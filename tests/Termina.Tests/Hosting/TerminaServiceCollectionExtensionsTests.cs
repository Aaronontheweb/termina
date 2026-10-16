// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the Apache 2.0 license. See LICENSE file in the project root for full license information.

using Microsoft.Extensions.DependencyInjection;
using Termina.Hosting;
using Termina.Layout;
using Termina.Reactive;
using Termina.Terminal;

namespace Termina.Tests.Hosting;

public sealed class TerminaServiceCollectionExtensionsTests
{
    [Fact]
    public void AddTermina_allows_a_view_model_to_resolve_inline_output_during_start_page_navigation()
    {
        var services = new ServiceCollection();
        services.AddSingleton<InlineOutputCapture>();
        services.AddTermina("/", builder =>
            builder.RegisterRoute<InlineOutputPage, InlineOutputViewModel>("/"));
        using var provider = services.BuildServiceProvider();

        var application = provider.GetRequiredService<TerminaApplication>();
        var capture = provider.GetRequiredService<InlineOutputCapture>();

        Assert.Equal("/", application.CurrentPath);
        Assert.Same(provider.GetRequiredService<IInlineOutput>(), capture.Output);
    }

    private sealed class InlineOutputCapture
    {
        public IInlineOutput? Output { get; set; }
    }

    private sealed class InlineOutputViewModel : ReactiveViewModel
    {
        public InlineOutputViewModel(IInlineOutput output, InlineOutputCapture capture)
        {
            capture.Output = output;
        }
    }

    private sealed class InlineOutputPage : ReactivePage<InlineOutputViewModel>
    {
        public override ILayoutNode BuildLayout() => new TextNode("Inline output test");
    }
}
