using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PoMo.Client.Controls;
using PoMo.Client.Views.FirmSummary;
using PoMo.Client.Views.Positions;
using PoMo.Common.DataObjects;
using PoMo.Common.Windsor;

namespace PoMo.Client.Views.Shell
{
    partial class ShellView
    {
        public static readonly RoutedCommand CloseTabCommand = new RoutedCommand(nameof(ShellView.CloseTabCommand), typeof(ShellView));
        public static readonly RoutedCommand CreateFirmSummaryViewCommand = new RoutedCommand(nameof(ShellView.CreateFirmSummaryViewCommand), typeof(ShellView));
        public static readonly RoutedCommand CreateViewCommand = new RoutedCommand(nameof(ShellView.CreateViewCommand), typeof(ShellView));

        internal static readonly object FirmSummaryHeader = new object();

        private readonly IFactory<FirmSummaryView> _firmSummaryViewFactory;
        private readonly IFactory<FirmSummaryViewModel> _firmSummaryViewModelFactory;
        private readonly IFactory<PositionsView> _positionsViewFactory;
        private readonly IFactory<PortfolioModel, PositionsViewModel> _positionsViewModelFactory;
        private int _ignoreSelectionChangedCounter;

        static ShellView()
        {
            CommandManager.RegisterClassCommandBinding(typeof(ShellView), new CommandBinding(ApplicationCommands.Close, ShellView.CloseCommand_Executed));
            CommandManager.RegisterClassCommandBinding(typeof(ShellView), new CommandBinding(ShellView.CreateFirmSummaryViewCommand, ShellView.CreateFirmSummaryViewCommand_Executed, ShellView.CreateFirmSummaryViewCommand_CanExecute));
            CommandManager.RegisterClassCommandBinding(typeof(ShellView), new CommandBinding(ShellView.CreateViewCommand, ShellView.CreateViewCommand_Executed, ShellView.CreateViewCommand_CanExecute));
            CommandManager.RegisterClassCommandBinding(typeof(ShellView), new CommandBinding(ShellView.CloseTabCommand, ShellView.CloseTabCommand_Executed));
        }

        public ShellView(
            IFactory<FirmSummaryView> firmSummaryViewFactory,
            IFactory<FirmSummaryViewModel> firmSummaryViewModelFactory,
            IFactory<PositionsView> positionsViewFactory,
            IFactory<PortfolioModel, PositionsViewModel> positionsViewModelFactory,
            ITabTearOffHandler tabTearOffHandler,
            ShellViewModel shellViewModel
        )
        {
            this.InitializeComponent();
            this._firmSummaryViewFactory = firmSummaryViewFactory;
            this._firmSummaryViewModelFactory = firmSummaryViewModelFactory;
            this._positionsViewFactory = positionsViewFactory;
            this._positionsViewModelFactory = positionsViewModelFactory;
            this.TabTearOffHandler = tabTearOffHandler;
            this.DataContext = shellViewModel;
            this.Loaded += this.ShellView_Loaded;
        }

        public ITabTearOffHandler TabTearOffHandler
        {
            get;
        }

        internal ShellViewModel ViewModel => this.DataContext as ShellViewModel;

        internal IDisposable CreateIgnoreSelectionChangedScope()
        {
            return new IgnoreSelectionChangedScope(this);
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (Application.Current.MainWindow.Equals(this))
            {
                ShellView other = Application.Current.Windows.OfType<ShellView>().FirstOrDefault(view => !view.Equals(this));
                if (other != null)
                {
                    Application.Current.MainWindow = other;
                }
            }
            this.CloseAllTabs();
            base.OnClosing(e);
        }

        private static void CloseCommand_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            ((Window)sender).Close();
        }

        private static void CloseTabCommand_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            ShellView shellView = (ShellView)sender;
            shellView.CloseTab(shellView.TabControl.ItemContainerGenerator.IndexFromContainer((DependencyObject)e.Parameter));
            if (shellView.TabControl.Items.Count == 0)
            {
                shellView.Close();
            }
        }

        private static void CreateFirmSummaryViewCommand_CanExecute(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = !ShellView.IsFirmSummaryTabOpen();
        }

        private static void CreateFirmSummaryViewCommand_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            ((ShellView)sender).CreateFirmSummaryTab();
        }

        private static void CreateViewCommand_CanExecute(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = !ShellView.IsTabOpen((PortfolioModel)e.Parameter);
        }

        private static void CreateViewCommand_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            ((ShellView)sender).CreateTab((PortfolioModel)e.Parameter);
        }

        private static IEnumerable<TViewModel> GetOpenTabs<TViewModel>()
            where TViewModel : class
        {
            return Application.Current.Windows.OfType<ShellView>()
                .SelectMany(shellView => shellView.TabControl.Items.Cast<TabItem>())
                .Select(tabItem => tabItem.DataContext as TViewModel)
                .Where(viewModel => viewModel != null);
        }

        private static bool IsFirmSummaryTabOpen()
        {
            return ShellView.GetOpenTabs<FirmSummaryViewModel>().Any();
        }

        private static bool IsTabOpen(PortfolioModel portfolio)
        {
            return ShellView.GetOpenTabs<PositionsViewModel>().Any(viewModel => viewModel.Portfolio.Id == portfolio.Id);
        }

        private void CloseAllTabs()
        {
            using (this.CreateIgnoreSelectionChangedScope())
            {
                for (int index = this.TabControl.Items.Count - 1; index != -1; index--)
                {
                    this.CloseTab(index);
                }
            }
        }

        private void CloseTab(int index)
        {
            TabItem item = (TabItem)this.TabControl.Items[index];
            ((SubscriberViewModelBase)item.DataContext).IsActive = false;
            this.TabControl.Items.RemoveAt(index);
            PositionsViewModel positionsViewModel = item.DataContext as PositionsViewModel;
            if (positionsViewModel != null)
            {
                this._positionsViewFactory.Release((PositionsView)item.Content);
                this._positionsViewModelFactory.Release(positionsViewModel);
                return;
            }
            FirmSummaryViewModel firmSummaryViewModel = item.DataContext as FirmSummaryViewModel;
            if (firmSummaryViewModel == null)
            {
                return;
            }
            this._firmSummaryViewFactory.Release((FirmSummaryView)item.Content);
            this._firmSummaryViewModelFactory.Release(firmSummaryViewModel);
        }

        private void CreateFirmSummaryTab()
        {
            this.TabControl.SelectedIndex = this.TabControl.Items.Add(new TabItem
            {
                DataContext = this._firmSummaryViewModelFactory.Create(),
                Header = FirmSummaryModel.Instance,
                HeaderTemplate = (DataTemplate)this.TryFindResource(FontWeights.SemiBold),
                Content = this._firmSummaryViewFactory.Create()
            });
        }

        private void CreateTab(PortfolioModel portfolio)
        {
            this.TabControl.SelectedIndex = this.TabControl.Items.Add(new TabItem
            {
                DataContext = this._positionsViewModelFactory.Create(portfolio),
                Header = portfolio,
                Content = this._positionsViewFactory.Create()
            });
        }

        private void ShellView_Loaded(object sender, RoutedEventArgs e)
        {
            this.Loaded -= this.ShellView_Loaded;
            if (Application.Current.Windows.OfType<ShellView>().All(view => view.Equals(this)))
            {
                this.CreateFirmSummaryTab();
            }
        }

        private void TabControl_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Middle)
            {
                return;
            }
            DependencyObject tabItem = (e.OriginalSource as DependencyObject)?.FindVisualTreeAncestor(typeof(TabItem));
            if (tabItem == null)
            {
                return;
            }
            ShellView.CloseTabCommand.Execute(tabItem, null);
            e.Handled = true;
        }

        private void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (this._ignoreSelectionChangedCounter != 0 || !this.TabControl.Equals(e.OriginalSource))
            {
                return;
            }
            foreach (TabItem tabItem in e.RemovedItems.Cast<TabItem>())
            {
                ((SubscriberViewModelBase)tabItem.DataContext).IsActive = false;
            }
            foreach (TabItem tabItem in e.AddedItems.Cast<TabItem>())
            {
                ((SubscriberViewModelBase)tabItem.DataContext).IsActive = true;
            }
        }

        private sealed class IgnoreSelectionChangedScope : IDisposable
        {
            private readonly ShellView _shellView;
            private bool _isDisposed;

            public IgnoreSelectionChangedScope(ShellView shellView)
            {
                this._shellView = shellView;
                Interlocked.Increment(ref this._shellView._ignoreSelectionChangedCounter);
            }

            public void Dispose()
            {
                if (this._isDisposed)
                {
                    return;
                }
                Interlocked.Decrement(ref this._shellView._ignoreSelectionChangedCounter);
                this._isDisposed = true;
                for (int index = 0; index < this._shellView.TabControl.Items.Count; index++)
                {
                    SubscriberViewModelBase viewModel = (SubscriberViewModelBase)((TabItem)this._shellView.TabControl.Items[index]).DataContext;
                    viewModel.IsActive = index == this._shellView.TabControl.SelectedIndex;
                }
            }
        }
    }
}