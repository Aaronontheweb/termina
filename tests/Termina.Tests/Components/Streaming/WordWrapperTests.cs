// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Termina.Components.Streaming;

namespace Termina.Tests.Components.Streaming;

/// <summary>
/// Tests for the plain <see cref="WordWrapper"/>.
/// </summary>
public class WordWrapperTests
{
    [Fact]
    public void WrapLine_WhitespaceOnlyLineWiderThanWidth_ReturnsOneBlankLine()
    {
        // A whitespace-only line that is wider than the width produces no words. The wrapper must
        // still return one blank line, the same as it does for an empty string.
        var wrapped = WordWrapper.WrapLine("   ", 2);

        Assert.Single(wrapped);
        Assert.Equal("", wrapped[0]);
    }

    [Fact]
    public void WrapLine_EmptyString_ReturnsOneBlankLine()
    {
        var wrapped = WordWrapper.WrapLine("", 2);

        Assert.Single(wrapped);
        Assert.Equal("", wrapped[0]);
    }
}
