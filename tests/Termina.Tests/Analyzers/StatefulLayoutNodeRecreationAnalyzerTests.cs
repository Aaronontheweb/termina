// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the Apache 2.0 license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Termina.Generators;
using Termina.Tests.Utility;

namespace Termina.Tests.Analyzers;

using Verify = TerminaVerifier<StatefulLayoutNodeRecreationAnalyzer>;

/// <summary>
/// Tests for <see cref="StatefulLayoutNodeRecreationAnalyzer"/> (TERMINA004).
///
/// The fixture follows the Akka.Analyzers layout: one bucket of success cases (the happy path, which must
/// produce no diagnostic) and one bucket of failure cases (the sad path, which must produce one warning).
/// Each failure case marks the expected location with <c>{|#0:Invalidate|}</c> markup, so the cases do not
/// depend on manual line numbers. The matrix ids (W*, N*, L*) map to the spec in issue #339.
/// </summary>
public sealed class StatefulLayoutNodeRecreationAnalyzerTests
{
    /// <summary>Stub of the Termina layout types the analyzer resolves. Added to every case.</summary>
    private const string TerminaFrameworkSource = """
        namespace Termina.Layout
        {
            public interface ILayoutNode { }
            public abstract class LayoutNode : ILayoutNode { }

            public sealed class DynamicLayoutNode : LayoutNode
            {
                public DynamicLayoutNode(System.Func<ILayoutNode> factory) { }
                public void Invalidate() { }
            }

            public sealed class KeyedDynamicLayoutNode<TKey> : LayoutNode
            {
                public KeyedDynamicLayoutNode(System.Func<TKey> keySelector, System.Func<TKey, ILayoutNode> contentFactory) { }
                public void Invalidate() { }
            }

            public sealed class TextNode : LayoutNode
            {
                public TextNode(string text) { }
            }

            public sealed class ScrollableContainerNode : LayoutNode
            {
                public ScrollableContainerNode WithContent(ILayoutNode content) => this;
            }

            public sealed class TextInputNode : LayoutNode { }
            public sealed class StreamingTextNode : LayoutNode { }
            public sealed class SelectionListNode<T> : LayoutNode { }

            public sealed class OtherInvalidatingNode : LayoutNode
            {
                public void Invalidate() { }
            }
        }
        """;

    // -------------------------------------------------------------------------------------------------
    // Happy path: correct code that reuses a stateful node or invalidates a narrower node. No warning.
    // -------------------------------------------------------------------------------------------------
    public static readonly TheoryData<string> SuccessCases = new()
    {
        // N1: `??=` creates the node one time and reuses it. (This was the earlier false positive.)
        """
        using Termina.Layout;

        public sealed class TestPage
        {
            private DynamicLayoutNode? _contentNode;
            private ScrollableContainerNode? _scrollNode;

            public ILayoutNode BuildLayout()
            {
                _contentNode = new DynamicLayoutNode(() =>
                {
                    _scrollNode ??= new ScrollableContainerNode();
                    return _scrollNode;
                });
                return _contentNode;
            }

            public void OnStateChanged() => _contentNode?.Invalidate();
        }
        """,

        // N2: the field is created once in BuildLayout. The factory only references it.
        """
        using Termina.Layout;

        public sealed class TestPage
        {
            private DynamicLayoutNode? _contentNode;
            private DynamicLayoutNode? _rowsNode;
            private ScrollableContainerNode? _scrollNode;

            public ILayoutNode BuildLayout()
            {
                _scrollNode = new ScrollableContainerNode();
                _rowsNode = new DynamicLayoutNode(() => new TextNode("rows"));
                _contentNode = new DynamicLayoutNode(() => _scrollNode.WithContent(_rowsNode));
                return _contentNode;
            }

            public void OnStateChanged() => _contentNode?.Invalidate();
        }
        """,

        // N3: the code invalidates the narrower rows node, not the parent that rebuilds the scroll node.
        """
        using Termina.Layout;

        public sealed class TestPage
        {
            private DynamicLayoutNode? _contentNode;
            private DynamicLayoutNode? _rowsNode;
            private ScrollableContainerNode? _scrollNode;

            public ILayoutNode BuildLayout()
            {
                _contentNode = new DynamicLayoutNode(() =>
                {
                    _scrollNode = new ScrollableContainerNode();
                    _rowsNode = new DynamicLayoutNode(() => new TextNode("rows"));
                    return _scrollNode.WithContent(_rowsNode);
                });
                return _contentNode;
            }

            public void OnRowsChanged() => _rowsNode?.Invalidate();
        }
        """,

        // N4: the factory returns a stateless node.
        """
        using Termina.Layout;

        public sealed class TestPage
        {
            private DynamicLayoutNode? _contentNode;

            public ILayoutNode BuildLayout()
            {
                _contentNode = new DynamicLayoutNode(() => new TextNode("content"));
                return _contentNode;
            }

            public void OnStateChanged() => _contentNode?.Invalidate();
        }
        """,

        // N5: KeyedDynamicLayoutNode caches by key and preserves state. Its Invalidate must not warn.
        """
        using Termina.Layout;

        public sealed class TestPage
        {
            private KeyedDynamicLayoutNode<int>? _contentNode;
            private int _step;

            public ILayoutNode BuildLayout()
            {
                _contentNode = new KeyedDynamicLayoutNode<int>(() => _step, key => new SelectionListNode<int>());
                return _contentNode;
            }

            public void OnStateChanged() => _contentNode?.Invalidate();
        }
        """,

        // N6: Invalidate() on a field that is not a DynamicLayoutNode.
        """
        using Termina.Layout;

        public sealed class TestPage
        {
            private OtherInvalidatingNode? _otherNode;
            private ScrollableContainerNode? _scrollNode;

            public ILayoutNode BuildLayout()
            {
                _otherNode = new OtherInvalidatingNode();
                _scrollNode = new ScrollableContainerNode();
                return _otherNode;
            }

            public void OnStateChanged() => _otherNode?.Invalidate();
        }
        """,
    };

    // -------------------------------------------------------------------------------------------------
    // Sad path: a factory that creates a new stateful node on every run. One warning.
    // Tuple: (source with {|#0:Invalidate|} markup, dynamic field name, stateful type name).
    // -------------------------------------------------------------------------------------------------
    public static readonly TheoryData<string, string, string> FailureCases = new()
    {
        // W1: factory assigns a new stateful node to a field, broad invalidation.
        {
            """
            using Termina.Layout;

            public sealed class TestPage
            {
                private DynamicLayoutNode? _contentNode;
                private ScrollableContainerNode? _scrollNode;

                public ILayoutNode BuildLayout()
                {
                    _contentNode = new DynamicLayoutNode(() =>
                    {
                        _scrollNode = new ScrollableContainerNode();
                        return _scrollNode;
                    });
                    return _contentNode;
                }

                public void OnStateChanged() => _contentNode?.{|#0:Invalidate|}();
            }
            """,
            "_contentNode", "ScrollableContainerNode"
        },

        // W2: factory clears the field, then creates a new instance.
        {
            """
            using Termina.Layout;

            public sealed class TestPage
            {
                private DynamicLayoutNode? _contentNode;
                private ScrollableContainerNode? _scrollNode;

                public ILayoutNode BuildLayout()
                {
                    _contentNode = new DynamicLayoutNode(() =>
                    {
                        _scrollNode = null;
                        _scrollNode = new ScrollableContainerNode();
                        return _scrollNode;
                    });
                    return _contentNode;
                }

                public void OnStateChanged() => _contentNode?.{|#0:Invalidate|}();
            }
            """,
            "_contentNode", "ScrollableContainerNode"
        },

        // W3: factory returns a freshly-built stateful node, no field. The main #155 footgun.
        {
            """
            using Termina.Layout;

            public sealed class TestPage
            {
                private DynamicLayoutNode? _contentNode;
                private int _step;

                public ILayoutNode BuildLayout()
                {
                    _contentNode = new DynamicLayoutNode(() =>
                    {
                        if (_step == 0)
                            return new SelectionListNode<int>();
                        return new TextNode("done");
                    });
                    return _contentNode;
                }

                public void OnStateChanged() => _contentNode?.{|#0:Invalidate|}();
            }
            """,
            "_contentNode", "SelectionListNode"
        },

        // W4: the factory calls a private helper that creates the stateful node.
        {
            """
            using Termina.Layout;

            public sealed class TestPage
            {
                private DynamicLayoutNode? _contentNode;
                private ScrollableContainerNode? _scrollNode;

                public ILayoutNode BuildLayout()
                {
                    _contentNode = new DynamicLayoutNode(() => Build());
                    return _contentNode;
                }

                private ILayoutNode Build()
                {
                    _scrollNode = new ScrollableContainerNode();
                    return _scrollNode;
                }

                public void OnStateChanged() => _contentNode?.{|#0:Invalidate|}();
            }
            """,
            "_contentNode", "ScrollableContainerNode"
        },

        // W5a: TextInputNode recreation.
        {
            """
            using Termina.Layout;

            public sealed class TestPage
            {
                private DynamicLayoutNode? _contentNode;

                public ILayoutNode BuildLayout()
                {
                    _contentNode = new DynamicLayoutNode(() => new TextInputNode());
                    return _contentNode;
                }

                public void OnStateChanged() => _contentNode?.{|#0:Invalidate|}();
            }
            """,
            "_contentNode", "TextInputNode"
        },

        // W5b: StreamingTextNode recreation.
        {
            """
            using Termina.Layout;

            public sealed class TestPage
            {
                private DynamicLayoutNode? _contentNode;

                public ILayoutNode BuildLayout()
                {
                    _contentNode = new DynamicLayoutNode(() => new StreamingTextNode());
                    return _contentNode;
                }

                public void OnStateChanged() => _contentNode?.{|#0:Invalidate|}();
            }
            """,
            "_contentNode", "StreamingTextNode"
        },

        // L2: a direct (non-conditional) Invalidate() call reports at the call site.
        {
            """
            using Termina.Layout;

            public sealed class TestPage
            {
                private DynamicLayoutNode _contentNode = null!;

                public ILayoutNode BuildLayout()
                {
                    _contentNode = new DynamicLayoutNode(() => new ScrollableContainerNode());
                    return _contentNode;
                }

                public void OnStateChanged()
                {
                    _contentNode.{|#0:Invalidate|}();
                }
            }
            """,
            "_contentNode", "ScrollableContainerNode"
        },

        // L3: the Invalidate() call inside a conditional block is still detected.
        {
            """
            using Termina.Layout;

            public sealed class TestPage
            {
                private DynamicLayoutNode? _contentNode;
                private bool _dirty;

                public ILayoutNode BuildLayout()
                {
                    _contentNode = new DynamicLayoutNode(() => new ScrollableContainerNode());
                    return _contentNode;
                }

                public void OnStateChanged()
                {
                    if (_dirty)
                        _contentNode?.{|#0:Invalidate|}();
                }
            }
            """,
            "_contentNode", "ScrollableContainerNode"
        },

        // L4: two dynamic fields exist. Only the one with the recreating factory warns.
        {
            """
            using Termina.Layout;

            public sealed class TestPage
            {
                private DynamicLayoutNode? _statelessNode;
                private DynamicLayoutNode? _statefulNode;

                public ILayoutNode BuildLayout()
                {
                    _statelessNode = new DynamicLayoutNode(() => new TextNode("safe"));
                    _statefulNode = new DynamicLayoutNode(() => new ScrollableContainerNode());
                    return _statefulNode;
                }

                public void OnSafeChanged() => _statelessNode?.Invalidate();
                public void OnStatefulChanged() => _statefulNode?.{|#0:Invalidate|}();
            }
            """,
            "_statefulNode", "ScrollableContainerNode"
        },
    };

    [Theory]
    [MemberData(nameof(SuccessCases))]
    public Task SuccessCase(string testCode)
        => Verify.VerifyAnalyzer([TerminaFrameworkSource, testCode]);

    [Theory]
    [MemberData(nameof(FailureCases))]
    public Task FailureCase(string testCode, string dynamicField, string statefulType)
        => Verify.VerifyAnalyzer(
            [TerminaFrameworkSource, testCode],
            Verify.Diagnostic(StatefulLayoutNodeRecreationAnalyzer.DiagnosticId)
                .WithLocation(0)
                .WithArguments(dynamicField, statefulType)
                .WithSeverity(DiagnosticSeverity.Warning));
}
