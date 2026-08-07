#nullable enable
using WorldMapStudio;
using Godot;

/// <summary>
/// Application root node. Owns the ImGui layer and the shared work queue, and drives the active
/// <see cref="IScene"/>: it starts at the <see cref="MainMenu"/>, then each frame asks the current
/// scene what to run next (stay, transition, or quit).
/// </summary>
public partial class WorldMapStudioApp : Node3D
{
	private IScene _currentScene = null!;

	public override void _Ready()
	{
		AddChild(new GodotImGui());

		_currentScene = new MainMenu(this);
		_currentScene.Start();
	}

	public override void _Process(double delta)
	{
		WorkQueue.PumpMainThread();

		IScene? nextScene = _currentScene.Update();
		if (nextScene == null)
		{
			GetTree().Quit();
		}
		else if (nextScene != _currentScene)
		{
			_currentScene = nextScene;
			_currentScene.Start();
		}
	}

	public override void _ExitTree()
	{
		WorkQueue.Shutdown();
	}
}
