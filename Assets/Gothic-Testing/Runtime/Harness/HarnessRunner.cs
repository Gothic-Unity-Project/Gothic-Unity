using UnityEngine;

namespace Gothic.Testing.Harness
{
    /// <summary>
    /// The harness' only MonoBehaviour: a hidden, scene-independent object which gives <see cref="TestSession"/>
    /// the per-frame and per-physics-step callbacks a plain C# class cannot have.
    ///
    /// It is marked DontDestroyOnLoad because a session outlives every scene it drives through - boot, main menu,
    /// loading and world are four scene loads inside one session.
    ///
    /// The game has two seams which look like they could do this job instead. Neither can:
    ///
    /// - >UnityLifecycleEvents< is the central Update/FixedUpdate dispatcher for services, and structurally it is
    ///   exactly this pattern. But it reaches its services through [Inject], so ticking the harness from there
    ///   would mean Gothic.Core referencing Gothic.Testing - an inverted dependency, and harness hooks compiled
    ///   into shipping players, which is what the GOTHIC_FUNCTIONAL_TESTS gate exists to prevent.
    ///
    /// - >UnityMonoService< wraps StartCoroutine on BootstrapAdapter, which lives in the Bootstrap scene and so
    ///   survives every scene load. The blocker is ordering, not lifetime: a session has to exist *before* the
    ///   game does. Random.InitState has to run before anything draws from the RNG, InputDriver has to replace
    ///   the input system before the game reads a device, and the boot watchdog's entire job is guarding the
    ///   ~10-45s boot. At TestSession.Start() there is no Bootstrap scene, no Reflex container and no MonoBehaviour
    ///   for UnityMonoService to borrow.
    ///
    /// Owning the object also keeps the session out of reach of the game's own scene management - nothing the
    /// game does to its scenes can stop the harness from ticking.
    /// </summary>
    public class HarnessRunner : MonoBehaviour
    {
        private const string _objectName = "[Gothic.Testing] Harness";

        private TestSession _session;


        public static HarnessRunner Create(TestSession session)
        {
            var gameObject = new GameObject(_objectName)
            {
                // Not HideAndDontSave: the object has to be destroyable at session end, and it stays visible in
                // the hierarchy on purpose, because "is the harness still alive?" is a question a developer asks.
                hideFlags = HideFlags.DontSave
            };

            DontDestroyOnLoad(gameObject);

            var runner = gameObject.AddComponent<HarnessRunner>();
            runner._session = session;

            return runner;
        }

        /// <summary>
        /// Tear the runner down. DestroyImmediate outside play mode because the deferred Destroy() would never
        /// run there - and a DontDestroyOnLoad object which survives its session leaks into the next fixture.
        /// </summary>
        public void Shutdown()
        {
            _session = null;

            if (gameObject == null)
                return;

            if (Application.isPlaying)
                Destroy(gameObject);
            else
                DestroyImmediate(gameObject);
        }

        private void Update()
        {
            _session?.Watchdog.Tick();
        }

        private void FixedUpdate()
        {
            _session?.Clock.CountFixedFrame();
        }
    }
}
