using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using PoMo.Common;

namespace PoMo.Client.Views
{
    public sealed class DataBoundObjectCollection : List<DataBoundObject>, IBindingList, IRaiseItemChangedEvents, ITypedList, IDisposable
    {
        private readonly DataTable _dataTable;
        private readonly Dictionary<DataRow, int> _rowIndices;
        private readonly IReadOnlyList<DataRow> _rowsWrapper;

        public DataBoundObjectCollection(DataTable dataTable)
        {
            this._dataTable = dataTable;
            this._rowsWrapper = new ReadOnlyDataRowCollectionWrapper(this._dataTable.Rows);
            PropertyDescriptor[] properties = new PropertyDescriptor[dataTable.Columns.Count];
            for (int index = 0; index < dataTable.Columns.Count; index++)
            {
                DataColumn column = dataTable.Columns[index];
                properties[index] = new ColumnPropertyDescriptor(
                    column.ColumnName,
                    index,
                    !column.AllowDBNull || !column.DataType.IsValueType ? column.DataType : typeof(Nullable<>).MakeGenericType(column.DataType)
                );
            }
            this.Properties = new PropertyDescriptorCollection(properties, true);
            this._rowIndices = new Dictionary<DataRow, int>(dataTable.Rows.Count);
            foreach (DataRow row in dataTable.Rows)
            {
                this.Add(new DataBoundObject(this, row.ItemArray));
                this._rowIndices.Add(row, this._rowIndices.Count);
            }
            this.CustomTypeDescriptor = new DataBoundTypeDescriptor(this.Properties);
            this._dataTable.RowChanged += this.DataTable_RowChanged;
            this._dataTable.TableCleared += this.DataTable_TableCleared;
            this._dataTable.RowDeleted += this.DataTable_RowDeleted;
            this._dataTable.ColumnChanged += this.DataTable_ColumnChanged;
        }

        public event ListChangedEventHandler ListChanged;

        public ICustomTypeDescriptor CustomTypeDescriptor
        {
            get;
        }

        bool IBindingList.AllowEdit => false;

        bool IBindingList.AllowNew => false;

        bool IBindingList.AllowRemove => false;

        bool IBindingList.IsSorted => false;

        ListSortDirection IBindingList.SortDirection => 0;

        PropertyDescriptor IBindingList.SortProperty => null;

        bool IBindingList.SupportsChangeNotification => true;

        bool IBindingList.SupportsSearching => false;

        bool IBindingList.SupportsSorting => false;

        public PropertyDescriptorCollection Properties
        {
            get;
        }

        bool IRaiseItemChangedEvents.RaisesItemChangedEvents => false;

        public void Dispose()
        {
            this._dataTable.RowChanged -= this.DataTable_RowChanged;
            this._dataTable.TableCleared -= this.DataTable_TableCleared;
            this._dataTable.ColumnChanged -= this.DataTable_ColumnChanged;
            this._dataTable.RowDeleted -= this.DataTable_RowDeleted;
            this.ListChanged = null;
        }

        void IBindingList.AddIndex(PropertyDescriptor property)
        {
            throw new NotSupportedException();
        }

        object IBindingList.AddNew()
        {
            throw new NotSupportedException();
        }

        void IBindingList.ApplySort(PropertyDescriptor property, ListSortDirection direction)
        {
            throw new NotSupportedException();
        }

        int IBindingList.Find(PropertyDescriptor property, object key)
        {
            throw new NotSupportedException();
        }

        void IBindingList.RemoveIndex(PropertyDescriptor property)
        {
            throw new NotSupportedException();
        }

        void IBindingList.RemoveSort()
        {
            throw new NotSupportedException();
        }

        PropertyDescriptorCollection ITypedList.GetItemProperties(PropertyDescriptor[] listAccessors)
        {
            return this.Properties;
        }

        string ITypedList.GetListName(PropertyDescriptor[] listAccessors)
        {
            return null;
        }

        private void DataTable_ColumnChanged(object sender, DataColumnChangeEventArgs e)
        {
            int rowIndex;
            if (this._rowIndices.TryGetValue(e.Row, out rowIndex))
            {
                this[rowIndex].OnPropertyChanged(e.Column.Ordinal, e.ProposedValue);
            }
        }

        private void DataTable_RowChanged(object sender, DataRowChangeEventArgs e)
        {
            if (e.Action != DataRowAction.Add)
            {
                return;
            }
            int rowIndex;
            switch (this._dataTable.PrimaryKey.Length)
            {
                case 1:
                    rowIndex = this._rowsWrapper.BinarySearch(e.Row, dataRow => dataRow[dataRow.Table.PrimaryKey[0]]);
                    break;
                default:
                    rowIndex = this._dataTable.Rows.IndexOf(e.Row);
                    break;
            }
            this.Insert(rowIndex, new DataBoundObject(this, e.Row.ItemArray));
            this._rowIndices.Add(e.Row, rowIndex);
            for (int index = rowIndex + 1; index < this._dataTable.Rows.Count; index++)
            {
                DataRow row = this._dataTable.Rows[index];
                this._rowIndices[row] = index;
            }
            this.OnListChanged(ListChangedType.ItemAdded, rowIndex);
        }

        private void DataTable_RowDeleted(object sender, DataRowChangeEventArgs e)
        {
            int rowIndex;
            if (!this._rowIndices.TryGetValue(e.Row, out rowIndex))
            {
                return;
            }
            this.RemoveAt(rowIndex);
            this._rowIndices.Remove(e.Row);
            for (int index = rowIndex; index < this._dataTable.Rows.Count; index++)
            {
                DataRow row = this._dataTable.Rows[index];
                this._rowIndices[row] = index;
            }
            this.OnListChanged(ListChangedType.ItemDeleted, rowIndex);
        }

        private void DataTable_TableCleared(object sender, DataTableClearEventArgs e)
        {
            this.Clear();
            this._rowIndices.Clear();
            this.OnListChanged(ListChangedType.Reset, -1);
        }

        private void OnListChanged(ListChangedType listChangedType, int rowIndex)
        {
            this.ListChanged?.Invoke(this, new ListChangedEventArgs(listChangedType, rowIndex));
        }

        private sealed class ColumnPropertyDescriptor : PropertyDescriptor
        {
            private readonly int _ordinal;

            public ColumnPropertyDescriptor(string name, int ordinal, Type propertyType)
                : base(name, null)
            {
                this._ordinal = ordinal;
                this.PropertyType = propertyType;
            }

            public override Type ComponentType => typeof(DataBoundObject);

            public override bool IsReadOnly => true;

            public override Type PropertyType
            {
                get;
            }

            public override bool CanResetValue(object component)
            {
                return false;
            }

            public override object GetValue(object component)
            {
                return ((DataBoundObject)component).GetValue(this._ordinal);
            }

            public override void ResetValue(object component)
            {
                throw new NotSupportedException();
            }

            public override void SetValue(object component, object value)
            {
                throw new NotSupportedException();
            }

            public override bool ShouldSerializeValue(object component)
            {
                return false;
            }
        }

        private sealed class DataBoundTypeDescriptor : CustomTypeDescriptor
        {
            private readonly PropertyDescriptorCollection _properties;

            public DataBoundTypeDescriptor(PropertyDescriptorCollection properties)
            {
                this._properties = properties;
            }

            public override PropertyDescriptorCollection GetProperties()
            {
                return this._properties;
            }

            public override PropertyDescriptorCollection GetProperties(Attribute[] attributes)
            {
                return this._properties;
            }
        }
    }
}