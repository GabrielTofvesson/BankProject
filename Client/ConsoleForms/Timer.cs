using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace Client.ConsoleForms
{
    public sealed class Timer
    {
        public delegate void Runnable();
        private readonly long millis;
        private readonly Task timer;

        public bool Expired => timer.Status != TaskStatus.Running;

        public Timer(Runnable onExpire, long millis, int resolution = 100)
        {
            this.millis = CurrentTimeMillis() + millis;
            timer = new Task(() =>
            {
                while (CurrentTimeMillis() < this.millis) System.Threading.Thread.Sleep(resolution);
                onExpire();
            });
        }
        public void Start() => timer.Start();
        public TaskAwaiter GetAwaiter() => timer.GetAwaiter();

        private static long CurrentTimeMillis() => DateTime.Now.Ticks / 10000;
    }
}
