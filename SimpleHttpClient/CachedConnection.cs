using System;
using System.Collections.Generic;
using System.Text;

namespace SimpleHttpClient
{
    internal class CachedConnection
    {
        internal readonly HttpConnection _connection;

        public CachedConnection(HttpConnection connection)
        {
            _connection = connection;
        }

        public bool IsUsable()
        {
            return _connection.PollRead();
        }

        public bool Equals(CachedConnection other) => ReferenceEquals(other._connection, _connection);
        public override bool Equals(object obj) => obj is CachedConnection && Equals((CachedConnection)obj);
        public override int GetHashCode() => _connection?.GetHashCode() ?? 0;
    }
}
