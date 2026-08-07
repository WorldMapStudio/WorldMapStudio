using System.Numerics;
using ImGuiNET;

namespace WorldMapStudio;

/// <summary>
/// Shows how each storage's live schema differs from what its code expects, with editable SQL to
/// bring the database in line. Opens itself automatically when drift is found on startup; can also be
/// opened from the Window menu and re-checked on demand.
/// </summary>
[Subsystem(nameof(WindowManager))]
public sealed class MigrationWindow : Window
{
    private static readonly Vector4 Destructive = new(0.95f, 0.5f, 0.4f, 1.0f);

    private readonly MigrationSystem _migrations;

    public MigrationWindow(WindowManager manager)
        : base("Schema Migration", startOpen: false, defaultSize: new Vector2(560, 520))
    {
        _migrations = manager.Context.Migrations;
    }

    protected override void OnBeforeDraw()
    {
        if (_migrations.ConsumeOpenRequest())
        {
            IsOpen = true;
        }
    }

    protected override void DrawContent()
    {
        if (ImGui.Button("Check schema"))
        {
            _migrations.Check();
        }

        ImGui.Separator();

        if (!_migrations.HasPending)
        {
            ImGui.TextDisabled("All storage schemas are up to date.");
            return;
        }

        foreach (StorageMigration migration in _migrations.Migrations)
        {
            if (!migration.HasChanges)
            {
                continue;
            }

            DrawMigration(migration);
        }
    }

    private void DrawMigration(StorageMigration migration)
    {
        if (!ImGui.CollapsingHeader($"{migration.Storage.Name} ({migration.Changes.Count} change(s))", ImGuiTreeNodeFlags.DefaultOpen))
        {
            return;
        }

        ImGui.PushID(migration.Storage.Name);

        foreach (SchemaChange change in migration.Changes)
        {
            string line = $"{change.Kind} {change.Table}{Detail(change)}";
            if (change.IsDestructive)
            {
                ImGui.TextColored(Destructive, line);
            }
            else
            {
                ImGui.TextUnformatted(line);
            }
        }

        ImGui.Spacing();
        ImGui.TextDisabled("SQL (editable):");
        ImGui.InputTextMultiline("##sql", ref migration.Sql, 8192, new Vector2(-1, 180));

        if (ImGui.Button("Apply"))
        {
            _migrations.Apply(migration);
        }

        if (migration.Error != null)
        {
            ImGui.TextColored(Destructive, migration.Error);
        }

        ImGui.Separator();
        ImGui.PopID();
    }

    private static string Detail(SchemaChange change)
    {
        if (change.Column != null)
        {
            return $".{change.Column.Name}";
        }

        if (change.Index != null)
        {
            return $" [{change.Index.Name}]";
        }

        return string.Empty;
    }
}
