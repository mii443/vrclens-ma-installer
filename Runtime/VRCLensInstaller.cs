using UnityEngine;
using VRC.SDKBase;

namespace Micro.VRCLensMA
{
    [DisallowMultipleComponent]
    [AddComponentMenu("VRCLens MA/VRCLens Installer")]
    public sealed class VRCLensInstaller : MonoBehaviour, IEditorOnly
    {
        [Tooltip("購入済みVRCLensから、このプロジェクト内で作成した設定オブジェクト。")]
        public GameObject setupObject;

        [Tooltip("この直下に置いたカスタマイズPrefabを、ビルド時にVRCLens直下へ移動します。")]
        public Transform customizations;
    }
}
