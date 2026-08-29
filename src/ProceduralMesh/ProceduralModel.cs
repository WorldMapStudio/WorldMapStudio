using System;

namespace WorldMapStudio;

/// <summary>
/// A named, saved procedural mesh — function, parameters, format, material bindings, and the
/// authored <see cref="VertexNetwork"/>. Catalog-backed like <see cref="MeshMaterialPreset"/>, so a
/// <see cref="ProceduralMeshComponent"/> merely references one by id instead of owning the data: many
/// placements can share one model, and editing it from any of them updates every placement.
/// </summary>
public sealed class ProceduralModel : CatalogEntity, IKeyedCatalogEntity
{
    private string _name = "Model";
    private string _functionId = "builtin.mesh.tube_network";
    private string _parameters = "";
    private string _formatId = "";
    private string _materials = "";
    private VertexNetwork _network = new();

    public string Name
    {
        get => _name;
        set
        {
            if (_name == value)
            {
                return;
            }

            _name = value;
            Revision++;
        }
    }

    public string FunctionId
    {
        get => _functionId;
        set
        {
            if (_functionId == value)
            {
                return;
            }

            _functionId = value;
            Revision++;
        }
    }

    public string Parameters
    {
        get => _parameters;
        set
        {
            if (_parameters == value)
            {
                return;
            }

            _parameters = value;
            Revision++;
        }
    }

    /// <summary>
    /// Which <see cref="IModelFormat"/> this model authors, e.g. "wow.format.wmo" for a plugin-defined
    /// format. Empty defers to the bound function's first supported format, or the plain authorable
    /// mesh format if the function does not care.
    /// </summary>
    public string FormatId
    {
        get => _formatId;
        set
        {
            if (_formatId == value)
            {
                return;
            }

            _formatId = value;
            Revision++;
        }
    }

    /// <summary>Serialized <see cref="ProceduralMeshMaterialBindings"/> for the bound function's material slots.</summary>
    public string Materials
    {
        get => _materials;
        set
        {
            if (_materials == value)
            {
                return;
            }

            _materials = value;
            Revision++;
        }
    }

    public VertexNetwork Network => _network;

    /// <summary>Replaces the network wholesale, cloning it so the caller's copy stays independent.</summary>
    public void ReplaceNetwork(VertexNetwork network)
    {
        _network = network.Clone();
        Revision++;
    }

    /// <summary>
    /// Bumped by every authored-field setter and by <see cref="ReplaceNetwork"/>. What
    /// <see cref="ProceduralMeshComponent"/> compares against to notice the model changed under it —
    /// see <see cref="ProceduralMeshSystem.Update"/> — and what <see cref="ContentVersion"/> and
    /// <see cref="NetworkFingerprint"/> cache against instead of recomputing on every read.
    /// </summary>
    public int Revision { get; private set; }

    /// <inheritdoc />
    public int? RecordId { get; set; }

    /// <inheritdoc />
    public bool IsSaved { get; set; }

    public override string DisplayName => Name;

    private int _cachedForRevision = -1;
    private string _cachedFingerprint = "";
    private int _cachedContentVersion;

    /// <summary>Cached <see cref="VertexNetwork.Fingerprint"/> of <see cref="Network"/>, recomputed
    /// only when <see cref="Revision"/> has moved — the underlying hash is a JSON serialize plus
    /// SHA256, too costly to pay on every read.</summary>
    public string NetworkFingerprint
    {
        get
        {
            RefreshCacheIfStale();
            return _cachedFingerprint;
        }
    }

    /// <summary>Cheap content hash of every authored field plus <see cref="NetworkFingerprint"/>, cached against <see cref="Revision"/>.</summary>
    public int ContentVersion
    {
        get
        {
            RefreshCacheIfStale();
            return _cachedContentVersion;
        }
    }

    private void RefreshCacheIfStale()
    {
        if (_cachedForRevision == Revision)
        {
            return;
        }

        _cachedFingerprint = _network.Fingerprint();
        _cachedContentVersion = HashCode.Combine(FunctionId, Parameters, FormatId, Materials, _cachedFingerprint);
        _cachedForRevision = Revision;
    }
}
