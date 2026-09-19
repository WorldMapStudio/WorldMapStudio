using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace WorldMapStudio;

public static class AssemblyTypes
{
    /// <summary>Every type in <paramref name="assembly"/> that loads; a partially loadable assembly
    /// still yields the types that did, and an unreadable one yields none.</summary>
    public static IEnumerable<Type> SafeGetTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException e)
        {
            return e.Types.Where(type => type is not null)!;
        }
        catch (Exception)
        {
            return [];
        }
    }
}
