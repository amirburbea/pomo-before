using System;

namespace PoMo.Common.DataObjects
{
    [Serializable]
    public sealed class ColumnChange
    {
        private int _columnOrdinal;
        private object _value;

        public int ColumnOrdinal
        {
            get
            {
                return this._columnOrdinal;
            }
            set
            {
                this._columnOrdinal = value;
            }
        }

        public object Value
        {
            get
            {
                return this._value;
            }
            set
            {
                this._value = value;
            }
        }
    }
}