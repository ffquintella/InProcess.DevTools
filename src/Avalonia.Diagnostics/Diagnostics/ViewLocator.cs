using System;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Diagnostics.ViewModels;
using Avalonia.Diagnostics.Views;

namespace Avalonia.Diagnostics
{
    internal class ViewLocator : IDataTemplate
    {
        public Control? Build(object? data)
        {
            if (data is null)
                return null;

            return data switch
            {
                ControlDetailsViewModel => new ControlDetailsView(),
                EventsPageViewModel => new EventsPageView(),
                HotKeyPageViewModel => new HotKeyPageView(),
                MainViewModel => new MainView(),
                TreePageViewModel => new TreePageView(),
                _ => new TextBlock { Text = data.ToString() }
            };
        }

        public bool Match(object? data)
        {
            return data is ViewModelBase;
        }
    }
}
