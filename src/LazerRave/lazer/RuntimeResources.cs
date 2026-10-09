using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

namespace LazerRave.Lazer;

internal static class RuntimeResources
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        AssemblyLoadContext.Default.Resolving += ResolveResource;
    }

    private static Assembly? ResolveResource(AssemblyLoadContext context, AssemblyName name)
    {
        if (name.Name == "osu.Game.Resources" && string.IsNullOrEmpty(name.CultureName))
        {
            string resourcePack = Path.Combine(AppContext.BaseDirectory, "Resources", "osu.Game.Resources.dll");
            return File.Exists(resourcePack) ? context.LoadFromAssemblyPath(resourcePack) : null;
        }

        if (name.Name is not ("LazerRave.resources" or "osu.Game.Resources.resources" or "Humanizer.resources")
            || string.IsNullOrEmpty(name.CultureName))
            return null;

        CultureInfo culture;
        try { culture = CultureInfo.GetCultureInfo(name.CultureName); }
        catch (CultureNotFoundException) { return null; }

        if (string.IsNullOrEmpty(culture.Name) || culture.Name.IndexOfAny(['/', '\\', ':']) >= 0)
            return null;

        string path = Path.Combine(AppContext.BaseDirectory, "Localization", culture.Name, name.Name + ".dll");
        return File.Exists(path) ? context.LoadFromAssemblyPath(path) : null;
    }
}
