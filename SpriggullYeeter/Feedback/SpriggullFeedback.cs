using System;
using Alta.Global;
using Alta.Impact;
using UnityEngine;

namespace SpriggullYeeter.Feedback
{
    /// <summary>
    /// Plays native ATT Spriggull squawks, launch feather/hit bursts, and impact audio safely on non-headless builds.
    /// </summary>
    internal static class SpriggullFeedback
    {
        public static void PlaySquawk(GameObject spriggullRoot, bool isLaunch)
        {
            if (ApplicationManager.IsHeadless || spriggullRoot == null)
                return;

            try
            {
                AudioManager audio = AudioManager.Instance;
                if (audio != null)
                {
                    SoundEffect primary = isLaunch ? SoundEffect.SpriggullAlert : SoundEffect.SpriggullFlail;
                    SoundEffectSet set = audio.Resolve(null, primary, spriggullRoot);
                    if (set == null && isLaunch)
                        set = audio.Resolve(null, SoundEffect.SpriggullFlail, spriggullRoot);

                    if (set != null)
                    {
                        audio.PlaySoundEffectOnObject(set, spriggullRoot, spriggullRoot, Array.Empty<ISoundProperty>());
                        return;
                    }
                }

                SoundSource soundSource = spriggullRoot.GetComponentInChildren<SoundSource>();
                if (soundSource != null)
                    soundSource.Play();
            }
            catch
            {
                // Never disrupt gameplay if Wwise/audio subsystem is uninitialized.
            }
        }

        public static void PlayLaunchFeatherBurst(GameObject spriggullRoot, Vector3 launchPoint, Vector3 launchDir)
        {
            if (ApplicationManager.IsHeadless || spriggullRoot == null)
                return;

            try
            {
                ImpactDetector detector = spriggullRoot.GetComponentInChildren<ImpactDetector>();
                if (detector == null || detector.Settings == null)
                    return;

                Vector3 safeDir = launchDir.sqrMagnitude > 1e-4f ? launchDir.normalized : Vector3.up;
                Collider col = spriggullRoot.GetComponentInChildren<Collider>();

                var syntheticHit = new ImpactHit(
                    baseHealthDamage: 0f,
                    baseCrippleDamage: 0f,
                    multipliers: 1f,
                    effectiveness: 1.0f,
                    detector: detector,
                    impactType: ImpactType.Blunt,
                    point: launchPoint,
                    normal: -safeDir,
                    direction: safeDir,
                    struckCollider: col,
                    wielder: null,
                    playerWielder: null);

                detector.Settings.PlayEffects(syntheticHit, 1.0f);
            }
            catch
            {
                // Ignore if prefab has no ImpactDetectorSettings or pooled effect is unavailable.
            }
        }

        public static void PlayImpactSound(GameObject targetRoot, Vector3 worldPoint)
        {
            if (ApplicationManager.IsHeadless || targetRoot == null)
                return;

            try
            {
                AudioManager audio = AudioManager.Instance;
                if (audio == null)
                    return;

                SoundEffectSet set = audio.Resolve(null, SoundEffect.ObjectCollision, targetRoot);
                if (set != null)
                {
                    audio.PlaySoundEffectAt(set, worldPoint, targetRoot, Array.Empty<ISoundProperty>());
                }
            }
            catch
            {
                // Ignore audio errors.
            }
        }
    }
}
