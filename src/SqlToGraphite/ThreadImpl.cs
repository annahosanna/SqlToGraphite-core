using System;
using System.Threading;

namespace SqlToGraphite
{
    public class ThreadImpl : IThread
    {
        private Thread thread;
        
        public ThreadImpl(ThreadStart process)
        {
           thread = new Thread(process);
        }

        public void Start()
        {
            thread.Start();
            Console.WriteLine("****" + thread.ManagedThreadId);
        }

        public void Abort()
        {
            thread.Abort();
        }
    }
}