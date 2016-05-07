using System;
using System.ComponentModel;

namespace PoMo.Client.Views
{
    [TypeDescriptionProvider(typeof(DataBoundTypeDescriptionProvider))]
    public sealed class DataBoundObject : INotifyPropertyChanged
    {
        private readonly DataBoundObjectCollection _collection;
        private readonly object[] _values;

        internal DataBoundObject(DataBoundObjectCollection collection, object[] values)
        {
            this._collection = collection;
            this._values = values;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public object GetValue(int ordinal)
        {
            return this._values[ordinal];
        }

        public object GetValue(string columnName)
        {
            return this._collection.Properties.Find(columnName, false)?.GetValue(this);
        }

        internal void OnPropertyChanged(int ordinal, object value)
        {
            if (object.Equals(this._values[ordinal], value))
            {
                return;
            }
            this._values[ordinal] = value;
            this.OnPropertyChanged(this._collection.Properties[ordinal].Name);
        }

        private void OnPropertyChanged(string propertyName)
        {
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private sealed class DataBoundTypeDescriptionProvider : TypeDescriptionProvider
        {
            public override ICustomTypeDescriptor GetTypeDescriptor(Type objectType, object instance)
            {
                return instance == null ? base.GetTypeDescriptor(objectType, null) : ((DataBoundObject)instance)._collection.CustomTypeDescriptor;
            }
        }
    }
}