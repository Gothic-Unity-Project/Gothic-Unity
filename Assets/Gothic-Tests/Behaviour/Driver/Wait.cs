using System;
using System.Collections;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;
using Gothic.Core;
using NUnit.Framework;

namespace Gothic.Tests.Behaviour.Driver
{
    /// <summary>
    /// Waits inside steps - no fixed sleeps where a condition can be polled. Each one polls once per frame.
    /// </summary>
    public static class Wait
    {
        public const float DefaultSeconds = 10f;

        // A game minute is 60 game seconds of ~0.07 s each (GameTimeService), ~4.2 s at TimeSpeedMultiplier 1.
        private const float _gameMinuteMaxSeconds = 10f;


        /// <summary>
        /// Until the condition holds. Fails with what was expected and what describe() says was there instead.
        /// </summary>
        public static IEnumerator Until(Func<bool> condition, float seconds, string expected, Func<string> describe)
        {
            var watch = Stopwatch.StartNew();
            while (!condition())
            {
                if (watch.Elapsed.TotalSeconds > seconds)
                    throw new AssertionException($"Expected {expected} within {seconds:0.#} s, but {describe()}");

                yield return null;
            }
        }

        public static IEnumerator Frames(int count)
        {
            for (var i = 0; i < count; i++)
                yield return null;
        }

        /// <summary>
        /// The whole time, for checks that something does not happen within it.
        /// </summary>
        public static IEnumerator Seconds(float seconds)
        {
            var watch = Stopwatch.StartNew();
            while (watch.Elapsed.TotalSeconds < seconds)
                yield return null;
        }

        /// <summary>
        /// Until the game clock's next minute (GameTimeMinuteChangeCallback): what runs on it - mob routines, NPC
        /// routines - has seen the time set before.
        /// </summary>
        public static IEnumerator GameMinute()
        {
            var isChanged = false;
            void OnMinute(DateTime _) => isChanged = true;

            GlobalEventDispatcher.GameTimeMinuteChangeCallback.AddListener(OnMinute);
            try
            {
                yield return Until(() => isChanged, _gameMinuteMaxSeconds, "the next game minute",
                    () => "the game clock didn't tick (paused?)");
            }
            finally
            {
                GlobalEventDispatcher.GameTimeMinuteChangeCallback.RemoveListener(OnMinute);
            }
        }

        /// <summary>
        /// Until the task is done; its exception fails the step.
        /// </summary>
        public static IEnumerator For(Task task)
        {
            while (!task.IsCompleted)
                yield return null;

            if (task.IsFaulted)
                ExceptionDispatchInfo.Capture(task.Exception!.GetBaseException()).Throw();
        }
    }
}
