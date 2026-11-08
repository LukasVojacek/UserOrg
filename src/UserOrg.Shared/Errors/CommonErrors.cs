using Lukdrasil.Error;

namespace UserOrg.Shared.Errors;

/// <summary>
/// Provides factory methods for creating common error instances used throughout the application.
/// </summary>
/// <remarks>This class contains static methods to standardize the creation of frequently used error types,
/// ensuring consistency in error reporting across different components.</remarks>
public static class CommonErrors
{
    /// <summary>
    /// Creates an error indicating that a method argument is invalid or missing.
    /// </summary>
    /// <param name="paramName">The name of the parameter that is invalid or missing. Cannot be null or empty.</param>
    /// <returns>An Error instance representing the invalid argument condition, with the specified parameter name included in the
    /// error description.</returns>
    public static Error InvalidArgument(string paramName) => new ErrorBuilder()
        .WithDescription("Invalid or missing argument: '{0}'.")
        .WithSeverity(ErrorSeverity.Error)
        .Build(paramName);
}