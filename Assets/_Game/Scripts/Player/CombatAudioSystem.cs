using System;
using UnityEngine;

namespace FengChenInn
{
    // A bounded shared pool: eight enemies cannot create eight unlimited one-shot stacks.
    public sealed class CombatAudioSystem : MonoBehaviour
    {
        public static CombatAudioSystem Instance { get; private set; }
        public int SwingCount { get; private set; }
        public int HitCount { get; private set; }
        public int BlockCount { get; private set; }
        public int DodgeCount { get; private set; }
        public int DropCount { get; private set; }
        public float VolumeBudget => .8f;
        public event Action<string,AudioClip> SoundPlayed;
        readonly AudioSource[] voices = new AudioSource[8];
        readonly float[] gains = new float[8];
        readonly int[] priorities = new int[8];
        readonly double[] started = new double[8];
        AudioClip light,heavy,hit,block,dodge;
        AudioClip[] combo,drops;
        int lastDrop=-1;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatic() => Instance=null;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot() { if(!Instance)new GameObject("Combat audio system").AddComponent<CombatAudioSystem>(); }
        void Awake()
        {
            if(Instance&&Instance!=this){enabled=false;Destroy(gameObject);return;}
            Instance=this;DontDestroyOnLoad(gameObject);
            light=Resources.Load<AudioClip>("CombatAudio/LightSwing/LightSwing");
            heavy=Resources.Load<AudioClip>("CombatAudio/HeavySwing/HeavySwing");
            hit=Resources.Load<AudioClip>("CombatAudio/Hit/Hit");block=Resources.Load<AudioClip>("CombatAudio/Block/Block");
            dodge=Resources.Load<AudioClip>("CombatAudio/Dodge/Dodge");
            combo=Resources.LoadAll<AudioClip>("CombatAudio/Combo");drops=Resources.LoadAll<AudioClip>("CombatAudio/Drop");
            Array.Sort(combo,(a,b)=>string.CompareOrdinal(a.name,b.name));
            for(int i=0;i<voices.Length;i++) {
                var child=new GameObject("Combat voice "+i);child.transform.SetParent(transform);
                var s=child.AddComponent<AudioSource>();voices[i]=s;
                s.playOnAwake=false;s.loop=false;s.spatialBlend=1;s.dopplerLevel=0;
                s.rolloffMode=AudioRolloffMode.Linear;s.minDistance=3;s.maxDistance=22;s.priority=80;
            }
            if(!light||!heavy||!hit||!block||!dodge||combo.Length==0||drops.Length==0)Debug.LogError("Missing combat audio resources",this);
        }
        void Update()
        {
            foreach(var actor in CombatActor.Actors)
                if(actor&&!actor.GetComponent<CombatAudioEmitter>())actor.gameObject.AddComponent<CombatAudioEmitter>();
            if(Time.timeScale<=0){foreach(var voice in voices)if(voice)voice.Stop();return;}
            Balance();
        }
        void Balance()
        {
            if(voices[0]==null)return;
            float sum=0;for(int i=0;i<voices.Length;i++)if(voices[i]&&voices[i].isPlaying)sum+=gains[i];
            float factor=sum>VolumeBudget?VolumeBudget/sum:1;
            for(int i=0;i<voices.Length;i++)if(voices[i]&&voices[i].isPlaying)voices[i].volume=gains[i]*factor;
        }
        bool Play(string kind,AudioClip clip,Vector3 position,float gain,int priority,float pitch=1)
        {
            if(!clip||Time.timeScale<=0||voices[0]==null)return false;
            int slot=-1;for(int i=0;i<voices.Length;i++)if(!voices[i].isPlaying){slot=i;break;}
            if(slot<0) {
                for(int i=0;i<voices.Length;i++)if(priorities[i]<=priority&&(slot<0||priorities[i]<priorities[slot]||priorities[i]==priorities[slot]&&started[i]<started[slot]))slot=i;
                if(slot<0)return false;
            }
            var source=voices[slot];source.Stop();source.transform.position=position;source.clip=clip;source.pitch=pitch;
            gains[slot]=gain;priorities[slot]=priority;started[slot]=AudioSettings.dspTime;
            source.volume=0;source.Play();Balance();SoundPlayed?.Invoke(kind,clip);return true;
        }
        public void Swing(CombatActor actor,CombatAttack attack,int strike)
        {
            AudioClip clip=attack.motion==CombatMotion.Light?light:heavy;
            if(strike>0&&combo.Length>0)clip=combo[(strike-1)%combo.Length];
            if(attack.motion==CombatMotion.Skill&&combo.Length>0)clip=combo[UnityEngine.Random.Range(0,combo.Length)];
            if(Play("Swing",clip,actor.transform.position+Vector3.up,.32f,1,UnityEngine.Random.Range(.97f,1.03f)))SwingCount++;
        }
        public void Hit(Vector3 at){if(Play("Hit",hit,at,.36f,3))HitCount++;}
        public void Block(Vector3 at){if(Play("Block",block,at,.34f,3))BlockCount++;}
        public void Dodge(Vector3 at){if(Play("Dodge",dodge,at,.32f,2))DodgeCount++;}
        public void Drop(Vector3 at)
        {
            if(drops.Length==0)return;int index=UnityEngine.Random.Range(0,drops.Length);
            if(drops.Length>1&&index==lastDrop)index=(index+1)%drops.Length;
            if(Play("Drop",drops[index],at,.32f,2)){lastDrop=index;DropCount++;}
        }
        void OnDestroy(){if(Instance==this)Instance=null;}
    }
}
