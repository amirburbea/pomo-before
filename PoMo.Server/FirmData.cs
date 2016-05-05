using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.Linq;
using System.Threading;
using PoMo.Common;
using PoMo.Common.DataObjects;
using PoMo.Data;
using PoMo.Data.Models;

namespace PoMo.Server
{
    public interface IFirmData : ISubscribable
    {
        event EventHandler<DataEventArgs<RowChangeBase[]>> SummaryChanged;

        IReadOnlyCollection<IPortfolioData> Portfolios
        {
            get;
        }

        DataTable GetSummary();

        bool TryGetPortfolio(string portfolioId, out IPortfolioData data);
    }

    internal sealed class FirmData : IFirmData, IDisposable
    {
        private readonly IMarketDataProvider _marketDataProvider;
        private readonly PortfolioDataCollection _portfolios = new PortfolioDataCollection();
        private readonly List<RowChangeBase> _rowChanges = new List<RowChangeBase>();
        private readonly HashSet<string> _subscribers = new HashSet<string>();
        private readonly DataTable _summary;
        private readonly string[] _tickers;
        private readonly Dictionary<string, decimal[]> _pnls = new Dictionary<string, decimal[]>();
        private readonly Timer _timer;
        private readonly ITradeFactory _tradeFactory;

        public FirmData(IDataContext dataContext, IMarketDataProvider marketDataProvider, ITradeFactory tradeFactory)
        {
            this._marketDataProvider = marketDataProvider;
            this._tradeFactory = tradeFactory;
            this._summary = new DataTable
            {
                TableName = nameof(FirmData),
                Columns =
                {
                    { "PortfolioId", typeof(string) },
                    { "PortfolioName", typeof(string) },
                    { "Pnl", typeof(decimal) }
                }
            };
            this._summary.PrimaryKey = new[] { this._summary.Columns[0] };
            this._tickers = dataContext.Securities.Select(security => security.Ticker).OrderBy(ticker => ticker).ToArray();
            foreach (Portfolio portfolio in dataContext.Portfolios.OrderBy(portfolio => portfolio.Id))
            {
                PortfolioData portfolioData = new PortfolioData(portfolio, dataContext);
                decimal portfolioPnl = 0m;
                decimal[] pnl = new decimal[this._tickers.Length];
                DataTable dataTable = portfolioData.GetData();
                for (int index = 0; index < this._tickers.Length; index++)
                {
                    string ticker = this._tickers[index];
                    DataRow dataRow = dataTable.Rows.Find(ticker);
                    if (dataRow == null)
                    {
                        continue;
                    }
                    portfolioPnl += (pnl[index] = dataRow.Field<decimal>("Pnl"));
                }
                DataRow summaryRow = this._summary.NewRow();
                summaryRow.SetField("PortfolioId", portfolio.Id);
                summaryRow.SetField("PortfolioName", portfolio.Name);
                summaryRow.SetField("Pnl", portfolioPnl);
                this._summary.Rows.Add(summaryRow);
                this._pnls.Add(portfolio.Id, pnl);
                portfolioData.DataChanged += this.PortfolioData_DataChanged;
                this._portfolios.Add(portfolioData);
            }
            this._tradeFactory.TradesCreated += this.TradeFactory_TradesCreated;
            this._marketDataProvider.PricesChanged += this.MarketDataProvider_PricesChanged;
            this._timer = new Timer(this.Timer_Elapsed, null, 5000, 4000);
        }

        public event EventHandler<DataEventArgs<RowChangeBase[]>> SummaryChanged;

        IReadOnlyCollection<IPortfolioData> IFirmData.Portfolios => this._portfolios;

        public void Dispose()
        {
            this._tradeFactory.TradesCreated -= this.TradeFactory_TradesCreated;
            this._marketDataProvider.PricesChanged -= this.MarketDataProvider_PricesChanged;
            foreach (PortfolioData portfolioData in this._portfolios)
            {
                portfolioData.DataChanged -= this.PortfolioData_DataChanged;
            }
            using (this._timer)
            {
                this._timer.Change(Timeout.Infinite, Timeout.Infinite);
            }
        }

        DataTable IFirmData.GetSummary()
        {
            lock (this._summary)
            {
                return this._summary.FullClone();
            }
        }

        bool IFirmData.TryGetPortfolio(string portfolioId, out IPortfolioData data)
        {
            PortfolioData value;
            if (this._portfolios.TryGetPortfolio(portfolioId, out value))
            {
                data = value;
                return true;
            }
            data = null;
            return false;
        }

        string[] ISubscribable.GetSubscribers()
        {
            lock (this._subscribers)
            {
                return this._subscribers.Count == 0 ? (string[])Enumerable.Empty<string>() : this._subscribers.ToArray();
            }
        }

        bool ISubscribable.Subscribe(string subscriberId, out int subscriberCount)
        {
            lock (this._subscribers)
            {
                bool added = this._subscribers.Add(subscriberId);
                subscriberCount = this._subscribers.Count;
                return added;
            }
        }

        bool ISubscribable.Unsubscribe(string subscriberId, out int subscriberCount)
        {
            lock (this._subscribers)
            {
                bool removed = this._subscribers.Remove(subscriberId);
                subscriberCount = this._subscribers.Count;
                return removed;
            }
        }

        private void MarketDataProvider_PricesChanged(object sender, DataEventArgs<IReadOnlyCollection<PriceChange>> e)
        {
            foreach (PortfolioData portfolioData in this._portfolios)
            {
                portfolioData.ProcessPriceChanges(e.Data);
            }
        }

        private void PortfolioData_DataChanged(object sender, DataEventArgs<RowChangeBase[]> e)
        {
            PortfolioData portfolioData = (PortfolioData)sender;
            decimal portfolioPnl;
            lock (this._summary)
            {
                decimal[] pnl = this._pnls[portfolioData.PortfolioId];
                DataRow summaryRow = this._summary.Rows.Find(portfolioData.PortfolioId);
                portfolioPnl = summaryRow.Field<decimal>("Pnl");
                int portfolioDataPnlOrdinal = PositionData.SchemaTable.Columns.IndexOf("Pnl");
                foreach (RowChangeBase rowChange in e.Data)
                {
                    string ticker = (string)rowChange.RowKey;
                    int index = Array.BinarySearch(this._tickers, ticker);
                    if (rowChange.ChangeType == RowChangeType.Added)
                    {
                        decimal rowPnl = (decimal)((RowAdded)rowChange).Data[portfolioDataPnlOrdinal];
                        portfolioPnl += (pnl[index] = rowPnl);
                    }
                    else
                    {
                        decimal currentRowPnl = pnl[index];
                        switch (rowChange.ChangeType)
                        {
                            case RowChangeType.Removed:
                                portfolioPnl -= currentRowPnl;
                                pnl[index] = 0m;
                                break;
                            case RowChangeType.ColumnsChanged:
                                ColumnChange columnChange = ((RowColumnsChanged)rowChange).ColumnChanges.FirstOrDefault(change => change.ColumnOrdinal == portfolioDataPnlOrdinal);
                                if (columnChange == null)
                                {
                                    continue;
                                }
                                decimal newRowPnl = (decimal)columnChange.Value;
                                pnl[index] = newRowPnl;
                                portfolioPnl += newRowPnl - currentRowPnl;
                                break;
                        }
                    }
                }
                summaryRow.SetField("Pnl", portfolioPnl);
                summaryRow.AcceptChanges();
            }
            lock (this._rowChanges)
            {
                int index = this._rowChanges.BinarySearchByValue(portfolioData.PortfolioId, change => (string)change.RowKey);
                if (index < 0)
                {
                    this._rowChanges.Insert(~index, new RowColumnsChanged
                    {
                        RowKey = portfolioData.PortfolioId,
                        ColumnChanges =
                        {
                            new ColumnChange
                            {
                                ColumnOrdinal = this._summary.Columns.IndexOf("Pnl"),
                                Value = portfolioPnl
                            }
                        }
                    });
                }
                else
                {
                    RowColumnsChanged rowChange = (RowColumnsChanged)this._rowChanges[index];
                    rowChange.ColumnChanges[0].Value = portfolioPnl;
                }
            }
        }

        private void Timer_Elapsed(object state)
        {
            RowChangeBase[] changes;
            lock (this._rowChanges)
            {
                if (this._rowChanges.Count == 0)
                {
                    return;
                }
                changes = this._rowChanges.ToArray();
                this._rowChanges.Clear();
            }
            this.SummaryChanged?.Invoke(this, DataEventArgs.Create(changes));
        }

        private void TradeFactory_TradesCreated(object sender, DataEventArgs<IReadOnlyCollection<Trade>> e)
        {
            foreach (IGrouping<string, Trade> group in e.Data.GroupBy(trade => trade.PortfolioId))
            {
                this._portfolios[group.Key].ProcessTrades(group);
            }
        }

        private sealed class PortfolioDataCollection : KeyedCollection<string, PortfolioData>
        {
            public PortfolioDataCollection()
                : base(StringComparer.Ordinal, 0)
            {
            }

            public bool TryGetPortfolio(string portfolioId, out PortfolioData data)
            {
                if (this.Dictionary != null)
                {
                    return this.Dictionary.TryGetValue(portfolioId, out data);
                }
                data = null;
                return false;
            }

            protected override string GetKeyForItem(PortfolioData item)
            {
                return item.PortfolioId;
            }
        }
    }
}