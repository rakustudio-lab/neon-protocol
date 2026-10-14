using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace NeonProtocol
{
    // The default heist has a deliberate combat beat. Three distinct defenders hold
    // the core access codes; repeat tags on one actor cannot satisfy the lock.
    public class PaintballEncounter : MonoBehaviour
    {
        public static PaintballEncounter Instance { get; private set; }
        public CompetitorAI[] guards;
        public Transform[] posts;
        public Relay iris;
        public CompetitorAI[] pursuitGuards;
        public Transform[] pursuitPosts;
        float pursuitDelay = -1f;
        readonly HashSet<CompetitorAI> cleared = new HashSet<CompetitorAI>();
        HeistDirector director;
        bool breached, response;
        struct Original { public Vector3 pos; public Quaternion rot; public int team, hits; public Transform post; public Transform[] patrol; public bool lesson; }
        Original[] original;
        public bool Required => !HeistDirector.FullRoute && guards != null && guards.Length > 0;
        public int Cleared => cleared.Count;
        public int Total => guards == null ? 0 : guards.Length;
        public bool Complete => !Required || Cleared >= Total;

        void Awake() { Instance = this; }
        void Start()
        {
            director = HeistDirector.Instance;
            if (director) director.PhaseChanged += Phase;
            original = new Original[guards.Length];
            for (int i=0;i<guards.Length;i++)
            {
                var g=guards[i]; if(!g) continue;
                original[i]=new Original { pos=g.transform.position, rot=g.transform.rotation, team=g.team, hits=g.GetComponent<TagHealth>().hitsToTag, post=g.dormantPost, patrol=g.patrol, lesson=g.lessonGuard };
            }
            foreach (var guard in guards)
            {
                if (!guard) continue;
                var g = guard;
                g.GetComponent<TagHealth>().Tagged += () => { if (Required) cleared.Add(g); };
            }
        }
        void OnDestroy() { if (director) director.PhaseChanged -= Phase; if (Instance == this) Instance = null; }
        void Phase(HeistDirector.Phase phase)
        {
            if (phase != HeistDirector.Phase.Infiltrate) return;
            cleared.Clear(); breached = response = false; pursuitDelay=-1f;
            if(pursuitGuards!=null) foreach(var g in pursuitGuards) if(g) g.gameObject.SetActive(false);
            if (!Required)
            {
                for(int i=0;i<guards.Length;i++) {
                    var g=guards[i]; if(!g) continue; var o=original[i];
                    g.encounterSuspended=g.stayDownForRun=false; g.team=o.team; g.lessonGuard=o.lesson;
                    g.dormantPost=o.post; g.patrol=o.patrol;
                    var health=g.GetComponent<TagHealth>(); health.team=o.team; health.hitsToTag=o.hits; health.Revive();
                    var agent=g.GetComponent<NavMeshAgent>(); if(agent && agent.isOnNavMesh) agent.Warp(o.pos); g.transform.rotation=o.rot; g.ForgetPlayer();
                }
                return;
            }
            for (int i = 0; i < guards.Length; i++)
            {
                if (!guards[i] || i >= posts.Length || !posts[i]) continue;
                guards[i].ResetEncounter(posts[i]);
            }
        }
        public void StartPursuit()
        {
            if(!Required || pursuitGuards==null) return;
            for(int i=0;i<pursuitGuards.Length;i++) {
                var g=pursuitGuards[i]; if(!g) continue;
                g.transform.SetPositionAndRotation(pursuitPosts[i].position,pursuitPosts[i].rotation);
                g.gameObject.SetActive(true);
            }
            pursuitDelay=.25f; // let newly activated agents initialise before Warp
        }
        void Update()
        {
            if(pursuitDelay>=0f) {
                pursuitDelay-=Time.deltaTime;
                if(pursuitDelay<0f) for(int i=0;i<pursuitGuards.Length;i++) {
                    var g=pursuitGuards[i]; if(!g) continue;
                    g.ResetEncounter(pursuitPosts[i]); g.encounterSuspended=false; g.Wake();
                }
            }
            if (!Required || !director || !director.player || director.Current != HeistDirector.Phase.Infiltrate) return;
            var player = director.player.transform.position;
            if (!breached && player.x < -250f && player.z > 9f)
            {
                breached = true; guards[0].encounterSuspended = false; guards[0].Wake();
                director.AnnounceText("CONTACT · USE THE REACTOR AS COVER");
            }
            if (iris && iris.Online && !response)
            {
                response = true;
                foreach (var g in guards) if (g) { g.encounterSuspended = false; g.Wake(); }
                director.AnnounceText("RESPONSE TEAM · CLEAR THE CORE ACCESS");
            }
        }
    }
}
