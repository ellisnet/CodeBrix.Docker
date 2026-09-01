using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading;

namespace RedisSetupTool.DockerManagement;

//The CodeBrix.Docker project reference carries PrivateAssets=all, which is what stops any downstream
//  project compiling against its types - the D17 boundary. That same setting keeps the assembly out
//  of every consumer's deps.json, so the default load context cannot find CodeBrix.Docker.dll even
//  though the build copies the file next to this assembly. This resolver closes the gap once, inside
//  the one library that owns the reference, so the heads, RedisSetupTool.Core and the test projects
//  need no plumbing of their own. DockerManager's static constructor is the single trigger.
internal static class DockerAssemblyResolver
{
    private const string DockerAssemblyName = "CodeBrix.Docker";

    private static int _registered;

    internal static void EnsureRegistered()
    {
        if (Interlocked.Exchange(ref _registered, 1) == 0)
        {
            AssemblyLoadContext.Default.Resolving += ResolveDockerAssembly;
        }
    }

    private static Assembly ResolveDockerAssembly(AssemblyLoadContext context, AssemblyName name)
    {
        if (!string.Equals(name?.Name, DockerAssemblyName, StringComparison.Ordinal))
        {
            return null;
        }

        var directory = Path.GetDirectoryName(typeof(DockerAssemblyResolver).Assembly.Location);
        if (string.IsNullOrEmpty(directory))
        {
            return null;
        }

        var candidate = Path.Combine(directory, DockerAssemblyName + ".dll");
        return File.Exists(candidate) ? context.LoadFromAssemblyPath(candidate) : null;
    }
}
