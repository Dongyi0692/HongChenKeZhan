using System.Collections.Generic;
using UnityEngine;

namespace FengChenInn
{
    // Fed by the actual hinge rotation, not interaction requests or a fixed timer.
    public sealed class InnDoorAudio : MonoBehaviour
    {
        static readonly HashSet<InnDoorAudio> doors = new();
        AudioSource hingeSound, closeSound;
        AudioClip opening, closing;
        float openness, desiredVolume;
        int direction;
        bool paused;
        public static float RainExposure
        {
            get { float value = 0; foreach (var door in doors) if (door) value = Mathf.Max(value, door.openness); return value; }
        }
        void Awake()
        {
            opening = Resources.Load<AudioClip>("DoorAudio/WoodOpenHinge");
            closing = Resources.Load<AudioClip>("DoorAudio/WoodCloseHinge");
            hingeSound = MakeSource("Wood hinge", true);
            closeSound = MakeSource("Wood door closure", false);
            closeSound.clip = Resources.Load<AudioClip>("DoorAudio/WoodCloseImpact");
            closeSound.volume = .65f;
            if (!opening || !closing || !closeSound.clip) Debug.LogError("Missing wood door audio segments.");
        }
        void OnEnable() { doors.Add(this); }
        void OnDisable()
        {
            doors.Remove(this);
            if (hingeSound) hingeSound.Stop();
            if (closeSound) closeSound.Stop();
            direction = 0; desiredVolume = 0; paused = false;
        }
        AudioSource MakeSource(string label, bool loop)
        {
            var child = new GameObject(label); child.transform.SetParent(transform, false);
            child.transform.localPosition = Vector3.up;
            var source = child.AddComponent<AudioSource>();
            source.playOnAwake = false; source.loop = loop; source.spatialBlend = 1;
            source.dopplerLevel = 0; source.minDistance = 3; source.maxDistance = 22;
            source.rolloffMode = AudioRolloffMode.Linear; source.volume = 0;
            return source;
        }
        // Call immediately AFTER updating the hinge(s). Angles are distances from closed.
        public void SyncMotion(float previousAngle, float currentAngle, float fullAngle)
        {
            openness = fullAngle > .001f ? Mathf.Clamp01(currentAngle / fullAngle) : 0;
            if (Time.deltaTime <= 0) return;
            float delta = currentAngle - previousAngle;
            int nextDirection = Mathf.Abs(delta) > .0001f ? (delta > 0 ? 1 : -1) : 0;
            if (nextDirection != 0 && nextDirection != direction)
            {
                hingeSound.Stop(); hingeSound.volume = 0;
                hingeSound.clip = nextDirection > 0 ? opening : closing;
                if (hingeSound.clip) hingeSound.Play();
            }
            direction = nextDirection;
            desiredVolume = direction == 0 ? 0 : .55f;
            if (previousAngle > .001f && currentAngle <= .001f && delta < 0)
            {
                desiredVolume = 0;
                // Exactly one impact at actual contact; no startup or repeated closed-frame hit.
                if (closeSound.clip) closeSound.Play();
            }
        }
        void LateUpdate()
        {
            bool shouldPause = Time.timeScale <= 0;
            if (shouldPause != paused)
            {
                paused = shouldPause;
                if (paused) { hingeSound.Pause(); closeSound.Pause(); }
                else { hingeSound.UnPause(); closeSound.UnPause(); }
            }
            if (paused) return;
            hingeSound.volume = Mathf.MoveTowards(hingeSound.volume, desiredVolume, Time.deltaTime * 7);
            if (desiredVolume == 0 && hingeSound.volume == 0) hingeSound.Stop();
        }
    }
}
