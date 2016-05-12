using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Threading;
using PoMo.Common.DataObjects;

namespace PoMo.Client.Views.Shell
{
    public sealed class ShellViewModel : ViewModelBase
    {
        private readonly ObservableCollection<PortfolioModel> _portfolios;
        private ConnectionStatus _connectionStatus;
        private bool _isTabControlLocked;

        public ShellViewModel(Dispatcher dispatcher, IConnectionManager connectionManager)
            : base(dispatcher, connectionManager)
        {
            this.Portfolios = new ReadOnlyObservableCollection<PortfolioModel>(this._portfolios = new ObservableCollection<PortfolioModel>());
        }

        public ConnectionStatus ConnectionStatus
        {
            get
            {
                return this._connectionStatus;
            }
            private set
            {
                this.SetValue(ref this._connectionStatus, value);
            }
        }

        public bool IsTabControlLocked
        {
            get
            {
                return this._isTabControlLocked;
            }
            set
            {
                this.SetValue(ref this._isTabControlLocked, value);
            }
        }

        public ReadOnlyObservableCollection<PortfolioModel> Portfolios
        {
            get;
        }

        protected override void OnConnectionStatusChanged()
        {
            if ((this.ConnectionStatus = this.ConnectionManager.ConnectionStatus) != ConnectionStatus.Connected || this.Portfolios.Count != 0)
            {
                return;
            }
            IDisposable busyScope = this.CreateBusyScope();
            this.ConnectionManager.GetPortfoliosAsync()
                .ContinueWith(
                    task =>
                    {
                        using (busyScope)
                        {
                            if (!task.IsFaulted)
                            {
                                this.Dispatcher.BeginInvoke(
                                    DispatcherPriority.Normal,
                                    new Action<PortfolioModel[]>(portfolios => Array.ForEach(portfolios, this._portfolios.Add)),
                                    task.Result
                                );
                            }
                        }
                    }
                );
        }
    }
}