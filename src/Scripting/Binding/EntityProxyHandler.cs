using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Jint;
using Jint.Native;
using Jint.Native.Object;
using Jint.Runtime.Interop;

namespace WorldMapStudio;

/// <summary>
/// Makes a <see cref="ScriptEntityHandle"/> behave like a plain JS object — <c>entity.Name = "Lantern"</c>
/// works as direct assignment, and calling any [ScriptFunction] method works too — by intercepting every
/// access through Jint's ECMAScript Proxy machinery (<c>engine.Advanced.CreateProxy</c>) instead of
/// relying on its automatic reflection-based CLR object wrapping.
///
/// That automatic path was the original plan and doesn't work for this: Jint's
/// <see cref="TypeResolver.MemberFilter"/> is evaluated once per <see cref="PropertyInfo"/>, so it
/// cannot expose a mutable property's getter while routing the setter through
/// <see cref="ScriptPropertyEditCommand"/> — if the filter lets a settable property through at all, JS
/// gets the raw CLR setter too, bypassing the edit session. A Proxy's <c>get</c>/<c>set</c> traps don't
/// have that limitation: they see every access individually, so writes can be routed through
/// <see cref="ScriptEntityHandle.Set"/> while reads stay direct. See ScriptingPlan.md's Phase 9 notes.
///
/// Installed globally via <c>Options.Interop.WrapObjectHandler</c> in <see cref="ScriptEngineHost"/>,
/// so every <see cref="ScriptEntityHandle"/> crossing the JS boundary — a module's return value, an
/// array element, anything — gets proxied the same way with no per-call-site wiring.
/// </summary>
internal sealed class EntityProxyHandler : ProxyHandler
{
    private readonly Engine _engine;
    private readonly ScriptEntityHandle _handle;

    public EntityProxyHandler(Engine engine, ScriptEntityHandle handle)
    {
        _engine = engine;
        _handle = handle;
    }

    public override JsValue Get(ObjectInstance target, JsValue property, JsValue receiver)
    {
        Entity entity = _handle.Resolve();
        string name = property.ToString();
        Type type = entity.GetType();

        PropertyInfo? scriptProperty = ScriptReflection.Properties(type).FirstOrDefault(p => p.Name == name);
        if (scriptProperty is not null)
        {
            return JsValue.FromObject(_engine, scriptProperty.GetValue(entity));
        }

        MethodInfo? scriptFunction = ScriptReflection.Functions(type).FirstOrDefault(m => m.Name == name);
        return scriptFunction is not null
            ? JsValue.FromObject(_engine, BindDelegate(entity, scriptFunction))
            : JsValue.Undefined;
    }

    public override bool? Set(ObjectInstance target, JsValue property, JsValue value, JsValue receiver)
    {
        _handle.Set(property.ToString(), value.ToObject());
        return true;
    }

    // Builds a delegate matching the method's exact signature so Jint's own argument/return
    // conversion (the same machinery [ScriptFunction] module methods already rely on) applies —
    // this class doesn't do its own JS<->CLR value marshalling for function calls.
    private static Delegate BindDelegate(Entity entity, MethodInfo method)
    {
        Type[] signature = method.GetParameters().Select(p => p.ParameterType).Append(method.ReturnType).ToArray();
        Type delegateType = Expression.GetDelegateType(signature);
        return method.CreateDelegate(delegateType, entity);
    }
}
