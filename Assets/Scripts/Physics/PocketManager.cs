// SnookerKit — pocket sensors, pot bookkeeping and drop animation (Physics module, CONTRACTS §2).
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SnookerKit
{
    /// <summary>Owns the six pocket trigger sensors ("PocketSensor_0".."PocketSensor_5") and all pot bookkeeping:
    /// on a ball entering a sensor the ball is potted (Ball.Pot), object-ball pots are recorded into the tracked
    /// ShotContext and raised as GameEvents.PottedBall, and the ball plays its shrink-and-deactivate drop
    /// animation. Cue-ball pots set ShotContext.CuePotted and never raise PottedBall — cue pots are fouls,
    /// never scores.</summary>
    [DisallowMultipleComponent]
    public class PocketManager : MonoBehaviour
    {
        /// <summary>Drop animation duration (s) from natural scale down to the final shrink factor.</summary>
        private const float DropSeconds = 0.25f;
        /// <summary>Final scale factor of the drop animation before the ball deactivates.</summary>
        private const float DropFinalScale = 0.25f;

        /// <summary>The six pocket centres in world space, initialized by the static BuildPocketLayout at type
        /// load (C# forbids instance code assigning a static readonly field). Order (frozen CONTRACTS §2):
        /// -X+Z corner, +X+Z corner, -X-Z corner, +X-Z corner, then +Z middle, -Z middle.
        /// Y = BallRestHeight + 0.012 — sensor height, just above the resting ball centre.</summary>
        public static readonly Vector3[] Pockets = BuildPocketLayout();

        /// <summary>Sensor-index lookup by collider (filled once at build; TryGetPocket is allocation-free).</summary>
        private readonly Dictionary<Collider, int> _sensorLookup = new Dictionary<Collider, int>(6);

        private GameObject _sensorsRoot;
        private bool _built;

        /// <summary>Static layout builder invoked once from the Pockets field initializer.</summary>
        private static Vector3[] BuildPocketLayout()
        {
            float y = GameConfig.TableDims.BallRestHeight + 0.012f;
            float cx = GameConfig.TableDims.MaxX + 0.02f;
            float cz = GameConfig.TableDims.MaxZ + 0.02f;
            return new Vector3[]
            {
                new Vector3(-cx, y, cz),   // 0: -X+Z corner
                new Vector3(cx, y, cz),    // 1: +X+Z corner
                new Vector3(-cx, y, -cz),  // 2: -X-Z corner
                new Vector3(cx, y, -cz),   // 3: +X-Z corner
                new Vector3(0f, y, cz),    // 4: +Z middle
                new Vector3(0f, y, -cz),   // 5: -Z middle
            };
        }

        /// <summary>Registers the manager in the ServiceRegistry (frozen manager convention).</summary>
        private void Awake()
        {
            ServiceRegistry.Register<PocketManager>(this);
        }

        /// <summary>Builds the six trigger sensors once (guarded, like TableBuilder.Build).</summary>
        private void Start()
        {
            if (_built) return;
            _built = true;

            _sensorsRoot = new GameObject("PocketSensors");
            _sensorsRoot.transform.SetParent(transform, false);

            for (int i = 0; i < Pockets.Length; i++)
            {
                GameObject go = new GameObject("PocketSensor_" + i);
                go.transform.SetParent(_sensorsRoot.transform, false);
                go.transform.position = Pockets[i];

                SphereCollider col = go.AddComponent<SphereCollider>();
                col.isTrigger = true;
                col.radius = i < 4
                    ? GameConfig.TableDims.PocketSensorCorner
                    : GameConfig.TableDims.PocketSensorMiddle;

                // Kinematic rigidbody so dynamic-ball trigger callbacks always fire against the sensors.
                Rigidbody rb = go.AddComponent<Rigidbody>();
                rb.isKinematic = true;

                PocketSensorRelay relay = go.AddComponent<PocketSensorRelay>();
                relay.Owner = this;
                relay.Index = i;

                _sensorLookup[col] = i;
            }
        }

        /// <summary>Deregisters, stops any running drop animations and clears the sensor lookup.</summary>
        private void OnDestroy()
        {
            StopAllCoroutines();
            ServiceRegistry.Deregister<PocketManager>();
            _sensorLookup.Clear();
            _sensorsRoot = null;
        }

        /// <summary>Resolves a collider to its pocket index (0..5). Defensive: false for null/foreign colliders.</summary>
        public bool TryGetPocket(Collider sensor, out int index)
        {
            if (sensor == null)
            {
                index = -1;
                return false;
            }
            if (_sensorLookup.TryGetValue(sensor, out index)) return true;
            index = -1;
            return false;
        }

        /// <summary>Trigger entry from a sensor relay. Object balls: Pot + PottedBallRecord + GameEvents.PottedBall
        /// + PocketDrop + PotChime audio + drop animation. Cue ball: Pot + CuePotted + PocketDrop only — cue pots
        /// are fouls and are never scores. Non-ball colliders (baize/cushions overlap the sensor volume) are
        /// ignored. Runs per trigger event, never per frame.</summary>
        private void HandleSensorEnter(int index, Collider other)
        {
            Ball ball = other != null ? other.GetComponentInParent<Ball>() : null;
            if (ball == null) return;

            if (ball.IsCue)
            {
                ball.Pot(index);

                ShotContext cueCtx = Ball.CurrentContext;
                if (cueCtx != null && !cueCtx.Completed) cueCtx.CuePotted = true;

                if (ServiceRegistry.TryGet<AudioManager>(out AudioManager cueAudio) && cueAudio != null)
                {
                    cueAudio.Play(SfxKey.PocketDrop);
                }
                return;
            }

            ball.Pot(index);

            ShotContext ctx = Ball.CurrentContext;
            if (ctx != null && !ctx.Completed)
            {
                ctx.Potted.Add(new PottedBallRecord { Color = ball.Color, PocketIndex = index });
            }

            GameEvents.RaisePottedBall(new PottedBallArgs { Color = ball.Color, PocketIndex = index });

            if (ServiceRegistry.TryGet<AudioManager>(out AudioManager audio) && audio != null)
            {
                audio.Play(SfxKey.PocketDrop);
                audio.Play(SfxKey.PotChime);
            }

            if (ball.State == BallState.Dropping)
            {
                StartCoroutine(DropAndHide(ball));
            }
        }

        /// <summary>Drop animation: shrink the ball from its natural scale to ~0.25 over 0.25 s, then deactivate
        /// the GameObject. Ball.PlaceAt restores the natural scale on respot, so nothing needs un-shrinking here.
        /// One iterator per pot (rare event); safe against the ball being destroyed mid-drop.</summary>
        private IEnumerator DropAndHide(Ball ball)
        {
            if (ball == null) yield break;

            Transform transform = ball.transform;
            Vector3 startScale = transform.localScale;
            Vector3 endScale = startScale * DropFinalScale;
            float elapsed = 0f;

            while (elapsed < DropSeconds)
            {
                yield return null;
                if (ball == null) yield break; // destroyed mid-drop (scene teardown)

                elapsed += Time.deltaTime;
                float k = elapsed >= DropSeconds ? 1f : elapsed / DropSeconds;
                transform.localScale = Vector3.Lerp(startScale, endScale, k);
            }

            if (ball != null) ball.gameObject.SetActive(false);
        }

        /// <summary>Per-sensor relay component: OnTriggerEnter cannot reach a parent script, so each sensor
        /// carries this lightweight forwarder bound to its pocket index. Private nested type — never surfaced.</summary>
        private class PocketSensorRelay : MonoBehaviour
        {
            /// <summary>Owning manager (always the creating PocketManager).</summary>
            internal PocketManager Owner;

            /// <summary>Pocket index of the sensor this relay sits on (0..5).</summary>
            internal int Index;

            /// <summary>Forwards the trigger to the manager; ignores foreign colliders there.</summary>
            private void OnTriggerEnter(Collider other)
            {
                if (Owner != null) Owner.HandleSensorEnter(Index, other);
            }
        }
    }
}
