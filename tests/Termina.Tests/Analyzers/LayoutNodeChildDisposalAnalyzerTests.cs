// Copyright (c) Petabridge, LLC. All rights reserved.
// Licensed under the Apache 2.0 license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using Termina.Generators;
using Termina.Tests.Utility;

namespace Termina.Tests.Analyzers;

using Verify = TerminaVerifier<LayoutNodeChildDisposalAnalyzer>;

/// <summary>
/// Tests for <see cref="LayoutNodeChildDisposalAnalyzer"/> (TERMINA003).
///
/// SuccessCases are the happy path (correct code, no diagnostic). FailureCases are the sad path (a container
/// that disposes a `this`-held layout node child outside its own teardown). Each failure case marks the
/// expected location with <c>{|#0:Dispose|}</c> markup and passes the member name as the message argument.
/// </summary>
public sealed class LayoutNodeChildDisposalAnalyzerTests
{
    private const string TerminaFrameworkSource = """
        namespace Termina.Layout
        {
            public interface ILayoutNode : System.IDisposable { }

            public interface IActivatableNode : ILayoutNode
            {
                void OnActivate();
                void OnDeactivate();
            }

            public abstract class LayoutNode : ILayoutNode
            {
                public virtual void Dispose() { }
            }

            public sealed class TextNode : LayoutNode
            {
                public TextNode(string text) { }
            }

            public sealed class ContentNode : LayoutNode, IActivatableNode
            {
                public void OnActivate() { }
                public void OnDeactivate() { }
            }

            public sealed class Holder
            {
                public ContentNode? Child { get; set; }
            }

            public sealed class FakeObservable
            {
                public System.IDisposable Subscribe(System.Action<object> onNext) => null!;
            }
        }
        """;

    // -------------------------------------------------------------------------------------------------
    // Happy path: correct code that keeps children alive, or disposes only during teardown. No warning.
    // -------------------------------------------------------------------------------------------------
    public static readonly TheoryData<string> SuccessCases = new()
    {
        // N1: dispose the child inside the container's own Dispose() override (final teardown).
        """
        using Termina.Layout;

        public sealed class MyContainer : LayoutNode
        {
            private ContentNode? _currentChild;

            public override void Dispose()
            {
                _currentChild?.Dispose();
                base.Dispose();
            }
        }
        """,

        // N2: dispose the child inside DisposeAsync() (also final teardown).
        """
        using Termina.Layout;

        public sealed class MyContainer : LayoutNode
        {
            private ContentNode? _currentChild;

            public System.Threading.Tasks.ValueTask DisposeAsync()
            {
                _currentChild?.Dispose();
                return default;
            }
        }
        """,

        // N3: deactivate the old child on a switch. There is no Dispose() call.
        """
        using Termina.Layout;

        public sealed class MyContainer : LayoutNode
        {
            private ContentNode? _currentChild;

            public void SwitchTo(ContentNode next)
            {
                _currentChild?.OnDeactivate();
                _currentChild = next;
            }
        }
        """,

        // N4: dispose a non-layout IDisposable (a subscription). Not an ILayoutNode.
        """
        using Termina.Layout;

        public sealed class MyContainer : LayoutNode
        {
            private System.IDisposable? _subscription;

            public void Refresh()
            {
                _subscription?.Dispose();
            }
        }
        """,

        // N5: the enclosing type is not a LayoutNode (for example a page). Out of scope.
        """
        using Termina.Layout;

        public sealed class MyPage
        {
            private ContentNode? _child;

            public void Close()
            {
                _child?.Dispose();
            }
        }
        """,

        // N6: dispose a local layout node, not a field or property. Out of scope.
        """
        using Termina.Layout;

        public sealed class MyContainer : LayoutNode
        {
            public void Rebuild()
            {
                var temp = new ContentNode();
                temp.Dispose();
            }
        }
        """,

        // N7: dispose the child inside a finalizer (teardown, not a content switch).
        """
        using Termina.Layout;

        public sealed class MyContainer : LayoutNode
        {
            private ContentNode? _currentChild;

            ~MyContainer()
            {
                _currentChild?.Dispose();
            }
        }
        """,

        // N8: dispose the child inside a Dispose(bool) method (the classic dispose pattern).
        """
        using Termina.Layout;

        public sealed class MyContainer : LayoutNode
        {
            private ContentNode? _currentChild;

            public override void Dispose()
            {
                Dispose(true);
                base.Dispose();
            }

            private void Dispose(bool disposing)
            {
                if (disposing)
                    _currentChild?.Dispose();
            }
        }
        """,

        // N9: dispose a layout node that belongs to another instance, not this container.
        """
        using Termina.Layout;

        public sealed class MyContainer : LayoutNode
        {
            private ContentNode? _currentChild;

            public void Detach(MyContainer other)
            {
                other._currentChild?.Dispose();
            }
        }
        """,

        // N10: dispose a layout node reached through another object (a holder's member).
        """
        using Termina.Layout;

        public sealed class MyContainer : LayoutNode
        {
            private Holder _holder = new Holder();

            public void Clear()
            {
                _holder.Child?.Dispose();
            }
        }
        """,

        // N11: dispose a collection element, not a direct member. Out of scope.
        """
        using Termina.Layout;

        public sealed class MyContainer : LayoutNode
        {
            private System.Collections.Generic.List<ContentNode> _children = new();

            public void Replace()
            {
                _children[0].Dispose();
            }
        }
        """,

        // N12: dispose inside a lambda that is itself inside Dispose(). Still teardown.
        """
        using Termina.Layout;

        public sealed class MyContainer : LayoutNode
        {
            private ContentNode? _currentChild;
            private FakeObservable _source = new FakeObservable();

            public override void Dispose()
            {
                _source.Subscribe(v => _currentChild?.Dispose());
                base.Dispose();
            }
        }
        """,
    };

    // -------------------------------------------------------------------------------------------------
    // Sad path: a LayoutNode container disposes a this-held layout node child outside its teardown.
    // Tuple: (source with {|#0:Dispose|} markup, disposed member name).
    // -------------------------------------------------------------------------------------------------
    public static readonly TheoryData<string, string> FailureCases = new()
    {
        // S1: dispose the child in a content-switch method.
        {
            """
            using Termina.Layout;

            public sealed class MyContainer : LayoutNode
            {
                private ContentNode? _currentChild;

                public void SwitchTo(ContentNode next)
                {
                    _currentChild?.{|#0:Dispose|}();
                    _currentChild = next;
                }
            }
            """,
            "_currentChild"
        },

        // S2: dispose the child inside a Subscribe(...) handler (the shape of #70).
        {
            """
            using Termina.Layout;

            public sealed class MyContainer : LayoutNode
            {
                private ILayoutNode? _currentChild;
                private FakeObservable _source = new FakeObservable();

                public MyContainer()
                {
                    _source.Subscribe(v =>
                    {
                        _currentChild?.{|#0:Dispose|}();
                        _currentChild = new TextNode("next");
                    });
                }
            }
            """,
            "_currentChild"
        },

        // S3: dispose the old child, then assign the new one, via a plain (non-conditional) call.
        {
            """
            using Termina.Layout;

            public sealed class MyContainer : LayoutNode
            {
                private ContentNode _currentChild = new ContentNode();

                public void Replace(ContentNode next)
                {
                    _currentChild.{|#0:Dispose|}();
                    _currentChild = next;
                }
            }
            """,
            "_currentChild"
        },

        // S4: dispose a layout node exposed as a property.
        {
            """
            using Termina.Layout;

            public sealed class MyContainer : LayoutNode
            {
                private ContentNode? Current { get; set; }

                public void Clear()
                {
                    Current?.{|#0:Dispose|}();
                    Current = null;
                }
            }
            """,
            "Current"
        },

        // S5: the child is disposed through an explicit `this.` qualifier.
        {
            """
            using Termina.Layout;

            public sealed class MyContainer : LayoutNode
            {
                private ContentNode _currentChild = new ContentNode();

                public void Replace(ContentNode next)
                {
                    this._currentChild.{|#0:Dispose|}();
                    _currentChild = next;
                }
            }
            """,
            "_currentChild"
        },

        // S6: null-forgiving receiver (idiomatic in a nullable-enabled repo).
        {
            """
            using Termina.Layout;

            public sealed class MyContainer : LayoutNode
            {
                private ContentNode? _currentChild;

                public void Replace(ContentNode next)
                {
                    _currentChild!.{|#0:Dispose|}();
                    _currentChild = next;
                }
            }
            """,
            "_currentChild"
        },

        // S7: parenthesized receiver.
        {
            """
            using Termina.Layout;

            public sealed class MyContainer : LayoutNode
            {
                private ContentNode _currentChild = new ContentNode();

                public void Replace(ContentNode next)
                {
                    (_currentChild).{|#0:Dispose|}();
                    _currentChild = next;
                }
            }
            """,
            "_currentChild"
        },

        // S8: dispose the old child inside a property setter that switches content (a #70 shape).
        {
            """
            using Termina.Layout;

            public sealed class MyContainer : LayoutNode
            {
                private ContentNode? _currentChild;

                public ContentNode? Content
                {
                    set
                    {
                        _currentChild?.{|#0:Dispose|}();
                        _currentChild = value;
                    }
                }
            }
            """,
            "_currentChild"
        },

        // S9: dispose a base-qualified inherited field.
        {
            """
            using Termina.Layout;

            public abstract class BaseContainer : LayoutNode
            {
                protected ContentNode? Child;
            }

            public sealed class MyContainer : BaseContainer
            {
                public void SwitchTo(ContentNode next)
                {
                    base.Child?.{|#0:Dispose|}();
                    Child = next;
                }
            }
            """,
            "Child"
        },
    };

    [Theory]
    [MemberData(nameof(SuccessCases))]
    public Task SuccessCase(string testCode)
        => Verify.VerifyAnalyzer([TerminaFrameworkSource, testCode]);

    [Theory]
    [MemberData(nameof(FailureCases))]
    public Task FailureCase(string testCode, string memberName)
        => Verify.VerifyAnalyzer(
            [TerminaFrameworkSource, testCode],
            Verify.Diagnostic(LayoutNodeChildDisposalAnalyzer.DiagnosticId)
                .WithLocation(0)
                .WithArguments(memberName)
                .WithSeverity(DiagnosticSeverity.Warning));
}
