using UnityEngine;

namespace FengChenInn
{
    // Attach only to an actual detached weapon with Rigidbody + Collider.
    // Never infer a physical weapon drop merely from a character's defeat.
    [DisallowMultipleComponent,RequireComponent(typeof(Rigidbody))]
    public sealed class WeaponDropAudio : MonoBehaviour
    {
        bool played;
        public void Arm(){played=false;}
        void OnEnable(){played=false;}
        void OnCollisionEnter(Collision collision)
        {
            if(played||Time.timeScale<=0||collision.relativeVelocity.magnitude<.5f)return;
            for(int i=0;i<collision.contactCount;i++) {
                var contact=collision.GetContact(i);
                if(contact.normal.y<.4f)continue;
                played=true;CombatAudioSystem.Instance?.Drop(contact.point);break;
            }
        }
    }
}
