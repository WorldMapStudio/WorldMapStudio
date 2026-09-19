using System.Threading.Tasks;
using Godot;

namespace WorldMapStudio;

public static class StyleFontsScriptApiTests
{
    [EditorTest(Category = "Style Script", Thread = TestThread.Background)]
    public static async Task System_fonts_resolves_on_the_first_call_and_on_a_cached_one()
    {
        var context = new EditorContext(new Node3D(), new Project { Name = "__wms_style_fonts_script_test__" });
        var host = new ScriptEngineHost([context.Scripting.StyleScriptApi]);

        string first = await Run(host, "(async function () { return (await wms.style.SystemFonts()).length.toString(); })()");
        if (first == "0")
        {
            Assert.Skip("this machine reports no system fonts");
        }

        string cached = await Run(host, "(async function () { return (await wms.style.SystemFonts()).length.toString(); })()");
        string narrowed = await Run(host, "(async function () { return (await wms.style.SystemFonts('zzzz-no-such-family')).length.toString(); })()");

        Assert.AreEqual(first, cached);
        Assert.AreEqual("0", narrowed);
    }

    private static async Task<string> Run(ScriptEngineHost host, string code)
    {
        Task<ScriptResult> pending = host.EvaluateAsync(code);
        for (int i = 0; i < 400 && !pending.IsCompleted; i++)
        {
            host.Update();
            await Task.Delay(25);
        }

        Assert.IsTrue(pending.IsCompleted, "the script's promise should settle");
        ScriptResult result = await pending;
        Assert.IsTrue(result.Success, result.Output);
        return result.Output;
    }
}
