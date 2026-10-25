using System;
using System.Linq;
using nadena.dev.ndmf;
using UnityEngine;
using VRC.SDKBase.Editor.BuildPipeline;

namespace Micro.VRCLensMA.Editor
{
    // Menu Favorites reads the installed menus at SDK order -20000, before NDMF (-11000).
    // Prepare only our installer in that case; the regular NDMF pipeline still handles MA afterwards.
    // No vendor hook is invoked manually, so each vendor callback continues to run exactly once.
    public sealed class EarlyFavoritesCompatibility : IVRCSDKPreprocessAvatarCallback
    {
        public int callbackOrder => -21000;

        public bool OnPreprocessAvatar(GameObject avatarGameObject)
        {
            if (avatarGameObject.GetComponentsInChildren<VRCLensInstaller>(true).Length == 0) return true;
            var hasFavorites = avatarGameObject.GetComponentsInChildren<Component>(true)
                .Any(c => c != null && c.GetType().FullName == "VRCLensCustom.VRCLensMenuFavorites");
            if (!hasFavorites) return true;
            try
            {
                // Retain output assets in memory until the normal NDMF context serializes them.
                // No generated path or additional asset-cleanup lifecycle is necessary.
                InstallerBuild.Run(new BuildContext(avatarGameObject, null));
                return true;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return false;
            }
        }
    }
}
