using System.Collections.Generic;
using WorldMapStudio;
using Godot;
using ImGuiNET;

public partial class WorldMapStudioApp : Node3D
{
	private readonly List<ImGuiWindow> _windows = [];

	public override void _Ready()
	{
		AddChild(new GodotImGui());

		_windows.Add(new PerformanceWindow());
		_windows.Add(new ComputeMaterialWindow());
		_windows.Add(new ViewportWindow(this));
		_windows.Add(new WorkQueueWindow());
		_windows.Add(new WorkTestWindow());
	}

	public override void _Process(double delta)
	{
		WorkQueue.PumpMainThread();

		bool shouldQuit = false;
		ImGuiEx.MainMenuBar(() =>
		{
			ImGuiEx.Menu("File", () =>
			{
				if (ImGui.MenuItem("Exit"))
				{
					shouldQuit = true;
				}
			});

			ImGuiEx.Menu("Window", () =>
			{
				foreach (ImGuiWindow window in _windows)
				{
					window.DrawMenuItem();
				}
			});
		});

		ImGui.DockSpaceOverViewport();

		foreach (ImGuiWindow window in _windows)
		{
			window.Draw();
		}

		if (shouldQuit)
		{
			GetTree().Quit();
		}
	}

	public override void _ExitTree()
	{
		WorkQueue.Shutdown();
	}
}
