using System;
using System.Threading;
using System.Windows.Threading;
using PoMo.Common;

namespace PoMo.Client.Views
{
    public abstract class ViewModelBase : NotifierBase, IDisposable
    {
        private int _busyCounter;

        protected ViewModelBase(Dispatcher dispatcher, IConnectionManager connectionManager)
        {
            this.Dispatcher = dispatcher;
            this.ConnectionManager = connectionManager;
            this.ConnectionManager.ConnectionStatusChanged += this.ConnectionManager_ConnectionStatusChanged;
        }

        public bool IsBusy => this._busyCounter != 0;

        protected IConnectionManager ConnectionManager
        {
            get;
        }

        protected Dispatcher Dispatcher
        {
            get;
        }

        public virtual void Dispose()
        {
            this.ConnectionManager.ConnectionStatusChanged -= this.ConnectionManager_ConnectionStatusChanged;
        }

        protected IDisposable CreateBusyScope()
        {
            return new BusyScope(this);
        }

        protected virtual void OnConnectionStatusChanged()
        {
        }

        protected virtual void OnIsBusyChanged()
        {
            this.OnPropertyChanged(nameof(this.IsBusy));
        }

        private void ConnectionManager_ConnectionStatusChanged(object sender, EventArgs e)
        {
            this.OnConnectionStatusChanged();
        }

        private sealed class BusyScope : IDisposable
        {
            private readonly ViewModelBase _viewModel;
            private bool _disposed;

            public BusyScope(ViewModelBase viewModel)
            {
                this._viewModel = viewModel;
                if (Interlocked.Increment(ref this._viewModel._busyCounter) == 1)
                {
                    this._viewModel.Dispatcher.InvokeAsync(this._viewModel.OnIsBusyChanged);
                }
            }

            public void Dispose()
            {
                if (this._disposed)
                {
                    return;
                }
                this._disposed = true;
                if (Interlocked.Decrement(ref this._viewModel._busyCounter) == 0)
                {
                    this._viewModel.Dispatcher.InvokeAsync(this._viewModel.OnIsBusyChanged);
                }
            }
        }
    }
}