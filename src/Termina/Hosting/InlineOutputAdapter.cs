// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the Apache 2.0 license. See LICENSE file in the project root for full license information.

using Termina.Layout;
using Termina.Terminal;

namespace Termina.Hosting;

/// <summary>
/// Breaks the dependency cycle between application construction and view model construction.
/// </summary>
internal sealed class InlineOutputAdapter : IInlineOutput
{
    private IInlineOutput? _output;

    public void Attach(TerminaApplication application)
    {
        ArgumentNullException.ThrowIfNull(application);

        if (Interlocked.CompareExchange(ref _output, application, null) is not null)
            throw new InvalidOperationException("The inline output adapter already has an application.");
    }

    public ValueTask CommitAsync(ILayoutNode content, CancellationToken cancellationToken)
    {
        var output = Volatile.Read(ref _output)
            ?? throw new InvalidOperationException("The Termina application is not ready for inline output.");

        return output.CommitAsync(content, cancellationToken);
    }
}
