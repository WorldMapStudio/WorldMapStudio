using System;
using System.Threading;
using ImGuiNET;
using NVector2 = System.Numerics.Vector2;

namespace WorldMapStudio;

/// <summary>
/// Spawns meaningless work of various shapes so the <see cref="WorkQueueWindow"/> can be exercised:
/// background-only, main-only, thread-hopping, long/cancellable, faulting, bursts, and main-thread
/// hogs that stress the frame budget.
/// </summary>
public sealed class WorkTestWindow : ImGuiWindow
{
    private static readonly Random _rng = new();

    private int _burstCount = 20;
    private int _stepCount = 8;
    private int _stepDelayMs = 150;

    public WorkTestWindow() : base("Work Queue Tester", startOpen: false, defaultSize: new NVector2(420.0f, 460.0f))
    {
    }

    protected override void DrawContent()
    {
        ImGui.TextWrapped("Each button schedules meaningless work. Watch it in the Work Queue window.");
        ImGui.Separator();

        if (ImGui.Button("Background only", ButtonSize))
        {
            ScheduleBackgroundOnly();
        }
        Help("Runs entirely on a worker thread with a few named steps.");

        if (ImGui.Button("Main-thread only", ButtonSize))
        {
            ScheduleMainOnly();
        }
        Help("Runs on the main thread, yielding between steps so the frame budget can breathe.");

        if (ImGui.Button("Bounce background <-> main", ButtonSize))
        {
            ScheduleBouncing();
        }
        Help("Hops back and forth between a worker and the main thread each step.");

        ImGui.Separator();

        if (ImGui.Button("Long cancellable job", ButtonSize))
        {
            ScheduleLongCancellable();
        }
        Help("Many steps with cancellation checks. Use the Cancel button in the Work Queue window.");

        if (ImGui.Button("Faulting job", ButtonSize))
        {
            ScheduleFaulting();
        }
        Help("Throws partway through so you can see the Faulted state and error text.");

        ImGui.Separator();

        ImGui.PushItemWidth(160.0f);
        ImGui.SliderInt("Burst count", ref _burstCount, 1, 200);
        ImGui.PopItemWidth();
        if (ImGui.Button("Spawn burst", ButtonSize))
        {
            ScheduleBurst(_burstCount);
        }
        Help("Fires many short background items at once to fill the queue.");

        if (ImGui.Button("Main-thread hog", ButtonSize))
        {
            ScheduleMainHog();
        }
        Help("Busy-waits on the main thread WITHOUT yielding, to show budget limits (expect a hitch).");

        ImGui.Separator();

        ImGui.Text("Custom parameterised job");
        ImGui.PushItemWidth(160.0f);
        ImGui.SliderInt("Steps", ref _stepCount, 1, 40);
        ImGui.SliderInt("Step delay (ms)", ref _stepDelayMs, 0, 1000);
        ImGui.PopItemWidth();
        if (ImGui.Button("Spawn custom", ButtonSize))
        {
            ScheduleCustom(_stepCount, _stepDelayMs);
        }
    }

    private static NVector2 ButtonSize => new(-1.0f, 0.0f);

    private static void Help(string text)
    {
        ImGui.SameLine();
        ImGui.TextDisabled("(?)");
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(text);
        }
    }

    private static void ScheduleBackgroundOnly()
    {
        WorkQueue.Schedule($"Background Task {Tag()}", async ctx =>
        {
            string[] steps = { "Parsing", "Transforming", "Compressing", "Writing" };
            foreach (string step in steps)
            {
                ctx.Step(step);
                Thread.Sleep(_rng.Next(120, 300));
                await ctx.Yield();
            }
        });
    }

    private static void ScheduleMainOnly()
    {
        WorkQueue.Schedule($"Main Task {Tag()}", async ctx =>
        {
            for (int i = 0; i < 5; i++)
            {
                ctx.Step($"Building UI batch {i + 1}/5");
                Thread.Sleep(30);
                await ctx.Yield();
            }
        }, WorkThread.Main);
    }

    private static void ScheduleBouncing()
    {
        WorkQueue.Schedule($"Bouncing Task {Tag()}", async ctx =>
        {
            for (int i = 0; i < 4; i++)
            {
                await ctx.SwitchToBackground();
                ctx.Step($"Loading part {i + 1} (worker)");
                Thread.Sleep(_rng.Next(100, 250));

                await ctx.SwitchToMain();
                ctx.Step($"Uploading part {i + 1} (main)");
                Thread.Sleep(10);
            }
        });
    }

    private static void ScheduleLongCancellable()
    {
        WorkQueue.Schedule($"Long Job {Tag()}", async ctx =>
        {
            const int total = 40;
            for (int i = 0; i < total; i++)
            {
                ctx.ThrowIfCancellationRequested();
                ctx.Step($"Processing {i + 1}/{total}");
                Thread.Sleep(250);
                await ctx.Yield();
            }
        });
    }

    private static void ScheduleFaulting()
    {
        WorkQueue.Schedule($"Faulting Job {Tag()}", async ctx =>
        {
            ctx.Step("Warming up");
            Thread.Sleep(300);
            await ctx.Yield();
            ctx.Step("About to fail");
            Thread.Sleep(300);
            throw new InvalidOperationException("Something went wrong on purpose.");
        });
    }

    private static void ScheduleBurst(int count)
    {
        for (int i = 0; i < count; i++)
        {
            int index = i;
            WorkQueue.Schedule($"Burst Item {index:D3}", ctx =>
            {
                ctx.Step("Crunching");
                Thread.Sleep(_rng.Next(200, 800));
            });
        }
    }

    private static void ScheduleMainHog()
    {
        WorkQueue.Schedule($"Main Hog {Tag()}", ctx =>
        {
            ctx.Step("Blocking the main thread");
            long end = Environment.TickCount64 + 250;
            while (Environment.TickCount64 < end)
            {
                // Intentionally busy-wait without yielding.
            }
        }, WorkThread.Main);
    }

    private static void ScheduleCustom(int steps, int delayMs)
    {
        WorkQueue.Schedule($"Custom Job {Tag()}", async ctx =>
        {
            for (int i = 0; i < steps; i++)
            {
                ctx.ThrowIfCancellationRequested();
                ctx.Step($"Step {i + 1}/{steps}");
                if (delayMs > 0)
                {
                    Thread.Sleep(delayMs);
                }
                await ctx.Yield();
            }
        });
    }

    private static string Tag() => (Environment.TickCount & 0xFFF).ToString("X3");
}
