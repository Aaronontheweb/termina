// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the Apache 2.0 license. See LICENSE file in the project root for full license information.

namespace Termina.Terminal;

/// <summary>
/// Remembers the display column count of the last string it measured.
/// </summary>
/// <remarks>
/// <see cref="DisplayWidth.GetColumnCount(string)"/> walks the whole string and allocates for each
/// text element. A node that measures the same long text on every frame can use this cache, so
/// that only a changed string is walked again. The count depends only on the string value, so a
/// cached count is always correct for an equal string.
/// </remarks>
internal struct ColumnCountCache
{
    private string? _text;
    private int _columns;

    /// <summary>
    /// Returns the display column count of <paramref name="text"/>.
    /// </summary>
    public int Get(string text)
    {
        if (ReferenceEquals(_text, text))
            return _columns;

        // An equal string in a new object keeps the count. Keep the new reference, so the next call
        // with it is a reference check, not a compare of every character.
        if (!string.Equals(_text, text, StringComparison.Ordinal))
            _columns = DisplayWidth.GetColumnCount(text);

        _text = text;

        return _columns;
    }
}
