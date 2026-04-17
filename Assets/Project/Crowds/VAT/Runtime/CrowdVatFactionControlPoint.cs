using UnityEngine;

[System.Serializable]
public enum CrowdVatFactionControlPointKind
{
    Center = 0,
    RangeNorthWest = 1,
    RangeNorthEast = 2,
    RangeSouthEast = 3,
    RangeSouthWest = 4
}

[ExecuteAlways]
public sealed class CrowdVatFactionControlPoint : MonoBehaviour
{
    private static readonly Color CampAColor = new Color(0.92f, 0.35f, 0.26f, 0.95f);
    private static readonly Color CampBColor = new Color(0.18f, 0.66f, 0.95f, 0.95f);

    [SerializeField] private CrowdVatIndirectRenderer _owner;
    [SerializeField] private CrowdVatFaction _faction = CrowdVatFaction.CampA;
    [SerializeField] private CrowdVatFactionControlPointKind _kind = CrowdVatFactionControlPointKind.Center;

    public CrowdVatIndirectRenderer Owner => _owner;

    public CrowdVatFaction Faction => _faction;

    public CrowdVatFactionControlPointKind Kind => _kind;

    public void Configure(CrowdVatIndirectRenderer owner, CrowdVatFaction faction)
    {
        Configure(owner, faction, CrowdVatFactionControlPointKind.Center);
    }

    public void Configure(CrowdVatIndirectRenderer owner, CrowdVatFaction faction, CrowdVatFactionControlPointKind kind)
    {
        _owner = owner;
        _faction = faction;
        _kind = kind;
        transform.hasChanged = true;
    }

    private void OnEnable()
    {
        transform.hasChanged = true;
        NotifyOwner();
    }

    private void OnValidate()
    {
        transform.hasChanged = true;
        NotifyOwner();
    }

    private void Update()
    {
        if (!transform.hasChanged)
            return;

        transform.hasChanged = false;
        NotifyOwner();
    }

    private void OnDrawGizmos()
    {
        Color color = _faction == CrowdVatFaction.CampA ? CampAColor : CampBColor;
        Gizmos.color = color;
        if (_kind == CrowdVatFactionControlPointKind.Center)
        {
            Gizmos.DrawWireSphere(transform.position, 0.45f);
            Gizmos.DrawSphere(transform.position, 0.08f);
            return;
        }

        Gizmos.DrawWireCube(transform.position, Vector3.one * 0.42f);
        Gizmos.DrawCube(transform.position, Vector3.one * 0.12f);
    }

    private void NotifyOwner()
    {
        if (_owner == null)
            return;

        _owner.NotifyFactionControlPointChanged(_faction, _kind, transform);
    }
}
