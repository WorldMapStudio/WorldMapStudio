using System;
using System.Collections.Generic;
using Jint;
using Jint.Native;
using Jint.Native.Object;
using Jint.Runtime;
using Jint.Runtime.Interop;

namespace WorldMapStudio;

/// <summary>
/// Wraps the shared Jint engine every script call (console, and later the HTTP endpoint) runs
/// against — one instance per editor session, so state persists across calls the way Blender's
/// console and script text-blocks share one Python interpreter (see ScriptingPlan.md, decision #8).
///
/// The engine never gets raw CLR access. Two mechanisms enforce that, for two different shapes of
/// object: <see cref="TypeResolver.MemberFilter"/>, set once here to <see cref="ScriptReflection.IsVisible"/>,
/// filters every plain bound object (modules, and anything without special handling) down to
/// [ScriptProperty]/[ScriptFunction] members automatically; <see cref="EntityProxyHandler"/>, installed
/// below via <c>WrapObjectHandler</c>, does the equivalent for <see cref="ScriptEntityHandle"/>
/// specifically, because entities need direct property assignment to route through
/// <see cref="ScriptPropertyEditCommand"/> instead of a raw CLR setter — something MemberFilter alone
/// cannot express (see EntityProxyHandler's doc comment). Both read from the same
/// <see cref="ScriptReflection"/> metadata the .d.ts generator uses, so they can't disagree about
/// what's exposed.
/// </summary>
public sealed class ScriptEngineHost
{
    private readonly Engine _engine;

    public ScriptEngineHost(IEnumerable<IScriptModule> modules)
    {
        _engine = new Engine(options =>
        {
            options.SetTypeResolver(new TypeResolver { MemberFilter = ScriptReflection.IsVisible });
            options.TimeoutInterval(TimeSpan.FromSeconds(5));
            options.MaxStatements(2_000_000);

            // Entity handles get a hand-built Proxy instead of Jint's automatic CLR wrapping — see
            // EntityProxyHandler for why. Everything else must fall through to Jint's own default
            // wrapper *explicitly* — returning null here does not mean "use the default", it means
            // "this object wraps to nothing", which silently turns every other bound value (including
            // the "wms" global itself) into undefined. Confirmed against the real package: a handler
            // that unconditionally returns null breaks `engine.SetValue("wms", ...)` outright.
            options.Interop.WrapObjectHandler = (engine, target, type) => target is ScriptEntityHandle handle
                ? engine.Advanced.CreateProxy((ObjectInstance)engine.Evaluate("({})"), new EntityProxyHandler(engine, handle))
                : ObjectWrapper.Create(engine, target, type);
        });

        var wms = new Dictionary<string, object>();
        foreach (IScriptModule module in modules)
        {
            wms[module.Name] = module;
        }

        _engine.SetValue("wms", wms);
    }

    /// <summary>
    /// Evaluates one snippet and returns a display string of the result, or an error message —
    /// never throws. This is the console's eval path; later callers (the HTTP endpoint) will want the
    /// raw <see cref="JsValue"/> instead, but nothing needs that yet.
    /// </summary>
    public string Evaluate(string code)
    {
        try
        {
            JsValue result = _engine.Evaluate(code);
            return result.IsUndefined() ? "undefined" : result.ToString();
        }
        catch (JavaScriptException e)
        {
            return $"Error: {e.Message}";
        }
        catch (Exception e)
        {
            return $"Error: {e.GetType().Name}: {e.Message}";
        }
    }
}
