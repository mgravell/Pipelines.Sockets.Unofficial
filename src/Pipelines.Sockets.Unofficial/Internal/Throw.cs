using System;
using System.IO;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace Pipelines.Sockets.Unofficial.Internal
{
    internal static class Throw
    {
        internal static void ObjectDisposed(string objectName)
            => throw new ObjectDisposedException(objectName);

        internal static void InvalidOperation(string message = null)
        {
            if (string.IsNullOrWhiteSpace(message)) throw new InvalidOperationException();
            else throw new InvalidOperationException(message);
        }

        internal static void NotSupported(string message = null)
        {
            if (string.IsNullOrWhiteSpace(message)) throw new NotSupportedException();
            else throw new NotSupportedException(message);
        }

        internal static void ArgumentOutOfRange(string paramName)
            => throw new ArgumentOutOfRangeException(paramName);

        internal static void ArgumentNull(string paramName)
            => throw new ArgumentNullException(paramName);

        internal static void Timeout(string message = null)
        {
            if (string.IsNullOrWhiteSpace(message)) throw new TimeoutException();
            else throw new TimeoutException(message);
        }

        internal static void InvalidCast()
            => throw new InvalidCastException();

        internal static void EnumeratorOutOfRange()
            => throw new IndexOutOfRangeException("An attempt was made to progress the enumerator beyond the defined range");

        internal static void MultipleContinuations()
            => InvalidOperation($"Only one pending continuation is permitted");

        internal static void InvalidLockHolder()
            => InvalidOperation("Attempt to release a MutexSlim lock token that was not held");

        internal static void TaskCanceled()
            => throw new TaskCanceledException();

        internal static void Argument(string message, string paramName = null)
        {
            if (string.IsNullOrWhiteSpace(paramName)) throw new ArgumentException(message);
            else throw new ArgumentException(message, paramName);
        }

        internal static void Socket(int errorCode)
            => throw new SocketException(errorCode);

        internal static void FileNotFound(string message, string path)
            => throw new FileNotFoundException(message, path);

        internal static void IndexOutOfRange()
            => throw new IndexOutOfRangeException();

        internal static void SegmentDataUnavailable()
            => Throw.InvalidOperation("The segment data was not available");

        internal static void AllocationDuringEnumeration()
            => Throw.InvalidOperation("Data was allocated while the enumerator was iterating");
    }
}
