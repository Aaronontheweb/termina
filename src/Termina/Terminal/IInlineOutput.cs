// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the Apache 2.0 license. See LICENSE file in the project root for full license information.

using Termina.Layout;

namespace Termina.Terminal;

/// <summary>
/// Commits stable layout content above an inline application live region.
/// </summary>
public interface IInlineOutput
{
    /// <summary>
    /// Commits content to primary-buffer scrollback and redraws the current live region.
    /// </summary>
    /// <param name="content">The stable content to commit.</param>
    /// <param name="cancellationToken">The cancellation token for the queued commit.</param>
    ValueTask CommitAsync(ILayoutNode content, CancellationToken cancellationToken);
}
