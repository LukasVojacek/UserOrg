using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

using UserOrg.Shared.Persistance.Abstractions.Enums;

namespace Core.Domain.Exceptions
{
    /// <summary>
    /// Represents an error that occurs when an attempt is made to modify a record that is designated as read-only.
    /// </summary>
    /// <remarks>This exception is typically thrown to signal that a modification operation is not permitted
    /// due to the record's read-only status. Callers should catch this exception to handle scenarios where records must
    /// remain immutable, such as enforcing business rules or protecting data integrity.</remarks>
    public class RecordReadOnlyException : Exception
    {
        /// <summary>
        /// Initializes a new instance of the RecordReadOnlyException class.
        /// </summary>
        /// <remarks>This constructor creates a RecordReadOnlyException with no message or inner
        /// exception. Use this when you need to signal that a record is read-only without providing additional
        /// context.</remarks>
        public RecordReadOnlyException() { }

        /// <summary>
        /// Initializes a new instance of the RecordReadOnlyException class with a specified error message.
        /// </summary>
        /// <param name="message">The message that describes the error. This message is used to provide additional context about the
        /// exception.</param>
        public RecordReadOnlyException(string message) : base(message) { }

        /// <summary>
        /// Initializes a new instance of the RecordReadOnlyException class with a specified error message and a
        /// reference to the inner exception that caused this exception.
        /// </summary>
        /// <remarks>Use this constructor to provide additional context about the error and to preserve
        /// the original exception details when handling errors related to read-only record operations.</remarks>
        /// <param name="message">The error message that explains the reason for the exception.</param>
        /// <param name="innerException">The exception that is the cause of the current exception, or null if no inner exception is specified.</param>
        public RecordReadOnlyException(string message, Exception innerException) : base(message, innerException) { }

        /// <summary>
        /// Throws an exception if the specified operation mode is set to read-only, indicating that modifications are
        /// not allowed.
        /// </summary>
        /// <remarks>This method is useful for enforcing state constraints in scenarios where
        /// modifications are attempted in read-only contexts.</remarks>
        /// <param name="mode">The operation mode to check. If set to <see cref="EOperationMode.ReadOnly"/>, modifications are prohibited
        /// and an exception is thrown.</param>
        /// <param name="paramName">The name of the parameter being checked. Used in the exception message to identify which argument is in
        /// read-only mode.</param>
        public static void ThrowIfReadOnly([NotNull] EOperationMode mode, [CallerArgumentExpression(nameof(mode))] string? paramName = null)
        {
            if (mode == EOperationMode.ReadOnly)
            {
                Throw($"{paramName} is in read-only mode and cannot be modified.");
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="paramName"></param>
        /// <exception cref="RecordReadOnlyException"></exception>
        [DoesNotReturn]
        internal static void Throw(string? paramName) => throw new RecordReadOnlyException(paramName);
    }
}