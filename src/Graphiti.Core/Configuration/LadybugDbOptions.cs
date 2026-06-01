using Microsoft.Extensions.Options;

namespace Graphiti.Core.Configuration;

/// <summary>
/// Options for the LadybugDB graph driver.
/// </summary>
public sealed class LadybugDbOptions
{
    /// <summary>
    /// LadybugDB database path. The empty string uses the package's in-memory database.
    /// </summary>
    public string DatabasePath { get; set; } = string.Empty;
}

internal sealed class LadybugDbOptionsValidator : IValidateOptions<LadybugDbOptions>
{
    public ValidateOptionsResult Validate(string? name, LadybugDbOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return !string.IsNullOrEmpty(options.DatabasePath) && string.IsNullOrWhiteSpace(options.DatabasePath)
            ? ValidateOptionsResult.Fail("LadybugDbOptions.DatabasePath must not be blank when set.")
            : ValidateOptionsResult.Success;
    }
}
