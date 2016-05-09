using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Data.Entity;
using System.Linq;
using System.Reflection;
using PoMo.Common;
using PoMo.Common.DataObjects;
using PoMo.Data;
using PoMo.Data.Models;

namespace PoMo.Server
{
    public interface IPortfolioData : ISubscribable
    {
        event EventHandler<DataEventArgs<RowChangeBase[]>> DataChanged;

        string PortfolioId
        {
            get;
        }

        string PortfolioName
        {
            get;
        }

        DataTable GetData();
    }

    internal sealed class PortfolioData : IPortfolioData
    {
        private readonly DataTable _dataTable;
        private readonly Portfolio _portfolio;
        private readonly List<Position> _positions;
        private readonly HashSet<string> _subscribers;

        public PortfolioData(Portfolio portfolio, IDataContext dataContext)
        {
            this._portfolio = portfolio;
            this._dataTable = PositionData.SchemaTable.Clone();
            this._dataTable.TableName = portfolio.Id;
            this._subscribers = new HashSet<string>();
            this._positions = new List<Position>();
            IOrderedQueryable<IGrouping<Security, Trade>> groups = dataContext.Trades
                .Where(trade => trade.PortfolioId == portfolio.Id)
                .Include(trade => trade.Security)
                .GroupBy(trade => trade.Security)
                .OrderBy(group => group.Key.Ticker);
            foreach (IGrouping<Security, Trade> group in groups)
            {
                Position position = new Position { Ticker = group.Key.Ticker, Description = group.Key.Description };
                foreach (Trade trade in group.OrderBy(trade => trade.TradeDate))
                {
                    PortfolioData.ProcessTrade(position, trade.Quantity, trade.Price);
                }
                this._positions.Add(position);
                this._dataTable.Rows.Add(this.CreateRow(position));
            }
        }

        public event EventHandler<DataEventArgs<RowChangeBase[]>> DataChanged;

        public string PortfolioId => this._portfolio.Id;

        public string PortfolioName => this._portfolio.Name;

        public DataTable GetData()
        {
            lock (this)
            {
                return this._dataTable.FullClone();
            }
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

        public void ProcessPriceChanges(IEnumerable<PriceChange> priceChanges)
        {
            PositionChangeProcessor changeProcessor = new PositionChangeProcessor(this._dataTable);
            lock (this)
            {
                foreach (PriceChange priceChange in priceChanges)
                {
                    int index = this._positions.BinarySearchByValue(priceChange.Ticker, pos => pos.Ticker);
                    if (index < 0)
                    {
                        continue;
                    }
                    Position position = this._positions[index];
                    position.PropertyChanged += changeProcessor.PropertyChangedEventHandler;
                    PortfolioData.CalculatePosition(position, priceChange.Price);
                    position.PropertyChanged -= changeProcessor.PropertyChangedEventHandler;
                }
                if (changeProcessor.RowChanges.Count == 0)
                {
                    return;
                }
                this._dataTable.AcceptChanges();
            }
            this.OnDataChanged(changeProcessor.RowChanges);
        }

        public void ProcessTrades(IEnumerable<Trade> trades)
        {
            List<RowChangeBase> list = new List<RowChangeBase>();
            PositionChangeProcessor changeProcessor = new PositionChangeProcessor(this._dataTable);
            lock (this)
            {
                foreach (Trade trade in trades)
                {
                    int index = this._positions.BinarySearchByValue(trade.Security.Ticker, pos => pos.Ticker);
                    if (index < 0)
                    {
                        Position position = new Position { Ticker = trade.Security.Ticker, Description = trade.Security.Description };
                        PortfolioData.ProcessTrade(position, trade.Quantity, trade.Price);
                        this._positions.Insert(~index, position);
                        DataRow dataRow = this.CreateRow(position);
                        this._dataTable.Rows.InsertAt(dataRow, ~index);
                        list.Add(new RowAdded { RowKey = position.Ticker, Data = dataRow.ItemArray });
                    }
                    else
                    {
                        Position position = this._positions[index];
                        position.PropertyChanged += changeProcessor.PropertyChangedEventHandler;
                        PortfolioData.ProcessTrade(position, trade.Quantity, trade.Price);
                        position.PropertyChanged -= changeProcessor.PropertyChangedEventHandler;
                    }
                }
            }
            this.OnDataChanged(list.Count == 0 ? changeProcessor.RowChanges : list.Concat(changeProcessor.RowChanges));
        }

        private static void CalculatePosition(Position position, decimal price)
        {
            position.LastPrice = price;
            position.MarkToMarket = position.Quantity * price;
            position.Pnl = position.MarkToMarket - position.CostBasis + position.Cash;
        }

        private static void ProcessTrade(Position position, int quantity, decimal price)
        {
            if (quantity == 0)
            {
                return;
            }
            position.Quantity += quantity;
            decimal tradeValue = quantity * price;
            if (tradeValue < 0)
            {
                position.Cash -= tradeValue;
            }
            else
            {
                position.CostBasis += tradeValue;
            }
            PortfolioData.CalculatePosition(position, price);
        }

        private DataRow CreateRow(Position position)
        {
            DataRow dataRow = this._dataTable.NewRow();
            foreach (PropertyInfo property in PositionData.Properties)
            {
                dataRow[property.Name] = position[property.Name];
            }
            return dataRow;
        }

        private void OnDataChanged(IEnumerable<RowChangeBase> changes)
        {
            this.DataChanged?.Invoke(this, DataEventArgs.Create(changes as RowChangeBase[] ?? changes.ToArray()));
        }

        private sealed class PositionChangeProcessor
        {
            private readonly DataTable _dataTable;
            private readonly List<RowColumnsChanged> _rowChanges = new List<RowColumnsChanged>();

            public PositionChangeProcessor(DataTable dataTable)
            {
                this._dataTable = dataTable;
                this.PropertyChangedEventHandler = this.Position_PropertyChanged;
            }

            public PropertyChangedEventHandler PropertyChangedEventHandler
            {
                get;
            }

            public IReadOnlyCollection<RowColumnsChanged> RowChanges => this._rowChanges;

            private void Position_PropertyChanged(object sender, PropertyChangedEventArgs e)
            {
                Position position = (Position)sender;
                DataRow dataRow = this._dataTable.Rows.Find(position.Ticker);
                object newValue = dataRow[e.PropertyName] = position[e.PropertyName] ?? DBNull.Value;
                int index = this._rowChanges.BinarySearchByValue(position.Ticker, change => (string)change.RowKey);
                RowColumnsChanged rowChange;
                if (index < 0)
                {
                    this._rowChanges.Insert(~index, rowChange = new RowColumnsChanged { RowKey = position.Ticker });
                }
                else
                {
                    rowChange = this._rowChanges[index];
                }
                int columnOrdinal = PositionData.SchemaTable.Columns.IndexOf(e.PropertyName);
                if ((index = rowChange.ColumnChanges.BinarySearchByValue(columnOrdinal, columnChange => columnChange.ColumnOrdinal)) < 0)
                {
                    rowChange.ColumnChanges.Insert(~index, new ColumnChange { ColumnOrdinal = columnOrdinal, Value = newValue });
                }
                else
                {
                    rowChange.ColumnChanges[index].Value = newValue;
                }
            }
        }
    }
}