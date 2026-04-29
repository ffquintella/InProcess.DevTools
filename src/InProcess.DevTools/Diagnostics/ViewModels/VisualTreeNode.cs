using System;
using System.Reactive.Disposables;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Diagnostics;
using Avalonia.Controls.Primitives;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Avalonia.Reactive;
using Lifetimes = Avalonia.Controls.ApplicationLifetimes;
using System.Linq;

namespace InProcess.DevTools.ViewModels
{
    internal class VisualTreeNode : TreeNode
    {
        public VisualTreeNode(AvaloniaObject avaloniaObject, TreeNode? parent, string? customName = null)
            : base(avaloniaObject, parent, customName)
        {
            Children = avaloniaObject switch
            {
                Visual visual => new VisualTreeNodeCollection(this, visual),
                Controls.Application host => new ApplicationHostVisuals(this, host),
                _ => TreeNodeCollection.Empty
            };

            if (Visual is StyledElement styleable)
                IsInTemplate = styleable.TemplatedParent != null;
        }

        public bool IsInTemplate { get; }

        public override TreeNodeCollection Children { get; }

        public static VisualTreeNode[] Create(object control)
        {
            return control is AvaloniaObject visual ?
                new[] { new VisualTreeNode(visual, null) } :
                Array.Empty<VisualTreeNode>();
        }

        internal class VisualTreeNodeCollection : TreeNodeCollection
        {
            private readonly Visual _control;
            private readonly CompositeDisposable _subscriptions = new CompositeDisposable(2);

            public VisualTreeNodeCollection(TreeNode owner, Visual control)
                : base(owner)
            {
                _control = control;
            }

            public override void Dispose()
            {
                _subscriptions.Dispose();
            }

            protected override void Initialize(AvaloniaList<TreeNode> nodes)
            {
                _subscriptions.Clear();

                foreach (var child in _control.GetVisualChildren())
                {
                    nodes.Add(new VisualTreeNode((AvaloniaObject)child, Owner));
                }
            }

            private struct PopupRoot
            {
                public PopupRoot(Control root, string? customName = null)
                {
                    Root = root;
                    CustomName = customName;
                }

                public Control Root { get; }
                public string? CustomName { get; }
            }
        }

        internal class ApplicationHostVisuals : TreeNodeCollection
        {
            readonly Controls.Application _application;
            CompositeDisposable _subscriptions = new CompositeDisposable(2);
            public ApplicationHostVisuals(TreeNode owner, Controls.Application host) :
                base(owner)
            {
                _application = host;
            }

            protected override void Initialize(AvaloniaList<TreeNode> nodes)
            {
                if (_application.ApplicationLifetime is Lifetimes.ISingleViewApplicationLifetime single &&
                    single.MainView is not null)
                {
                    nodes.Add(new VisualTreeNode(single.MainView, Owner));
                }
                if (_application.ApplicationLifetime is Lifetimes.IClassicDesktopStyleApplicationLifetime classic)
                {

                    for (int i = 0; i < classic.Windows.Count; i++)
                    {
                        var window = classic.Windows[i];
                        if (window is Views.MainWindow)
                        {
                            continue;
                        }
                        nodes.Add(new VisualTreeNode(window, Owner));
                    }
                    _subscriptions = new CompositeDisposable(2)
                    {
                        Window.WindowOpenedEvent.AddClassHandler(typeof(Window), (s,e)=>
                            {
                                if (s is Views.MainWindow)
                                {
                                    return;
                                }
                                nodes.Add(new VisualTreeNode((AvaloniaObject)s!,Owner));
                            }),
                        Window.WindowClosedEvent.AddClassHandler(typeof(Window), (s,e)=>
                            {
                                if (s is Views.MainWindow)
                                {
                                    return;
                                }
                                var item = nodes.FirstOrDefault(node=>object.ReferenceEquals(node.Visual,s));
                                if(!(item is null))
                                {
                                    nodes.Remove(item);
                                }
                            }),
                    };


                }
            }

            public override void Dispose()
            {
                _subscriptions?.Dispose();
                base.Dispose();
            }
        }
    }
}
