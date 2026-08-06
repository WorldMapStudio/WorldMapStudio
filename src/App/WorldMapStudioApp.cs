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
	}

	public override void _Process(double delta)
	{
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
}
