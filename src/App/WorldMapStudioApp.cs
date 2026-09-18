#nullable enable
using System;
using WorldMapStudio;
using Godot;

/// <summary>
/// Application root node. Owns the ImGui layer and the shared work queue, and drives the active
/// <see cref="IScene"/>: it starts at the <see cref="MainMenu"/> — or, when a project config is
/// passed on the command line, straight in that project (see <see cref="ProjectAutostart"/>) — then
/// each frame asks the current scene what to run next (stay, transition, or quit).
/// </summary>
public partial class WorldMapStudioApp : Node3D
{
	private IScene _currentScene = null!;

	public override void _Ready()
	{
		AddChild(new GodotImGui());

		_currentScene = ProjectAutostart.Resolve(this) ?? new MainMenu(this);
		_currentScene.Start();
	}

	public override void _Process(double delta)
	{
		// A quarter of the frame rather than a flat 4 ms. The budget exists so draining queued work
		// never stalls rendering, and that is a proportion of a frame, not an absolute: on a 250 ms
		// frame the flat figure hands the queue 1.6% of the time, and the landscape rebuild behind a
		// paint stroke then lands tens of seconds after the stroke that caused it.
		WorkQueue.PumpMainThread(Math.Clamp(delta * 250.0, 4.0, 40.0));

		IScene? nextScene = _currentScene.Update();
		if (nextScene == null)
		{
			GetTree().Quit(AppExit.Code);
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
