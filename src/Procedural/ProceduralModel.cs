using System;

namespace WorldMapStudio;

/// <summary>
/// A named, saved procedural mesh — function, parameters, format, material bindings, and the
/// authored <see cref="VertexNetwork"/>. Catalog-backed like <see cref="MeshMaterialPreset"/>, so a
/// <see cref="ProceduralComponent"/> merely references one by id instead of owning the data: many
/// placements can share one model, and editing it from any of them updates every placement.
/// </summary>
public sealed class ProceduralModel : CatalogEntity, IKeyedCatalogEntity
{
    private string _name = "Model";
    private string _functionId = "builtin.mesh.tube_network";
    private string _parameters = "";
    private string _formats = "";
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
            Bump();
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
            Bump();
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
            Bump();
        }
    }

    /// <summary>
    /// Serialized <see cref="ProceduralFormats"/>: which <see cref="IModelFormat"/> each of the bound
    /// function's declared outputs authors, e.g. "wow.format.wmo" for a plugin-defined format. An
    /// output missing from this map defers to its slot's first supported format, or the plain
    /// authorable mesh format if the slot does not care.
    /// </summary>
    public string Formats
    {
        get => _formats;
        set
        {
            if (_formats == value)
            {
                return;
            }

            _formats = value;
            Bump();
        }
    }

    /// <summary>Serialized <see cref="ProceduralBindings"/> for the bound function's material slots.</summary>
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
            Bump();
        }
    }

    public VertexNetwork Network => _network;

    /// <summary>Replaces the network wholesale, cloning it so the caller's copy stays independent.</summary>
    public void ReplaceNetwork(VertexNetwork network)
    {
        _network = network.Clone();
        Bump();
    }

    /// <summary>Replaces the network wholesale, taking ownership without cloning. For load paths where
    /// the caller's copy is freshly parsed and not shared with anything else.</summary>
    public void LoadNetwork(VertexNetwork network)
    {
        _network = network;
        Bump();
    }

    /// <summary>
    /// Bumped by every authored-field setter and by <see cref="ReplaceNetwork"/>. What
    /// <see cref="ProceduralComponent"/> compares against to notice the model changed under it —
    /// see <see cref="ProceduralSystem.Update"/> — and what <see cref="ContentVersion"/> and
    /// <see cref="NetworkFingerprint"/> cache against instead of recomputing on every read.
    /// </summary>
    public int Revision { get; private set; }

    private void Bump()
    {
        Revision++;
        RevisionTick++;
    }

    /// <summary>Moves whenever any model's <see cref="Revision"/> does, so a per-frame check that
    /// nothing changed costs one comparison instead of a walk over every model.</summary>
    public static int RevisionTick { get; private set; }

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
        _cachedContentVersion = HashCode.Combine(FunctionId, Parameters, Formats, Materials, _cachedFingerprint);
        _cachedForRevision = Revision;
    }
}
