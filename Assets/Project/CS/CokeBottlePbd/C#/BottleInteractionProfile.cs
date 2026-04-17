using UnityEngine;

[CreateAssetMenu(fileName = "BottleInteractionProfile", menuName = "CokeBottlePbd/Bottle Interaction Profile")]
public sealed class BottleInteractionProfile : ScriptableObject
{
    [Tooltip("角色静止或低速接触时的基础推挤强度。")]
    [SerializeField] [Min(0.0f)] private float _characterPushBase = 0.12f;
    [Tooltip("角色速度转成额外推挤的比例。")]
    [SerializeField] [Min(0.0f)] private float _characterPushFromSpeed = 0.18f;
    [Tooltip("角色速度低于该阈值时，不再向瓶堆注入主动推力，只保留静态阻挡。")]
    [SerializeField] [Min(0.0f)] private float _characterPushActivationSpeed = 0.08f;
    [Tooltip("角色沿碰撞法线推进后，切向速度被抑制的比例。")]
    [SerializeField] [Min(0.0f)] private float _characterTangentialDamping = 0.08f;
    [Tooltip("找不到真实角色代理时，备用 BoxCollider 的中心。")]
    [SerializeField] private Vector3 _fallbackCharacterBoxCenter = new Vector3(0.0f, 0.5f, 0.0f);
    [Tooltip("找不到真实角色代理时，备用 BoxCollider 的尺寸。")]
    [SerializeField] private Vector3 _fallbackCharacterBoxSize = Vector3.one;

    public float CharacterPushBase => _characterPushBase;
    public float CharacterPushFromSpeed => _characterPushFromSpeed;
    public float CharacterPushActivationSpeed => _characterPushActivationSpeed;
    public float CharacterTangentialDamping => _characterTangentialDamping;
    public Vector3 FallbackCharacterBoxCenter => _fallbackCharacterBoxCenter;
    public Vector3 FallbackCharacterBoxSize => _fallbackCharacterBoxSize;

    private void OnValidate()
    {
        _characterPushBase = Mathf.Max(0.0f, _characterPushBase);
        _characterPushFromSpeed = Mathf.Max(0.0f, _characterPushFromSpeed);
        _characterPushActivationSpeed = Mathf.Max(0.0f, _characterPushActivationSpeed);
        _characterTangentialDamping = Mathf.Max(0.0f, _characterTangentialDamping);
        _fallbackCharacterBoxSize.x = Mathf.Max(0.1f, _fallbackCharacterBoxSize.x);
        _fallbackCharacterBoxSize.y = Mathf.Max(0.1f, _fallbackCharacterBoxSize.y);
        _fallbackCharacterBoxSize.z = Mathf.Max(0.1f, _fallbackCharacterBoxSize.z);
        _fallbackCharacterBoxCenter.y = Mathf.Max(0.0f, _fallbackCharacterBoxCenter.y);
    }
}
