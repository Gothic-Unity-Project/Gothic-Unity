using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using NUnit.Framework;

namespace Gothic.Tests.Behaviour.Runner
{
    /// <summary>
    /// Runs one step as part of the scenario's coroutine: nested IEnumerators are run here instead of by Unity, so a
    /// failure anywhere inside names the step, and the scenario's time limit is checked every frame. The clock stops
    /// while isClockPaused says so (the game loading a world has a limit of its own).
    /// </summary>
    public static class StepRoutine
    {
        public static IEnumerator Run(ScenarioStep step, Func<IEnumerator> invoke, Stopwatch watch,
            float timeoutSeconds, Func<bool> isClockPaused)
        {
            var routines = new Stack<IEnumerator>();
            Wrap(step, () =>
            {
                var routine = invoke();
                if (routine != null)
                    routines.Push(routine);
            });

            while (routines.Count > 0)
            {
                if (isClockPaused())
                    watch.Stop();
                else
                    watch.Start();

                if (watch.Elapsed.TotalSeconds > timeoutSeconds)
                    throw new AssertionException($"{step}: the scenario took longer than {timeoutSeconds} s");

                object current = null;
                var hasMoved = false;
                Wrap(step, () =>
                {
                    hasMoved = routines.Peek().MoveNext();
                    current = hasMoved ? routines.Peek().Current : null;
                });

                if (!hasMoved)
                    routines.Pop();
                else if (current is IEnumerator nested)
                    routines.Push(nested);
                else
                    yield return current;
            }
        }

        private static void Wrap(ScenarioStep step, Action action)
        {
            try
            {
                action();
            }
            catch (Exception e) when (e is IgnoreException or InconclusiveException or SuccessException)
            {
                throw;
            }
            catch (AssertionException e)
            {
                throw new AssertionException($"{step}: {e.Message}", e);
            }
            catch (Exception e)
            {
                throw new StepFailedException($"{step}: {e.GetType().Name}: {e.Message}", e);
            }
        }
    }
}
