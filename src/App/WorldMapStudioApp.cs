using WorldMapStudio;
using Godot;
using ImGuiNET;

public partial class WorldMapStudioApp : Node3D
{
	public override void _Ready()
	{
		AddChild(new GodotImGui());
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
		});

		if (shouldQuit)
		{
			GetTree().Quit();
		}
	}
}
