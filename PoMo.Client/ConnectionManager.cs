using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.ServiceModel;
using System.ServiceModel.Channels;
using System.ServiceModel.Description;
using System.Threading;
using System.Threading.Tasks;
using PoMo.Common.DataObjects;
using PoMo.Common.ServiceModel;
using PoMo.Common.ServiceModel.Contracts;

namespace PoMo.Client
{
    public interface IConnectionManager
    {
        event EventHandler ConnectionStatusChanged;

        event EventHandler<ChangeEventArgs> FirmSummaryChanged;

        event EventHandler<PortfolioChangeEventArgs> PortfolioChanged;

        ConnectionStatus ConnectionStatus
        {
            get;
        }

        Task<PortfolioModel[]> GetPortfoliosAsync();

        Task<DataTable> SubscribeToFirmSummaryAsync();

        Task<DataTable> SubscribeToPortfolioAsync(string portfolioId);

        Task UnsubscribeFromFirmSummaryAsync();

        Task UnsubscribeFromPortfolioAsync(string portfolioId);
    }

    internal sealed class ConnectionManager : IConnectionManager, IDisposable
    {
        private const int HeartbeatPeriod = 60000;

        private readonly Thread _channelThread;
        private readonly DuplexChannelFactory<IServerContract> _factory;
        private readonly Timer _heartbeatTimer;
        private readonly ConcurrentQueue<IRequest> _requests = new ConcurrentQueue<IRequest>();
        private readonly AutoResetEvent _resetEvent;
        private IServerContract _channel;
        private bool _isDisposed;

        public ConnectionManager(Binding binding, IWcfSettings wcfSettings)
        {
            this._factory = new DuplexChannelFactory<IServerContract>(
                new InstanceContext(new Callback(this)),
                new ServiceEndpoint(
                    ContractDescription.GetContract(typeof(IServerContract)),
                    binding,
                    new EndpointAddress(wcfSettings.WcfUri)
                )
            );
            this._resetEvent = new AutoResetEvent(true);
            this._channelThread = new Thread(this.Run)
            {
                IsBackground = true,
                Priority = ThreadPriority.BelowNormal,
                Name = nameof(ConnectionManager)
            };
            this._channelThread.Start();
            this._heartbeatTimer = new Timer(this.Timer_Elapsed, null, ConnectionManager.HeartbeatPeriod, ConnectionManager.HeartbeatPeriod);
        }

        public event EventHandler ConnectionStatusChanged;

        public event EventHandler<ChangeEventArgs> FirmSummaryChanged;

        public event EventHandler<PortfolioChangeEventArgs> PortfolioChanged;

        private interface IRequest
        {
            bool Run(IServerContract channel);
        }

        public ConnectionStatus ConnectionStatus
        {
            get
            {
                if (this._channel == null)
                {
                    return ConnectionStatus.Disconnected;
                }
                ICommunicationObject communicationObject = (ICommunicationObject)this._channel;
                switch (communicationObject.State)
                {
                    case CommunicationState.Created:
                    case CommunicationState.Opening:
                        return ConnectionStatus.Connecting;
                    case CommunicationState.Opened:
                        return ConnectionStatus.Connected;
                }
                return ConnectionStatus.Disconnected;
            }
        }

        public void Dispose()
        {
            if (this._isDisposed)
            {
                return;
            }
            this._isDisposed = true;
            this._resetEvent.Set();
            this._channelThread.Join();
            if (this._channel != null)
            {
                ICommunicationObject communicationObject = (ICommunicationObject)this._channel;
                communicationObject.Closed -= this.Channel_ClosedOrFaulted;
                communicationObject.Faulted -= this.Channel_ClosedOrFaulted;
            }
            ServiceModelMethods.TryClose(Interlocked.Exchange(ref this._channel, null));
            ServiceModelMethods.TryClose(this._factory);
            using (this._heartbeatTimer)
            {
                this._heartbeatTimer.Change(Timeout.Infinite, Timeout.Infinite);
            }
        }

        Task<PortfolioModel[]> IConnectionManager.GetPortfoliosAsync()
        {
            return this.EnqueueRequest(channel => channel.GetPortfolios());
        }

        Task<DataTable> IConnectionManager.SubscribeToFirmSummaryAsync()
        {
            return this.EnqueueRequest(channel => channel.SubscribeToFirmSummary());
        }

        Task<DataTable> IConnectionManager.SubscribeToPortfolioAsync(string portfolioId)
        {
            return this.EnqueueRequest(channel => channel.SubscribeToPortfolio(portfolioId));
        }

        Task IConnectionManager.UnsubscribeFromFirmSummaryAsync()
        {
            return this.EnqueueRequest(channel => channel.UnsubscribeFromFirmSummary());
        }

        Task IConnectionManager.UnsubscribeFromPortfolioAsync(string portfolioId)
        {
            return this.EnqueueRequest(channel => channel.UnsubscribeFromPortfolio(portfolioId));
        }

        private void Channel_ClosedOrFaulted(object sender, EventArgs e)
        {
            ICommunicationObject communicationObject = (ICommunicationObject)sender;
            communicationObject.Closed -= this.Channel_ClosedOrFaulted;
            communicationObject.Faulted -= this.Channel_ClosedOrFaulted;
            Interlocked.CompareExchange(ref this._channel, null, (IServerContract)sender);
            this.OnConnectionStatusChanged();
            this._resetEvent.Set();
        }

        private Task EnqueueRequest(Action<IServerContract> action)
        {
            return this.EnqueueRequest(
                channel =>
                {
                    action(channel);
                    return default(object);
                }
            );
        }

        private Task<TResult> EnqueueRequest<TResult>(Func<IServerContract, TResult> function)
        {
            try
            {
                Request<TResult> request = new Request<TResult>(function);
                this._requests.Enqueue(request);
                return request.Task;
            }
            finally
            {
                this._resetEvent.Set();
            }
        }

        private IServerContract InitializeChannel()
        {
            IServerContract channel = this._factory.CreateChannel();
            ICommunicationObject communicationObject = (ICommunicationObject)channel;
            try
            {
                communicationObject.Open();
            }
            catch
            {
                ServiceModelMethods.TryClose(channel);
                return null;
            }
            communicationObject.Closed += this.Channel_ClosedOrFaulted;
            communicationObject.Faulted += this.Channel_ClosedOrFaulted;
            return channel;
        }

        private void OnConnectionStatusChanged()
        {
            this.ConnectionStatusChanged?.Invoke(this, EventArgs.Empty);
        }

        private void OnFirmSummaryChanged(IReadOnlyCollection<RowChangeBase> changes)
        {
            this.FirmSummaryChanged?.Invoke(this, new ChangeEventArgs(changes));
        }

        private void OnPortfolioChanged(string portfolioId, IReadOnlyCollection<RowChangeBase> changes)
        {
            this.PortfolioChanged?.Invoke(this, new PortfolioChangeEventArgs(portfolioId, changes));
        }

        private void Run()
        {
            while (!this._isDisposed && this._resetEvent.WaitOne())
            {
                try
                {
                    if (this._isDisposed)
                    {
                        return;
                    }
                    if (this._channel == null)
                    {
                        if ((this._channel = this.InitializeChannel()) == null)
                        {
                            this.OnConnectionStatusChanged();
                            continue;
                        }
                        this._channel.RegisterClient();
                        this.OnConnectionStatusChanged();
                    }
                    while (!this._isDisposed && this._channel != null && ((ICommunicationObject)this._channel).State == CommunicationState.Opened)
                    {
                        IRequest request;
                        if (!this._requests.TryDequeue(out request) || !request.Run(this._channel))
                        {
                            break;
                        }
                    }
                }
                catch (CommunicationException e)
                {
                    Debug.WriteLine(e);
                }
            }
        }

        private void Timer_Elapsed(object state)
        {
            if (!this._isDisposed)
            {
                this.EnqueueRequest(channel => channel.Heartbeat());
            }
        }

        private sealed class Callback : ICallbackContract
        {
            private readonly ConnectionManager _connectionManager;

            public Callback(ConnectionManager connectionManager)
            {
                this._connectionManager = connectionManager;
            }

            public void Heartbeat()
            {
                Debug.WriteLine("Heartbeat received from server.");
            }

            public void OnFirmSummaryChanged(RowChangeBase[] changes)
            {
                Task.Factory.StartNew(
                    () => this._connectionManager.OnFirmSummaryChanged(changes),
                    CancellationToken.None,
                    TaskCreationOptions.None,
                    TaskScheduler.Default
                );
            }

            public void OnPortfolioChanged(string portfolioId, RowChangeBase[] changes)
            {
                Task.Factory.StartNew(
                    () => this._connectionManager.OnPortfolioChanged(portfolioId, changes),
                    CancellationToken.None,
                    TaskCreationOptions.None,
                    TaskScheduler.Default
                );
            }
        }

        private sealed class Request<TResult> : IRequest
        {
            private readonly Func<IServerContract, TResult> _function;
            private readonly TaskCompletionSource<TResult> _taskCompletionSource;

            public Request(Func<IServerContract, TResult> function)
            {
                this._taskCompletionSource = new TaskCompletionSource<TResult>();
                this._function = function;
            }

            public Task<TResult> Task => this._taskCompletionSource.Task;

            public bool Run(IServerContract channel)
            {
                try
                {
                    this._taskCompletionSource.TrySetResult(this._function(channel));
                    return true;
                }
                catch (Exception e)
                {
                    this._taskCompletionSource.TrySetException(e);
                    return false;
                }
            }
        }
    }
}