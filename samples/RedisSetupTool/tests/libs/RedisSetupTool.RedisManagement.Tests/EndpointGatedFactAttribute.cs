using System;
using System.Runtime.CompilerServices;
using Xunit;

namespace RedisSetupTool.RedisManagement.Tests;

/// <summary>
/// A fact that runs only when an environment variable names a live endpoint. The rest of this suite
/// is daemon-free, so this is the one way in and it stays shut unless asked for.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class EndpointGatedFactAttribute : FactAttribute
{
    /// <summary>Creates the attribute, setting the skip reason when the variable is unset.</summary>
    /// <param name="variable">The environment variable naming the endpoint.</param>
    /// <param name="sourceFilePath">Filled in by the compiler.</param>
    /// <param name="sourceLineNumber">Filled in by the compiler.</param>
    public EndpointGatedFactAttribute(string variable,
        [CallerFilePath] string sourceFilePath = null,
        [CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber)
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(variable)))
        {
            Skip = $"Set {variable}=host:port[,password] to run this test.";
        }
    }
}
