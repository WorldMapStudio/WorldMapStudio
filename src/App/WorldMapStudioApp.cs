using WorldMapStudio;
using Godot;
using ImGuiNET;

public partial class WorldMapStudioApp : Node3D
{
	public override void _Ready()
	{
		var imgui = new GodotImGui();
		imgui.AddLayout(ImGui.ShowDemoWindow);
		AddChild(imgui);
	}

	public override void _Process(double delta)
	{
	}
}
