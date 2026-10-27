using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SnowBoarderLab
{
    public enum RunState { Menu, HowTo, LevelSelect, Options, Playing, Paused, LevelComplete, Won, Lost }
    public enum PickupKind { Snowflake, Boost, Shield, Finish }

    public struct LevelDef
    {
        public float Start, Finish;
        public float[] Trees, Rocks, Boosts, Shields, Checkpoints;
        public string Difficulty;
        public int Seconds;
    }

    public sealed class SnowBoarderGame : MonoBehaviour
    {
        public static SnowBoarderGame Instance;
        public const int LevelCount = 3;
        public const int MaxLives = 3;
        public RunState State { get; private set; }
        public int Score { get; private set; }
        public int Lives { get; private set; }
        public int Combo { get; private set; }
        public int Level { get; private set; } = 1;
        public int Unlocked { get; private set; } = 1;
        public float LevelStart { get; private set; } = 5f;
        public float RespawnX { get; private set; } = 5f;
        public float ShieldUntil, BoostUntil;
        public Rider Player;
        public Camera View;
        public float FinishX = 320;
        public bool Snow = true;
        public float Volume = .65f;
        public bool AutoTest;
        // Torso placement relative to the rider origin, in world units. The torso is drawn at
        // the same pixels-per-unit as the legs, so these offsets only slide it into place.
        // Tune in the Inspector or here; the main-menu art follows the same values (see DrawMenu).
        public float RiderTopOffsetX = -0.077f;
        public float RiderTopOffsetY = 0.43f;
        public float BoardHeight = .65f;

        const string UnlockKey = "SnowBoarder.Unlocked";
        const float BoostSeconds = 5f, ShieldSeconds = 4f;

        // ---------- ride feel ----------
        // Cruise is the always-on base speed, so the rider never has to hold a key to keep
        // moving. Boost is a clear step above it AND pulls harder to get there, so a speed-up
        // pickup always reads as a real burst rather than a slower version of normal riding.
        // 26 (not more) is deliberate: above ~27 the rider flies the ~60 units between two
        // ramps and lands on the next crest instead of in the flat gap before it.
        public const float CruiseSpeed = 18f;
        public const float BoostSpeed = 26f;
        // Drive force per m/s of speed error, applied on top of the constant push below.
        // BoostAccel is more than double CruiseAccel: that is what makes speed up accelerate
        // faster than normal riding. At full throttle the drive is DriveForce+ThrottleForce+
        // CruiseAccel = 17.5 N, comfortably above the 12.5 N needed to crawl up the steepest
        // ramp on the course (36 deg at x=598), so no ramp can stall the rider.
        public const float CruiseAccel = 10f;
        public const float BoostAccel = 22f;
        public const float DriveForce = 3.5f;
        public const float ThrottleForce = 4f;
        public const float BoostForce = 5f;
        public const float AccelBand = 4f;

        // Airborne spin. The torque ramps the spin in exactly as before, but the rate is
        // capped at one turn per second so a long boost jump can never wind the board up to
        // the ~800 deg/s the un-capped version reached. A cap (not more damping) is the right
        // lever: angular damping is a linear brake that only sets an equilibrium, and raising
        // it enough to help would also make the board sluggish on the ground.
        public const float AirSpinTorque = 4.5f;
        public const float MaxAirSpin = 360f;

        static readonly float[] Ramps = { 40f, 110f, 180f, 250f, 320f, 390f, 460f, 530f, 600f, 660f };
        const float CourseMinX = -20f, CourseMaxX = 690f;
        // Trees and rocks keep at least this much room from a ramp centre. The bump itself is
        // flat past ~8 units, so 15 guarantees the obstacle sits on ordinary slope instead of
        // the crest a rider has to power up.
        const float RampClearance = 15f;

        // Obstacles sit in the flat gaps BETWEEN the ramps, never on a crest or its climb, so
        // a hit can never leave the rider stranded halfway up a slope.
        static readonly LevelDef[] Levels =
        {
            new LevelDef{ Start=5f, Finish=320f, Difficulty="EASY", Seconds=30,
                Trees=new[]{62f,152f,268f}, Rocks=new[]{88f,205f,300f},
                Boosts=new[]{40f,230f}, Shields=new[]{180f}, Checkpoints=new[]{170f} },
            new LevelDef{ Start=5f, Finish=480f, Difficulty="NORMAL", Seconds=45,
                Trees=new[]{58f,130f,205f,270f,340f,408f}, Rocks=new[]{85f,158f,235f,300f,375f},
                Boosts=new[]{40f,240f,400f}, Shields=new[]{170f,340f}, Checkpoints=new[]{120f,300f} },
            new LevelDef{ Start=5f, Finish=640f, Difficulty="HARD", Seconds=60,
                Trees=new[]{58f,128f,198f,268f,338f,408f,478f,545f,615f}, Rocks=new[]{88f,158f,228f,298f,368f,438f,505f,575f},
                Boosts=new[]{40f,250f,460f}, Shields=new[]{160f,360f,540f}, Checkpoints=new[]{150f,320f,500f} },
        };

        Transform world;
        Transform[] levelRoots;
        List<Checkpoint> levelCheckpoints = new List<Checkpoint>();
        AudioSource audioSource;
        float comboUntil, scoreFraction, elapsed, toastUntil;
        string toast = "";
        int best;
        Material spriteMaterial;
        GUIStyle title, heading, body, small, button, scoreStyle, subtitle, good, danger, button2, buttonLocked;
        GUIStyle centerTitle, centerSub, centerHeading, menuTitleSmall, cardStyle;
        Texture2D accent, cardTex, cardTexSolid, dim, barTrack;
        Texture2D heartFull, heartEmpty, clockIcon, gaugeIcon;
        Texture2D roundedPrimary, roundedPrimaryHover, roundedPrimaryActive, roundedGhost, roundedGhostHover, roundedLocked;
        Texture2D[] wipeTex;
        Sprite checkpointSprite;
        ParticleSystem snowfall;
        bool stylesReady;
        bool returnToPause;
        Texture2D menuShade, menuShadow;
        GUIStyle logoSnow, logoBoarder, menuPlay, menuLink;

        const int WipeSteps = 48;

        // Menu buttons share one size, one left edge and one gap so the stack reads as a
        // single list instead of four ad-hoc rects.
        const float MenuButtonX = 101f, MenuButtonWidth = 300f, MenuButtonHeight = 50f, MenuButtonGap = 16f;
        Rect MenuButtonRect(float y) => new Rect(MenuButtonX, y, MenuButtonWidth, MenuButtonHeight);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (FindAnyObjectByType<SnowBoarderGame>() == null)
                new GameObject("Snow Boarder Game").AddComponent<SnowBoarderGame>();
        }
        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            Time.timeScale = 1;
            best = PlayerPrefs.GetInt("SnowBoarder.Best", 0);
            Volume = PlayerPrefs.GetFloat("SnowBoarder.Volume", .65f);
            Snow = PlayerPrefs.GetInt("SnowBoarder.Snow", 1) == 1;
            Unlocked = Mathf.Clamp(PlayerPrefs.GetInt(UnlockKey, 1), 1, LevelCount);
            spriteMaterial = new Material(Resources.Load<Material>("SnowSprite"));
            checkpointSprite = MakeCheckpointSprite();
            foreach (var c in FindObjectsByType<Camera>()) c.gameObject.SetActive(false);
            View = new GameObject("Follow Camera").AddComponent<Camera>();
            View.tag = "MainCamera"; View.orthographic = true; View.orthographicSize = 9;
            View.backgroundColor = new Color(.57f,.77f,.88f);
            View.transform.position = new Vector3(10, Surface(5)+5, -10);
            View.gameObject.AddComponent<AudioListener>();
            audioSource = gameObject.AddComponent<AudioSource>();
            FinishX = Levels[0].Finish; LevelStart = Levels[0].Start; RespawnX = LevelStart;
            BuildWorld(); State = RunState.Menu;
        }
        public static float Surface(float x)
        {
            float y = 18 - .20f*x + 2.2f*Mathf.Sin(x*.075f) + .65f*Mathf.Sin(x*.19f);
            foreach (float ramp in Ramps)
                y += 2.8f*Mathf.Exp(-Mathf.Pow((x-ramp)/3.4f,2));
            return y;
        }
        public static float Slope(float x) => Mathf.Atan2(Surface(x+.2f)-Surface(x-.2f), .4f)*Mathf.Rad2Deg;
        // Safety net for hand-edited level data: pushes an obstacle that someone dropped onto a
        // ramp back out to the flat ground on the near side instead of leaving it on the climb.
        // The shipped level arrays are already clear, so this returns every position untouched.
        static float RampSafeX(float x)
        {
            float nearest=Ramps[0],best=Mathf.Abs(x-nearest);
            for(int i=1;i<Ramps.Length;i++){float d=Mathf.Abs(x-Ramps[i]);if(d<best){best=d;nearest=Ramps[i];}}
            if(best>=RampClearance)return x;
            return x<nearest?nearest-RampClearance:nearest+RampClearance;
        }
        public Sprite Art(string name) => Resources.Load<Sprite>("Art/"+name);
        public GameObject SpriteObject(string name, Sprite sprite, Vector2 position, float height, int order, Transform parent = null)
        {
            var go = new GameObject(name); go.transform.SetParent(parent ? parent : world);
            go.transform.position = position;
            var sr = go.AddComponent<SpriteRenderer>(); sr.sprite = sprite; sr.sortingOrder = order; sr.sharedMaterial = spriteMaterial;
            if (sprite != null) go.transform.localScale = Vector3.one*(height/sprite.bounds.size.y);
            return go;
        }
        void BuildWorld()
        {
            world = new GameObject("Mountain Course").transform;
            BuildTerrain();
            levelRoots = new Transform[LevelCount + 1];
            for (int lv = 1; lv <= LevelCount; lv++)
            {
                var root = new GameObject("Stage " + lv).transform;
                root.SetParent(world);
                levelRoots[lv] = root;
                BuildLevelContent(lv, root);
                root.gameObject.SetActive(false);
            }
            var rider = new GameObject("Player Snowboarder"); rider.transform.SetParent(world);
            rider.transform.position=new Vector2(Levels[0].Start,Surface(Levels[0].Start)+1.1f);
            Player=rider.AddComponent<Rider>();Player.Game=this;
            var rb=rider.AddComponent<Rigidbody2D>();rb.mass=1;rb.gravityScale=2;rb.linearDamping=.08f;rb.angularDamping=2;rb.collisionDetectionMode=CollisionDetectionMode2D.Continuous;rb.interpolation=RigidbodyInterpolation2D.Interpolate;
            var capsule=rider.AddComponent<CapsuleCollider2D>();capsule.direction=CapsuleDirection2D.Horizontal;capsule.size=new Vector2(1.3f,.42f);capsule.sharedMaterial=new PhysicsMaterial2D("Board friction"){friction=.04f,bounciness=0};
            Player.Body=rb;
            // Board height sets the scale for the whole rider: the torso uses the same
            // pixels-per-unit as the legs, so the two splits line up exactly.
            Sprite bottomArt=Art("Boarder_Bottom"), topArt=Art("Boarder_Top");
            var board=SpriteObject("Board and boots",bottomArt,Vector2.zero,BoardHeight,6,rider.transform);board.transform.localPosition=new Vector3(0,.15f);board.transform.localRotation=Quaternion.identity;
            var top=SpriteObject("Animated rider",topArt,Vector2.zero,BoardHeight*(topArt.bounds.size.y/bottomArt.bounds.size.y),7,rider.transform);top.transform.localPosition=new Vector3(RiderTopOffsetX,RiderTopOffsetY);top.transform.localRotation=Quaternion.identity;Player.Visual=top.transform;
            rb.simulated=false;
            BuildSnow();
        }
        void BuildTerrain()
        {
            int count = Mathf.RoundToInt((CourseMaxX - CourseMinX) / .5f) + 1;
            var points = new Vector2[count];
            var vertices = new Vector3[count*2]; var tris = new int[(count-1)*6];
            for(int i=0;i<count;i++)
            {
                float x = CourseMinX+i*.5f, y = Surface(x);
                points[i]=new Vector2(x,y); vertices[i*2]=new Vector3(x,y); vertices[i*2+1]=new Vector3(x,y-30);
                if(i<count-1) { int t=i*6,v=i*2; tris[t]=v;tris[t+1]=v+2;tris[t+2]=v+1;tris[t+3]=v+1;tris[t+4]=v+2;tris[t+5]=v+3; }
            }
            var terrain = new GameObject("Snow Slope - physics surface"); terrain.transform.SetParent(world);
            var mesh = new Mesh();mesh.vertices=vertices;mesh.triangles=tris;mesh.RecalculateNormals();
            var colors=new Color[vertices.Length];for(int i=0;i<colors.Length;i++)colors[i]=new Color(.84f,.94f,.98f);mesh.colors=colors;
            terrain.AddComponent<MeshFilter>().sharedMesh=mesh;
            var mr=terrain.AddComponent<MeshRenderer>();mr.sharedMaterial=new Material(Resources.Load<Material>("SnowMesh"));mr.sortingOrder=0;
            var edge=terrain.AddComponent<EdgeCollider2D>();edge.points=points;edge.edgeRadius=.08f;
            edge.sharedMaterial=new PhysicsMaterial2D("Packed snow"){friction=.10f,bounciness=0};
            var line=terrain.AddComponent<LineRenderer>();line.sharedMaterial=new Material(Resources.Load<Material>("SnowMesh"));line.positionCount=count;line.startWidth=.22f;line.endWidth=.22f;line.sortingOrder=1;line.startColor=line.endColor=Color.white;
            for(int i=0;i<count;i++)line.SetPosition(i,points[i]);
            int decor=Mathf.CeilToInt((CourseMaxX+10f)/15f);
            for(int i=0;i<decor;i++)
            {
                float x=i*15-10;
                SpriteObject("Distant tree",Art(i%2==0?"Snow-Tree-1":"Snow-Tree-2"),new Vector2(x,Surface(x)+2),5,-3);
                SpriteObject("Cloud",Art("Sprite_Cloud_"+(i%3==0?"2":i%3==1?"5":"6")),new Vector2(x+5,Surface(x)+12),2.5f,-5);
            }
        }
        void BuildLevelContent(int level, Transform root)
        {
            var def = Levels[level - 1];
            foreach(float spot in def.Trees)
            {
                float x=RampSafeX(spot);
                var tree=SpriteObject("Tree obstacle",Art("Snow-Tree-2"),new Vector2(x,Surface(x)+0.7f),2.0f,3,root);
                var collider=tree.AddComponent<BoxCollider2D>(); collider.size=new Vector2(.4f/tree.transform.localScale.x,1.0f/tree.transform.localScale.y);
                tree.AddComponent<Obstacle>();
            }
            foreach(float spot in def.Rocks)
            {
                float x=RampSafeX(spot);
                var rock=SpriteObject("Rock obstacle",Art("Snow-Rock"),new Vector2(x,Surface(x)+.35f),.7f,3,root);
                var col=rock.AddComponent<CircleCollider2D>();col.radius=.36f/rock.transform.localScale.x;
                rock.AddComponent<Obstacle>();
            }
            foreach(float x in def.Checkpoints)
            {
                var flag=SpriteObject("Checkpoint",checkpointSprite,new Vector2(x,Surface(x)),1.5f,3,root);
                var col=flag.AddComponent<BoxCollider2D>();
                col.size=new Vector2(1.0f/flag.transform.localScale.x,2.4f/flag.transform.localScale.y);
                col.offset=new Vector2(0,1.2f/flag.transform.localScale.y);
                col.isTrigger=true;
                var cp=flag.AddComponent<Checkpoint>();cp.Flag=flag.GetComponent<SpriteRenderer>();cp.Game=this;
            }
            for(float x=def.Start+8f;x<def.Finish-8f;x+=8f)
                AddPickup(PickupKind.Snowflake,x,1.7f+(((int)(x/8f))%5==0?1.5f:0),root);
            foreach(float x in def.Boosts)AddPickup(PickupKind.Boost,x,.75f,root);
            foreach(float x in def.Shields)AddPickup(PickupKind.Shield,x,.75f,root);
            AddPickup(PickupKind.Finish,def.Finish,2,root);
        }
        void AddPickup(PickupKind kind,float x,float height,Transform parent)
        {
            string art=kind==PickupKind.Snowflake?"Snowflake":kind==PickupKind.Boost?"Boost":kind==PickupKind.Shield?"Shield":"Finish";
            var go=SpriteObject(art,Art(art),new Vector2(x,Surface(x)+height),kind==PickupKind.Finish?4:.85f,4,parent);
            // Generous trigger so a fast rider never slips past a power-up.
            bool powerUp=kind==PickupKind.Boost || kind==PickupKind.Shield;
            float radius=kind==PickupKind.Finish?2.5f:powerUp?1.05f:.8f;
            var col=go.AddComponent<CircleCollider2D>();col.radius=radius/go.transform.localScale.x;col.isTrigger=true;
            var pickup=go.AddComponent<CoursePickup>();pickup.Kind=kind;
        }
        void BuildSnow()
        {
            var go=new GameObject("Falling snow"); go.transform.SetParent(View.transform);go.transform.localPosition=new Vector3(0,12,5);
            snowfall=go.AddComponent<ParticleSystem>();var main=snowfall.main;main.startLifetime=7;main.startSpeed=0;main.startSize=new ParticleSystem.MinMaxCurve(.04f,.11f);main.startColor=new Color(1,1,1,.8f);main.maxParticles=500;main.simulationSpace=ParticleSystemSimulationSpace.World;
            var em=snowfall.emission;em.rateOverTime=45;var shape=snowfall.shape;shape.shapeType=ParticleSystemShapeType.Box;shape.scale=new Vector3(38,1,1);
            var vel=snowfall.velocityOverLifetime;vel.enabled=true;vel.space=ParticleSystemSimulationSpace.World;vel.y=-3;vel.x=-.7f;
            var renderer=go.GetComponent<ParticleSystemRenderer>();renderer.sharedMaterial=new Material(Resources.Load<Material>("SnowMesh"));renderer.sharedMaterial.mainTexture=Resources.Load<Texture2D>("Art/Snowflake");renderer.sortingOrder=12;
            if(!Snow)snowfall.Stop();
        }
        Sprite MakeCheckpointSprite()
        {
            int w=42,h=64;
            var tex=new Texture2D(w,h,TextureFormat.RGBA32,false);
            var pole=new Color(.25f,.29f,.34f); var flag=new Color(1f,.58f,.16f); var shade=new Color(.85f,.42f,.10f);
            for(int y=0;y<h;y++)for(int x=0;x<w;x++)
            {
                Color c=Color.clear;
                if(x>=6&&x<=10)c=pole;
                if(x>10&&x<=34)
                {
                    float t=(x-10)/24f;
                    float halfY=11f*(1f-t);
                    if(y>= 49-halfY && y<=49+halfY) c=(y<49?flag:shade);
                }
                tex.SetPixel(x,y,c);
            }
            tex.Apply();
            return Sprite.Create(tex,new Rect(0,0,w,h),new Vector2(.5f,0f),100f);
        }
        public void StartRun() => StartRun(1);
        public void StartRun(int level)
        {
            Level=Mathf.Clamp(level,1,Mathf.Min(LevelCount,Unlocked));Lives=MaxLives;Score=0;Combo=0;elapsed=0;scoreFraction=0;ShieldUntil=BoostUntil=comboUntil=0;toast="";
            State=RunState.Playing;Time.timeScale=1;
            EnterLevel(Level);
        }
        void EnterLevel(int level)
        {
            Level=level;
            for(int i=1;i<=LevelCount;i++)levelRoots[i].gameObject.SetActive(i==level);
            foreach(var pickup in levelRoots[level].GetComponentsInChildren<CoursePickup>(true))pickup.gameObject.SetActive(true);
            levelCheckpoints.Clear();
            levelCheckpoints.AddRange(levelRoots[level].GetComponentsInChildren<Checkpoint>(true));
            foreach(var cp in levelCheckpoints)cp.ResetCheckpoint();
            var def=Levels[level-1];
            LevelStart=def.Start;FinishX=def.Finish;RespawnX=def.Start;
            Player.Body.simulated=true;
            Player.ResetAt(def.Start);
            View.transform.position=new Vector3(def.Start+5,Surface(def.Start)+4,-10);
        }
        public void NextLevel()
        {
            if(State!=RunState.LevelComplete)return;
            EnterLevel(Level+1);
            State=RunState.Playing;Time.timeScale=1;
            Notify("STAGE  "+Level+"  /  GO");
        }
        public void Reach(Checkpoint checkpoint)
        {
            RespawnX=Mathf.Max(RespawnX,checkpoint.transform.position.x);
            Notify("CHECKPOINT");
        }
        public void Notify(string message) { toast=message;toastUntil=Time.time+2.5f; }
        public void AddScore(int amount,bool chain)
        {
            if(chain){Combo=Time.time<comboUntil?Mathf.Min(Combo+1,8):1;comboUntil=Time.time+5;}
            Score+=amount*Mathf.Max(1,Combo);
        }
        public void Collect(PickupKind kind)
        {
            if(State!=RunState.Playing)return;
            if(kind==PickupKind.Finish){Finish();return;}
            if(kind==PickupKind.Snowflake){AddScore(100,true);Notify("SNOWFLAKE  +"+(100*Mathf.Max(1,Combo)));}
            if(kind==PickupKind.Boost){BoostUntil=Time.time+BoostSeconds;Notify("SPEED BOOST  /  5 SECONDS");}
            if(kind==PickupKind.Shield){ShieldUntil=Time.time+ShieldSeconds;Notify("INVINCIBILITY  /  4 SECONDS");}
        }
        public void Crash()
        {
            if(State!=RunState.Playing || Time.time<ShieldUntil || Time.time<Player.SafeUntil)return;
            Lives--;Combo=0;comboUntil=0;PlaySound("Crash SFX");
            if(Lives<=0){State=RunState.Lost;Player.Body.simulated=false;SaveBest();Notify("RUN OVER");return;}
            Player.ResetAt(RespawnX);Player.SafeUntil=Time.time+1.5f;Notify("CRASH  /  "+Lives+" LIVES LEFT");
        }
        // Hitting a tree or rock sends the rider back to the last checkpoint. A shield is
        // spent on the hit instead of a life; without a shield a life is lost. The safety
        // window is NOT refreshed on repeat hits, so ramming an obstacle drains lives instead
        // of looping forever without a penalty.
        public void ObstacleHit(float obstacleX)
        {
            if(State!=RunState.Playing)return;
            if(Time.time<ShieldUntil)
            {
                ShieldUntil=0;
                Player.ResetAt(RespawnX);Player.SafeUntil=Mathf.Max(Player.SafeUntil,Time.time+.6f);
                Notify("SHIELD LOST  /  BACK TO CHECKPOINT");
                return;
            }
            if(Time.time<Player.SafeUntil){Player.ResetAt(RespawnX);return;}
            Lives--;Combo=0;comboUntil=0;PlaySound("Crash SFX");
            if(Lives<=0){State=RunState.Lost;Player.Body.simulated=false;SaveBest();Notify("RUN OVER");return;}
            Player.ResetAt(RespawnX);Player.SafeUntil=Time.time+.7f;Notify("CRASH  /  "+Lives+" LIVES LEFT");
        }
        void Finish()
        {
            if(State!=RunState.Playing)return;
            AddScore(Lives*500,false);Player.Body.simulated=false;PlaySound("Finish SFX");SaveBest();
            if(Level<LevelCount)
            {
                Unlocked=Mathf.Max(Unlocked,Mathf.Min(Level+1,LevelCount));
                PlayerPrefs.SetInt(UnlockKey,Unlocked);PlayerPrefs.Save();
                State=RunState.LevelComplete;
            }
            else State=RunState.Won;
        }
        void SaveBest(){best=Mathf.Max(best,Score);PlayerPrefs.SetInt("SnowBoarder.Best",best);PlayerPrefs.Save();}
        void PlaySound(string clip){var sound=Resources.Load<AudioClip>("Audio/"+clip);if(sound)audioSource.PlayOneShot(sound,Volume);}
        void Update()
        {
            var keyboard=Keyboard.current;
            if(keyboard!=null && keyboard.escapeKey.wasPressedThisFrame)
            {
                if(State==RunState.Playing){State=RunState.Paused;Time.timeScale=0;}
                else if(State==RunState.Paused){State=RunState.Playing;Time.timeScale=1;}
                else if(State==RunState.HowTo||State==RunState.LevelSelect)State=RunState.Menu;
                else if(State==RunState.Options)State=returnToPause?RunState.Paused:RunState.Menu;
            }
            if(keyboard!=null && keyboard.enterKey.wasPressedThisFrame)
            {
                if(State==RunState.Menu)State=RunState.LevelSelect;
                else if(State==RunState.HowTo)State=RunState.Menu;
                else if(State==RunState.LevelComplete)NextLevel();
            }
            if(State!=RunState.Playing)return;
            elapsed+=Time.deltaTime;
            Vector2 pp=Player.Body.position;
            float px=pp.x;
            foreach(var cp in levelCheckpoints)
                if(!cp.Reached && px>=cp.transform.position.x)cp.Activate();
            scoreFraction+=Mathf.Max(0,Player.Body.linearVelocity.x)*Time.deltaTime*2;
            if(scoreFraction>=1){int n=(int)scoreFraction;Score+=n;scoreFraction-=n;}
            if(Combo>0 && Time.time>comboUntil)Combo=0;
            if(pp.y<Surface(px)-10)Crash();
            if(px>FinishX+3)Finish();
        }
        void LateUpdate()
        {
            if(State==RunState.Menu || State==RunState.HowTo || State==RunState.LevelSelect || State==RunState.Options&&!returnToPause)
            {
                float pan=Mathf.Sin(Time.unscaledTime*.15f)*1.4f;
                View.transform.position=new Vector3(27+pan,Surface(27)+4.8f,-10);
                return;
            }
            Vector3 target=Player.transform.position+new Vector3(6,3.5f,-10);
            View.transform.position=Vector3.Lerp(View.transform.position,target,1-Mathf.Exp(-4*Time.unscaledDeltaTime));
        }

        // ---------- generated art ----------
        Texture2D Rounded(int n,float radius,Color fill,Color edge,float edgeW)
        {
            var t=new Texture2D(n,n,TextureFormat.RGBA32,false);
            float h=n/2f-1f;
            for(int y=0;y<n;y++)for(int x=0;x<n;x++)
            {
                float px=x+.5f-h-.5f, py=y+.5f-h-.5f;
                float qx=Mathf.Abs(px)-(h-.5f)+radius;
                float qy=Mathf.Abs(py)-(h-.5f)+radius;
                float d=Mathf.Min(Mathf.Max(qx,qy),0f)+Mathf.Sqrt(Mathf.Max(qx,0f)*Mathf.Max(qx,0f)+Mathf.Max(qy,0f)*Mathf.Max(qy,0f))-radius;
                float aa=Mathf.Clamp01(-d+.5f);
                Color c=Color.clear;
                if(aa>0f){c=(-d<edgeW)?edge:fill;c.a*=aa;}
                t.SetPixel(x,y,c);
            }
            t.Apply();t.wrapMode=TextureWrapMode.Clamp;
            return t;
        }
        GUIStyle RoundedStyle(Texture2D tex,Color text,int fontSize)
        {
            var s=new GUIStyle(GUI.skin.button){fontSize=fontSize,fontStyle=FontStyle.Bold,alignment=TextAnchor.MiddleCenter};
            s.border=new RectOffset(18,18,18,18);
            s.normal.background=tex;s.hover.background=tex;s.active.background=tex;
            s.normal.textColor=text;s.hover.textColor=text;s.active.textColor=text;
            s.padding=new RectOffset(10,10,6,6);
            return s;
        }
        Texture2D MakeHeart(Color body,Color rim)
        {
            const int n=56;
            var tex=new Texture2D(n,n,TextureFormat.RGBA32,false);
            for(int y=0;y<n;y++)for(int x=0;x<n;x++)
            {
                float u=((x+.5f)/n)*2.6f-1.3f;
                float v=((y+.5f)/n)*2.6f-1.3f;
                float q=u*u+v*v-1f;
                float f=q*q*q-u*u*v*v*v;
                float cover=Mathf.Clamp01(-f*7f);
                Color c=Color.clear;
                if(cover>0f)
                {
                    Color bodyT=(v>0f)?Color.Lerp(body,Color.Lerp(body,Color.white,.22f),v):body;
                    c=Color.Lerp(bodyT,rim,1f-Mathf.Clamp01(cover*1.7f));
                    // small glossy highlight in the upper-left lobe
                    float hx=u+.5f, hy=v-.55f;
                    if(hx*hx+hy*hy<0.055f)c=Color.Lerp(c,Color.white,.5f);
                    c.a=cover;
                }
                tex.SetPixel(x,y,c);
            }
            tex.Apply();
            return tex;
        }
        Texture2D MakeClock(Color c)
        {
            const int n=32;
            var tex=new Texture2D(n,n,TextureFormat.RGBA32,false);
            for(int y=0;y<n;y++)for(int x=0;x<n;x++)
            {
                float u=((x+.5f)/n)*2f-1f, v=((y+.5f)/n)*2f-1f;
                float r=Mathf.Sqrt(u*u+v*v);
                float a=0f;
                if(r>0.62f&&r<0.86f)a=1f;
                if(Mathf.Abs(u)<0.07f&&v>0f&&v<0.52f)a=1f;
                if(Mathf.Abs(v)<0.07f&&u>0f&&u<0.44f)a=1f;
                tex.SetPixel(x,y,new Color(c.r,c.g,c.b,a));
            }
            tex.Apply();
            return tex;
        }
        Texture2D MakeGauge(Color c)
        {
            const int n=32;
            var tex=new Texture2D(n,n,TextureFormat.RGBA32,false);
            for(int y=0;y<n;y++)for(int x=0;x<n;x++)
            {
                float u=((x+.5f)/n)*2f-1f, v=((y+.5f)/n)*2f-1f;
                float r=Mathf.Sqrt(u*u+v*v);
                float a=0f;
                if(r>0.62f&&r<0.88f)a=1f;
                // needle up-right
                float nx=u-0.34f, ny=v-0.30f;
                if(nx*nx+ny*ny<0.02f&&v>-0.1f)a=1f;
                if(Mathf.Abs(u)<0.06f&&Mathf.Abs(v)<0.06f)a=1f;
                tex.SetPixel(x,y,new Color(c.r,c.g,c.b,a));
            }
            tex.Apply();
            return tex;
        }
        void BuildWipes()
        {
            if(wipeTex!=null)return;
            const int n=64;
            wipeTex=new Texture2D[WipeSteps+1];
            for(int s=0;s<=WipeSteps;s++)
            {
                var t=new Texture2D(n,n,TextureFormat.RGBA32,false);
                float frac=s/(float)WipeSteps;
                for(int y=0;y<n;y++)for(int x=0;x<n;x++)
                {
                    float u=((x+.5f)/n)*2f-1f, v=((y+.5f)/n)*2f-1f;
                    float r=Mathf.Sqrt(u*u+v*v);
                    float ang=Mathf.Atan2(u,v); if(ang<0f)ang+=Mathf.PI*2f;
                    bool covered = r<=0.94f && ang<=frac*Mathf.PI*2f;
                    t.SetPixel(x,y,covered?new Color(.02f,.05f,.09f,.72f):Color.clear);
                }
                t.Apply();wipeTex[s]=t;
            }
        }
        void InitStyles()
        {
            if(stylesReady)return;stylesReady=true;
            accent=new Texture2D(1,1);accent.SetPixel(0,0,new Color(.15f,.73f,.85f));accent.Apply();
            dim=new Texture2D(1,1);dim.SetPixel(0,0,new Color(.01f,.03f,.06f,.62f));dim.Apply();
            cardTex=Rounded(48,16,new Color(.04f,.12f,.20f,.94f),new Color(.13f,.31f,.44f),2f);
            roundedPrimary=Rounded(48,14,new Color(.98f,.55f,.18f),new Color(.80f,.40f,.06f),2f);
            roundedPrimaryHover=Rounded(48,14,new Color(1f,.67f,.30f),new Color(.86f,.46f,.10f),2f);
            roundedPrimaryActive=Rounded(48,14,new Color(.86f,.45f,.11f),new Color(.70f,.34f,.04f),2f);
            roundedGhost=Rounded(48,14,new Color(.09f,.17f,.25f,.82f),new Color(.19f,.34f,.46f),2f);
            roundedGhostHover=Rounded(48,14,new Color(.15f,.28f,.38f,.92f),new Color(.28f,.46f,.58f),2f);
            roundedLocked=Rounded(48,14,new Color(.09f,.12f,.16f,.9f),new Color(.18f,.22f,.28f),2f);
            heartFull=MakeHeart(new Color(.92f,.18f,.26f),new Color(.62f,.07f,.13f));
            heartEmpty=MakeHeart(new Color(.20f,.26f,.34f),new Color(.34f,.42f,.50f));
            clockIcon=MakeClock(new Color(.78f,.88f,.95f));
            gaugeIcon=MakeGauge(new Color(.78f,.88f,.95f));
            BuildWipes();
            title=new GUIStyle(GUI.skin.label){fontSize=64,fontStyle=FontStyle.Bold};title.normal.textColor=Color.white;
            heading=new GUIStyle(GUI.skin.label){fontSize=28,fontStyle=FontStyle.Bold};heading.normal.textColor=Color.white;
            body=new GUIStyle(GUI.skin.label){fontSize=20};body.normal.textColor=new Color(.87f,.94f,.98f);
            small=new GUIStyle(GUI.skin.label){fontSize=15};small.normal.textColor=new Color(.76f,.86f,.93f);
            scoreStyle=new GUIStyle(GUI.skin.label){fontSize=30,fontStyle=FontStyle.Bold};scoreStyle.normal.textColor=Color.white;
            subtitle=new GUIStyle(GUI.skin.label){fontSize=19,fontStyle=FontStyle.Italic};subtitle.normal.textColor=new Color(.62f,.80f,.88f);
            good=new GUIStyle(GUI.skin.label){fontSize=28,fontStyle=FontStyle.Bold};good.normal.textColor=new Color(.35f,.92f,1f);
            danger=new GUIStyle(GUI.skin.label){fontSize=28,fontStyle=FontStyle.Bold};danger.normal.textColor=new Color(1f,.44f,.44f);
            centerTitle=new GUIStyle(title){alignment=TextAnchor.MiddleCenter};
            centerSub=new GUIStyle(subtitle){alignment=TextAnchor.MiddleCenter};
            centerHeading=new GUIStyle(heading){alignment=TextAnchor.MiddleCenter};
            menuTitleSmall=new GUIStyle(small){fontSize=14,fontStyle=FontStyle.Bold};
            cardStyle=new GUIStyle(GUI.skin.box){alignment=TextAnchor.MiddleCenter};
            cardStyle.border=new RectOffset(18,18,18,18);
            cardStyle.normal.background=cardTex;
            barTrack=new Texture2D(1,1);barTrack.SetPixel(0,0,new Color(.16f,.30f,.42f,.55f));barTrack.Apply();
            button=RoundedStyle(roundedPrimary,Color.white,22);
            button.normal.background=roundedPrimary;button.hover.background=roundedPrimaryHover;button.active.background=roundedPrimaryActive;
            button2=RoundedStyle(roundedGhost,Color.white,20);
            buttonLocked=RoundedStyle(roundedLocked,new Color(.44f,.50f,.57f),18);
            var play=RoundedStyle(roundedPrimary,new Color(.05f,.09f,.13f),22);
            play.normal.background=roundedPrimary;play.hover.background=roundedPrimaryHover;play.active.background=roundedPrimaryActive;
            menuPlay=play;
            logoSnow=new GUIStyle(title){fontSize=92,fontStyle=FontStyle.BoldAndItalic};
            logoBoarder=new GUIStyle(logoSnow){fontSize=78};logoBoarder.normal.textColor=new Color(.32f,.91f,1);
            menuLink=RoundedStyle(roundedGhost,new Color(.82f,.90f,.95f),17);
            menuLink.hover.background=roundedGhostHover;menuLink.active.background=roundedGhostHover;
        }
        bool Button(float x,float y,float w,string text,bool primary=true)=>GUI.Button(new Rect(x,y,w,52),text,primary?button:button2);
        void ShadowLabel(float x,float y,float w,float h,string text,GUIStyle style)
        {
            Color old=GUI.color;
            GUI.color=new Color(0f,0f,0f,.5f);
            GUI.Label(new Rect(x+2,y+2,w,h),text,style);
            GUI.color=old;
            GUI.Label(new Rect(x,y,w,h),text,style);
        }
        void DrawHearts(float x,float y,float size)
        {
            for(int i=0;i<MaxLives;i++)
                GUI.DrawTexture(new Rect(x+i*(size+6),y,size,size),i<Lives?heartFull:heartEmpty);
        }
        void DrawPowerIcon(float x,float y,float size,PickupKind kind,float remaining,float total)
        {
            string art=kind==PickupKind.Boost?"Boost":"Shield";
            DrawMenuSprite(art,new Rect(x,y,size,size));
            int s=Mathf.Clamp(Mathf.RoundToInt((1f-Mathf.Clamp01(remaining/total))*WipeSteps),0,WipeSteps);
            GUI.DrawTexture(new Rect(x,y,size,size),wipeTex[s]);
        }
        void DrawHud()
        {
            ShadowLabel(36,22,320,42,"SCORE  "+Score.ToString("N0"),scoreStyle);
            GUI.DrawTexture(new Rect(336,30,22,22),gaugeIcon);
            ShadowLabel(364,26,220,38,"SPEED  "+(Player.Body.linearVelocity.magnitude*3.6f).ToString("0")+" km/h",body);
            DrawHearts(596,26,32);
            ShadowLabel(712,28,70,34,Lives+" / "+MaxLives,Lives<=1?danger:small);
            ShadowLabel(806,22,240,42,"COMBO  x"+Mathf.Max(1,Combo),Combo>1?good:scoreStyle);
            if(Time.time<BoostUntil)DrawPowerIcon(1066,20,50,PickupKind.Boost,BoostUntil-Time.time,BoostSeconds);
            if(Time.time<ShieldUntil)DrawPowerIcon(1130,20,50,PickupKind.Shield,ShieldUntil-Time.time,ShieldSeconds);
            GUI.DrawTexture(new Rect(36,80,20,20),clockIcon);
            ShadowLabel(62,76,170,30,"TIME  "+elapsed.ToString("0.0")+"s",small);
            ShadowLabel(250,76,190,30,"STAGE  "+Level+" / "+LevelCount,small);
            ShadowLabel(450,76,340,30,Levels[Level-1].Difficulty+"   /   CHECKPOINT  "+CheckpointProgress(),small);
            ShadowLabel(900,76,320,30,"BEST  "+best.ToString("N0"),small);
            float progress=Mathf.Clamp01((Player.Body.position.x-LevelStart)/Mathf.Max(1f,FinishX-LevelStart));
            GUI.DrawTexture(new Rect(24,118,1232,6),barTrack);
            GUI.DrawTexture(new Rect(24,118,1232*progress,6),accent);
            if(Time.time<toastUntil)ShadowLabel(340,150,600,60,toast,heading);
        }
        string CheckpointProgress()
        {
            int total=Levels[Level-1].Checkpoints.Length, reached=0;
            foreach(var cp in levelCheckpoints)if(cp.Reached)reached++;
            return reached+" / "+total;
        }
        void InitMenuArt()
        {
            if(menuShade!=null)return;
            menuShade=new Texture2D(128,1,TextureFormat.RGBA32,false);
            for(int x=0;x<128;x++)
            {
                float t=x/127f;
                float opacity=Mathf.Lerp(.94f,.04f,Mathf.SmoothStep(0,1,t));
                menuShade.SetPixel(x,0,new Color(.018f,.065f,.11f,opacity));
            }
            menuShade.Apply();menuShade.wrapMode=TextureWrapMode.Clamp;
            menuShadow=new Texture2D(64,32,TextureFormat.RGBA32,false);
            for(int y=0;y<32;y++)for(int x=0;x<64;x++)
            {
                float distance=Mathf.Pow((x-31.5f)/31.5f,2)+Mathf.Pow((y-15.5f)/15.5f,2);
                menuShadow.SetPixel(x,y,new Color(.02f,.13f,.20f,Mathf.Clamp01(1-distance)*.25f));
            }
            menuShadow.Apply();
            if(barTrack==null){barTrack=new Texture2D(1,1);barTrack.SetPixel(0,0,new Color(.16f,.30f,.42f,.55f));barTrack.Apply();}
        }
        void DrawMenuSprite(string name,Rect rect)
        {
            Sprite sprite=Art(name);if(sprite==null)return;
            Rect r=sprite.textureRect;
            GUI.DrawTextureWithTexCoords(rect,sprite.texture,new Rect(r.x/sprite.texture.width,r.y/sprite.texture.height,r.width/sprite.texture.width,r.height/sprite.texture.height));
        }
        void DrawMenu()
        {
            InitMenuArt();
            GUI.DrawTexture(new Rect(0,0,1280,720),menuShade);
            GUI.DrawTexture(new Rect(0,0,1280,5),accent);
            ShadowLabel(94,56,380,24,"SNOW / MOUNTAIN ARCADE",menuTitleSmall);
            Matrix4x4 original=GUI.matrix;
            GUIUtility.RotateAroundPivot(-5,new Vector2(300,215));
            Color originalColor=GUI.color;GUI.color=new Color(0,0,0,.30f);
            GUI.Label(new Rect(101,129,570,124),"SNOW",logoSnow);
            GUI.Label(new Rect(101,227,600,114),"BOARDER",logoBoarder);
            GUI.color=originalColor;
            GUI.Label(new Rect(94,120,570,124),"SNOW",logoSnow);
            GUI.Label(new Rect(94,218,600,114),"BOARDER",logoBoarder);
            GUI.matrix=original;
            GUI.DrawTexture(new Rect(101,342,60,4),accent);
            ShadowLabel(101,360,470,24,"DROP IN. RIDE YOUR LINE.",menuTitleSmall);
            if(GUI.Button(MenuButtonRect(404f),"PLAY   >",menuPlay))State=RunState.LevelSelect;
            if(GUI.Button(MenuButtonRect(404f+MenuButtonHeight+MenuButtonGap),"HOW TO PLAY",menuLink))State=RunState.HowTo;
            if(GUI.Button(MenuButtonRect(404f+2f*(MenuButtonHeight+MenuButtonGap)),"OPTIONS",menuLink)){returnToPause=false;State=RunState.Options;}
            if(GUI.Button(MenuButtonRect(404f+3f*(MenuButtonHeight+MenuButtonGap)),"QUIT",menuLink))Quit();
            ShadowLabel(925,53,258,22,"BEST RUN",new GUIStyle(menuTitleSmall){alignment=TextAnchor.UpperRight});
            ShadowLabel(925,74,258,44,best.ToString("N0"),new GUIStyle(centerHeading){alignment=TextAnchor.UpperRight,fontSize=26});
            float bob=Mathf.Sin(Time.unscaledTime*1.6f)*7;
            GUI.DrawTexture(new Rect(710,604,420,45),menuShadow);
            GUIUtility.RotateAroundPivot(-12+Mathf.Sin(Time.unscaledTime*.8f)*3,new Vector2(925,460+bob));
            // Same layout as the in-game rider: the torso sits at (RiderTopOffsetX, RiderTopOffsetY)
            // from the rider origin, the board at (0, .15); BoardHeight world units = 160 menu px.
            var boardRect=new Rect(754,459+bob,332,160);
            var topSize=new Vector2(270.8f,181.8f);
            float pxPerUnit=boardRect.height/BoardHeight;
            Vector2 topCenter=boardRect.center+new Vector2(RiderTopOffsetX,-(RiderTopOffsetY-.15f))*pxPerUnit;
            DrawMenuSprite("Boarder_Bottom",boardRect);
            DrawMenuSprite("Boarder_Top",new Rect(topCenter-topSize*.5f,topSize));
            GUI.matrix=original;
            GUI.color=new Color(1,1,1,.65f);
            GUI.DrawTexture(new Rect(680,584,93,3),Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(704,598,54,2),Texture2D.whiteTexture);
            GUI.color=originalColor;
        }
        void DrawHowTo()
        {
            GUI.Box(new Rect(230,150,820,470),GUIContent.none,cardStyle);
            ShadowLabel(0,90,1280,80,"HOW TO PLAY",new GUIStyle(centerTitle){fontSize=56});
            ShadowLabel(280,190,720,34,"CONTROLS",heading);
            string[] keys={"A / D   or   LEFT / RIGHT","W   or   UP","S   or   DOWN","SPACE","ESC"};
            string[] acts={"lean and spin in the air","speed up","brake","jump","pause / resume"};
            for(int i=0;i<keys.Length;i++)
            {
                ShadowLabel(280,244+i*40,360,30,keys[i],new GUIStyle(good){fontSize=19});
                ShadowLabel(660,244+i*40,340,30,acts[i],body);
            }
            ShadowLabel(280,470,760,60,"Collect snowflakes for points. Land spins and long jumps for bonus\npoints and build a combo multiplier up to x8.",small);
            ShadowLabel(280,540,760,60,"A shield absorbs one obstacle hit, then sends you back to the last\ncheckpoint. Clear a stage to unlock the next one.",small);
            if(Button(470,640,340,"BACK"))State=RunState.Menu;
        }
        void DrawLevelSelect()
        {
            ShadowLabel(0,84,1280,80,"SELECT STAGE",new GUIStyle(centerTitle){fontSize=58});
            ShadowLabel(0,180,1280,30,"Clear a stage to unlock the next one.",centerSub);
            for(int i=1;i<=LevelCount;i++)
            {
                float y=228+(i-1)*126;
                GUI.Box(new Rect(300,y,680,106),GUIContent.none,cardStyle);
                bool unlocked=i<=Unlocked;
                ShadowLabel(330,y+18,300,34,"STAGE "+i,new GUIStyle(centerHeading){alignment=TextAnchor.MiddleLeft});
                ShadowLabel(330,y+52,300,30,Levels[i-1].Difficulty,new GUIStyle(small){fontSize=17});
                if(unlocked)
                {
                    if(GUI.Button(new Rect(800,y+28,150,50),"PLAY",button))StartRun(i);
                }
                else
                {
                    // Not a button on purpose: GUI.enabled=false still leaves a control that
                    // reports hover/active states, so a locked stage has to be a plain label.
                    ShadowLabel(800,y+28,150,50,"LOCKED",new GUIStyle(buttonLocked){alignment=TextAnchor.MiddleCenter});
                }
            }
            if(GUI.Button(new Rect(300,620,220,50),"BACK",button2))State=RunState.Menu;
        }
        void DrawOptions()
        {
            GUI.Box(new Rect(240,150,800,420),GUIContent.none,cardStyle);
            ShadowLabel(0,90,1280,80,"OPTIONS",new GUIStyle(centerTitle){fontSize=54});
            ShadowLabel(300,200,720,34,"Sound volume   "+Mathf.RoundToInt(Volume*100)+"%",heading);
            Volume=GUI.HorizontalSlider(new Rect(300,262,680,25),Volume,0,1);
            bool snow=GUI.Toggle(new Rect(300,320,680,40),Snow,"   Falling snow",new GUIStyle(heading){fontSize=22});
            if(snow!=Snow){Snow=snow;if(Snow)snowfall.Play();else snowfall.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);}
            ShadowLabel(300,392,680,30,"Progress: "+Unlocked+" / "+LevelCount+" stages unlocked.",body);
            if(Button(300,500,300,"BACK",false)){PlayerPrefs.SetFloat("SnowBoarder.Volume",Volume);PlayerPrefs.SetInt("SnowBoarder.Snow",Snow?1:0);PlayerPrefs.Save();State=returnToPause?RunState.Paused:RunState.Menu;}
        }
        void DrawPause()
        {
            GUI.Box(new Rect(430,200,420,300),GUIContent.none,cardStyle);
            ShadowLabel(0,120,1280,70,"PAUSED",new GUIStyle(centerTitle){fontSize=52});
            if(Button(470,250,340,"RESUME")){State=RunState.Playing;Time.timeScale=1;}
            if(Button(470,316,340,"OPTIONS",false)){returnToPause=true;State=RunState.Options;}
            if(Button(470,382,340,"MAIN MENU",false)){Time.timeScale=1;Player.Body.simulated=false;State=RunState.Menu;}
        }
        void DrawLevelComplete()
        {
            bool last=Level>=LevelCount;
            GUI.Box(new Rect(370,180,540,400),GUIContent.none,cardStyle);
            ShadowLabel(0,86,1280,80,"STAGE "+Level+" CLEARED",new GUIStyle(centerTitle){fontSize=52});
            ShadowLabel(0,168,1280,34,last?"Final stage complete.":("Stage "+(Level+1)+" ("+Levels[Level].Difficulty+") unlocked."),centerSub);
            ShadowLabel(430,214,420,40,"SCORE  "+Score.ToString("N0"),heading);
            GUI.DrawTexture(new Rect(430,274,20,20),clockIcon);
            ShadowLabel(456,272,420,30,"TIME  "+elapsed.ToString("0.0")+"s",body);
            ShadowLabel(430,314,420,30,"STAGE  "+Level+" / "+LevelCount,body);
            DrawHearts(430,360,30);
            if(Button(430,420,420,"NEXT STAGE   >"))NextLevel();
            if(Button(430,486,420,"MAIN MENU",false))State=RunState.Menu;
        }
        void DrawEnd()
        {
            bool won=State==RunState.Won;
            GUI.Box(new Rect(370,180,540,400),GUIContent.none,cardStyle);
            ShadowLabel(0,86,1280,80,won?"SUMMIT COMPLETE":"RUN OVER",new GUIStyle(centerTitle){fontSize=52});
            ShadowLabel(0,168,1280,34,won?"You conquered all three stages!":"Keep carving - you'll make it next time.",centerSub);
            ShadowLabel(430,214,420,40,"SCORE  "+Score.ToString("N0"),heading);
            ShadowLabel(430,266,420,30,"BEST  "+best.ToString("N0"),body);
            GUI.DrawTexture(new Rect(430,314,20,20),clockIcon);
            ShadowLabel(456,312,420,30,"TIME  "+elapsed.ToString("0.0")+"s",body);
            ShadowLabel(430,352,420,30,won?"+ "+(Lives*500).ToString("N0")+" lives bonus":"Tip: lean hard and jump before obstacles",small);
            if(Button(430,420,420,"RIDE AGAIN"))StartRun(Level);
            if(Button(430,486,420,"MAIN MENU",false))State=RunState.Menu;
        }
        void OnGUI()
        {
            InitStyles();
            GUI.matrix=Matrix4x4.Scale(new Vector3(Screen.width/1280f,Screen.height/720f,1));
            if(State==RunState.Playing||State==RunState.Paused)
            {
                DrawHud();
                if(State==RunState.Playing)return;
                GUI.DrawTexture(new Rect(0,0,1280,720),dim);
                DrawPause();return;
            }
            if(State==RunState.LevelComplete)
            {
                GUI.DrawTexture(new Rect(0,0,1280,720),dim);
                DrawLevelComplete();return;
            }
            if(State!=RunState.Menu)GUI.DrawTexture(new Rect(0,0,1280,720),dim);
            if(State==RunState.Menu)DrawMenu();
            else if(State==RunState.HowTo)DrawHowTo();
            else if(State==RunState.LevelSelect)DrawLevelSelect();
            else if(State==RunState.Options)DrawOptions();
            else DrawEnd();
        }
        void Quit()
        {
            Time.timeScale=1;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying=false;
#else
            Application.Quit();
#endif
        }
        void OnDestroy(){Time.timeScale=1;if(Instance==this)Instance=null;}
    }

    public sealed class Rider : MonoBehaviour
    {
        public SnowBoarderGame Game;
        public Rigidbody2D Body;
        public Transform Visual;
        public float SafeUntil;
        public bool Grounded { get; private set; }
        float spin, previousAngle, airborneTime, lastGroundTime, lastJumpTime=-5;
        bool wasGrounded, jumpRequested;
        float lean, throttle, brake;
        public void ResetAt(float x)
        {
            Body.simulated=true;
            Body.position=new Vector2(x,SnowBoarderGame.Surface(x)+.55f);Body.rotation=SnowBoarderGame.Slope(x);
            Body.linearVelocity=new Vector2(6,-1);Body.angularVelocity=0;spin=0;airborneTime=0;wasGrounded=false;previousAngle=Body.rotation;SafeUntil=Time.time+1.5f;
        }
        void Update()
        {
            if(Game.State!=RunState.Playing)return;
            var k=Keyboard.current;
            lean=0;throttle=0;brake=0;
            if(k!=null)
            {
                lean=(k.dKey.isPressed||k.rightArrowKey.isPressed?1:0)-(k.aKey.isPressed||k.leftArrowKey.isPressed?1:0);
                throttle=k.wKey.isPressed||k.upArrowKey.isPressed?1:0;brake=k.sKey.isPressed||k.downArrowKey.isPressed?1:0;
                if(k.spaceKey.wasPressedThisFrame)jumpRequested=true;
            }
            if(Game.AutoTest)
            {
                throttle=1;
                bool obstacleAhead=false;
                foreach(var hit in Physics2D.RaycastAll(Body.position,Vector2.right,5f))
                    if(hit.collider.GetComponent<Obstacle>()!=null){obstacleAhead=true;break;}
                if(Grounded && Time.time-lastJumpTime>0.45f && (obstacleAhead||Time.time-lastJumpTime>1.3f))jumpRequested=true;
            }
            Visual.localRotation=Quaternion.Euler(0,0,-lean*10+Mathf.Sin(Time.time*8)*Mathf.Min(2.5f,Body.linearVelocity.magnitude*.12f));
            foreach(var sr in GetComponentsInChildren<SpriteRenderer>())sr.color=Time.time<Game.ShieldUntil?new Color(.4f,1,1):Time.time<SafeUntil&&Mathf.Sin(Time.time*25)>0?new Color(1,1,1,.3f):Color.white;
        }
        void FixedUpdate()
        {
            if(Game.State!=RunState.Playing)return;
            Vector2 origin=Body.position;
            Grounded=false;
            foreach(var hit in Physics2D.RaycastAll(origin,Vector2.down,.72f))
                if(hit.collider is EdgeCollider2D){Grounded=true;break;}
            if(Grounded)lastGroundTime=Time.time;
            float slope=SnowBoarderGame.Slope(origin.x);
            Vector2 tangent=new Vector2(Mathf.Cos(slope*Mathf.Deg2Rad),Mathf.Sin(slope*Mathf.Deg2Rad));
            bool boosting=Time.time<Game.BoostUntil;
            float top=boosting?SnowBoarderGame.BoostSpeed:SnowBoarderGame.CruiseSpeed;
            if(Grounded)
            {
                if(!wasGrounded && airborneTime>.25f)
                {
                    if(Mathf.Abs(Mathf.DeltaAngle(Body.rotation,slope))>100){Game.Crash();return;}
                    int rotations=Mathf.FloorToInt((Mathf.Abs(spin)+25)/360);
                    if(rotations>0){Game.AddScore(300*rotations,true);Game.Notify((rotations*360)+" SPIN  /  x"+Game.Combo);}
                    else if(airborneTime>.7f){Game.AddScore(75,true);Game.Notify("CLEAN AIR  /  x"+Game.Combo);}
                }
                spin=0;airborneTime=0;
                float target=slope-lean*14;
                Body.AddTorque(Mathf.DeltaAngle(Body.rotation,target)*.15f-Body.angularVelocity*.06f);
                // Speed controller. A constant push holds the base speed with no keys held, and
                // the error term adds force only while the rider is under the cap, tapering to
                // nothing at it. A boost raises the cap AND switches to BoostAccel, which is
                // more than double CruiseAccel, so speed up always out-accelerates normal
                // riding instead of merely toping out at a higher number.
                float drive=SnowBoarderGame.DriveForce+throttle*SnowBoarderGame.ThrottleForce
                    +(boosting?SnowBoarderGame.BoostForce:0f);
                if(throttle>0||boosting)
                    drive+=Mathf.Clamp01((top-Vector2.Dot(Body.linearVelocity,tangent))/SnowBoarderGame.AccelBand)
                        *(boosting?SnowBoarderGame.BoostAccel:SnowBoarderGame.CruiseAccel);
                Body.AddForce(tangent*drive);
                if(brake>0)Body.AddForce(-Body.linearVelocity*3);
            }
            else
            {
                airborneTime+=Time.fixedDeltaTime;spin+=Mathf.DeltaAngle(previousAngle,Body.rotation);
                Body.AddTorque(-lean*SnowBoarderGame.AirSpinTorque);
                // Hard cap keeps every airborne spin readable no matter how long a boost
                // jump (or a run of ramps) keeps the rider in the air.
                if(Mathf.Abs(Body.angularVelocity)>SnowBoarderGame.MaxAirSpin)
                    Body.angularVelocity=Mathf.Sign(Body.angularVelocity)*SnowBoarderGame.MaxAirSpin;
            }
            if(jumpRequested)
            {
                if(Time.time-lastGroundTime<.2f && Time.time-lastJumpTime>.4f){Body.AddForce(Vector2.up*11,ForceMode2D.Impulse);lastJumpTime=Time.time;Grounded=false;}
                jumpRequested=false;
            }
            if(Body.linearVelocity.magnitude>top)Body.linearVelocity=Body.linearVelocity.normalized*top;
            if(Body.position.x<Game.LevelStart)Body.linearVelocity=new Vector2(Mathf.Max(3,Body.linearVelocity.x),Body.linearVelocity.y);
            previousAngle=Body.rotation;wasGrounded=Grounded;
        }
        void OnCollisionEnter2D(Collision2D collision)
        {
            if(collision.collider.GetComponent<Obstacle>()!=null)
                Game.ObstacleHit(collision.collider.bounds.center.x);
        }
    }
    public sealed class Obstacle : MonoBehaviour { }
    public sealed class Checkpoint : MonoBehaviour
    {
        public SnowBoarderGame Game;
        public SpriteRenderer Flag;
        public bool Reached;
        public void ResetCheckpoint(){Reached=false;if(Flag)Flag.color=Color.white;}
        public void Activate()
        {
            if(Reached)return;
            Reached=true;if(Flag)Flag.color=new Color(.35f,1f,.62f);
            Game.Reach(this);
        }
        void OnTriggerEnter2D(Collider2D other)
        {
            if(other.GetComponent<Rider>()==null)return;
            if(SnowBoarderGame.Instance.State!=RunState.Playing)return;
            Activate();
        }
    }
    public sealed class CoursePickup : MonoBehaviour
    {
        public PickupKind Kind;
        void OnTriggerEnter2D(Collider2D other)
        {
            if(other.GetComponent<Rider>()==null||SnowBoarderGame.Instance.State!=RunState.Playing)return;
            SnowBoarderGame.Instance.Collect(Kind);gameObject.SetActive(false);
        }
        void Update(){if(Kind!=PickupKind.Finish)transform.Rotate(0,0,30*Time.deltaTime);}
    }
}
