using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace WorkflowBAP.Sage
{
    internal sealed class ComObjectTracker : IDisposable
    {
        private static readonly NLog.Logger Logger =
            NLog.LogManager.GetCurrentClassLogger();

        private readonly List<object> _objects =
            new List<object>();

        private readonly HashSet<object> _uniqueObjects =
            new HashSet<object>(
                ReferenceEqualityComparer.Instance);

        private bool _disposed;

        public T Track<T>(T value)
            where T : class
        {
            if (value != null
                && Marshal.IsComObject(value)
                && _uniqueObjects.Add(value))
            {
                _objects.Add(value);
            }

            return value;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            Logger.Info(
                "Début de la libération de {0} objet(s) COM Sage enfant(s).",
                _objects.Count);

            for (int index = _objects.Count - 1;
                 index >= 0;
                 index--)
            {
                Release(
                    _objects[index],
                    "objet enfant Sage");
            }

            Logger.Info(
                "{0} objet(s) COM Sage enfant(s) libéré(s).",
                _objects.Count);

            _objects.Clear();
            _uniqueObjects.Clear();
        }

        public static void Release(
            object value,
            string description)
        {
            if (value == null || !Marshal.IsComObject(value))
                return;

            try
            {
                Marshal.FinalReleaseComObject(value);
            }
            catch (Exception exception)
            {
                Logger.Warn(
                    exception,
                    "Impossible de libérer proprement l'objet COM {0}.",
                    description);
            }
        }

        private sealed class ReferenceEqualityComparer
            : IEqualityComparer<object>
        {
            public static readonly ReferenceEqualityComparer Instance =
                new ReferenceEqualityComparer();

            public new bool Equals(
                object x,
                object y)
            {
                return ReferenceEquals(x, y);
            }

            public int GetHashCode(
                object obj)
            {
                return RuntimeHelpers.GetHashCode(obj);
            }
        }
    }
}
