#nullable enable
using Godot;
using ImGuiNET;
using Vector2 = System.Numerics.Vector2;
using Vector4 = System.Numerics.Vector4;

namespace WorldMapStudio;

/// <summary>
/// Gate scene between <see cref="LoadingScreen"/> and <see cref="Editor"/>: shown whenever
/// <see cref="MigrationSystem.HasPending"/> is true after startup, so the user reviews and applies
/// schema changes before the editor (and any queries it runs) can see a stale or incompatible database.
/// </summary>
public sealed class Migration : IScene
{
    private static readonly Vector4 Destructive = new(0.95f, 0.5f, 0.4f, 1.0f);

    private readonly Node3D _root;
    private readonly EditorContext _context;

    public Migration(Node3D root, EditorContext context)
    {
        _root = root;
        _context = context;
    }

    public void Start()
    {
    }

    public IScene? Update()
    {
        IScene? scene = this;
        MigrationSystem migrations = _context.Migrations;

        ImGuiEx.FullScreen("Migration", ImGuiWindowFlags.None, () =>
        {
            ImGui.Text("Database schema is out of date");
            ImGui.TextDisabled("Review the changes below and apply them before opening the editor.");
            ImGui.Separator();

            if (ImGui.Button("Check schema"))
            {
                migrations.Check();
            }

            ImGui.Separator();

            ImGuiEx.Child("MigrationList", new Vector2(0, -40), true, ImGuiWindowFlags.None, () =>
            {
                if (!migrations.HasPending)
                {
                    ImGui.TextDisabled("All storage schemas are up to date.");
                    return;
                }

                foreach (StorageMigration migration in migrations.Migrations)
                {
                    if (!migration.HasChanges)
                    {
                        continue;
                    }

                    DrawMigration(migrations, migration);
                }
            });

            bool hasPending = migrations.HasPending;
            if (hasPending)
            {
                ImGui.BeginDisabled();
            }

            if (ImGui.Button("Continue", new Vector2(150, 30)))
            {
                scene = new Editor(_context);
            }

            if (hasPending)
            {
                ImGui.EndDisabled();
            }

            ImGui.SameLine();

            if (ImGui.Button("Back", new Vector2(150, 30)))
            {
                scene = new ProjectSelect(_root);
            }
        });

        return scene;
    }

    private static void DrawMigration(MigrationSystem migrations, StorageMigration migration)
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
            migrations.Apply(migration);
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
