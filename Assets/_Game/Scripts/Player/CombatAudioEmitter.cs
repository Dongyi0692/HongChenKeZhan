using UnityEngine;

namespace FengChenInn
{
    [DisallowMultipleComponent,RequireComponent(typeof(CombatActor))]
    public sealed class CombatAudioEmitter : MonoBehaviour
    {
        CombatActor actor;PrototypeFighter fighter;CombatAttack attack;bool[] sounded;
        void Awake(){actor=GetComponent<CombatActor>();fighter=GetComponent<PrototypeFighter>();}
        void OnEnable()
        {
            actor.AttackStarted+=Started;actor.AttackPoseSampled+=Pose;
            actor.Damaged+=Damaged;actor.Blocked+=Blocked;
            if(fighter)fighter.ActionPerformed+=Action;
        }
        void OnDisable()
        {
            actor.AttackStarted-=Started;actor.AttackPoseSampled-=Pose;
            actor.Damaged-=Damaged;actor.Blocked-=Blocked;
            if(fighter)fighter.ActionPerformed-=Action;
            attack=null;sounded=null;
        }
        void Started(CombatAttack value)
        {
            attack=value;sounded=new bool[value.hitWindows!=null&&value.hitWindows.Length>0?value.hitWindows.Length:1];
        }
        void Pose(CombatMotion motion,float before,float now,Vector3 from,Vector3 to)
        {
            if(attack==null||actor.CurrentAttack!=attack||motion!=attack.motion||Time.timeScale<=0)return;
            for(int i=0;i<sounded.Length;i++) {
                var window=attack.hitWindows!=null&&attack.hitWindows.Length>0?attack.hitWindows[i]:null;
                float start=window!=null?window.start:attack.activeStart;
                if(window!=null&&window.end<window.start)continue;
                // Actual sampled playable clock, never a coroutine or an assumed hit timer.
                // The source's attack crest follows its lead-in by ~60 / 150 ms.
                float lead=i>0||motion==CombatMotion.Light||motion==CombatMotion.Skill?.06f:.15f;
                if(!sounded[i]&&now>=Mathf.Max(0,start-lead)) {
                    sounded[i]=true;CombatAudioSystem.Instance?.Swing(actor,attack,i);
                }
            }
        }
        void Damaged(CombatActor source,int amount)
        {
            if(source&&amount>0)CombatAudioSystem.Instance?.Hit(transform.position+Vector3.up);
        }
        void Blocked(CombatActor source){if(source)CombatAudioSystem.Instance?.Block(transform.position+Vector3.up);}
        void Action(CombatMotion motion){if(motion==CombatMotion.Dodge)CombatAudioSystem.Instance?.Dodge(transform.position+Vector3.up*.6f);}
    }
}
