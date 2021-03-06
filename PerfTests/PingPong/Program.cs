﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Management;
using System.Runtime.Remoting.Messaging;
using System.Threading;
using System.Threading.Tasks;
using Stacks;
using Stacks.Actors;
using Stacks.Actors.DI;

// Code has been ported from Akka.Net library https://github.com/akkadotnet/akka.net
#pragma warning disable 4014

namespace PingPong
{
    public class Program
    {
        public static uint CpuSpeed()
        {
#if !mono
            var mo = new ManagementObject("Win32_Processor.DeviceID='CPU0'");
            var sp = (uint)(mo["CurrentClockSpeed"]);
            mo.Dispose();
            return sp;
#else
            return 0;
#endif
        }

        private static void Main(params string[] args)
        {
            uint timesToRun = args.Length == 1 ? uint.Parse(args[0]) : 1u;
            Start(timesToRun);
            Console.ReadKey();
        }

        private static async void Start(uint timesToRun)
        {
            const int repeatFactor = 500;
            const long repeat = 3000L * repeatFactor;

            var processorCount = Environment.ProcessorCount;
            if (processorCount == 0)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Failed to read processor count..");
                return;
            }
            if (System.Diagnostics.Debugger.IsAttached)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Warning: Debugger is attached. This has a major performance impact on this benchmark");
                Console.ResetColor();
            }

            var serverProxyOptions = new ActorServerProxyOptions(actorSessionInjectionEnabled: false);

            int workerThreads;
            int completionPortThreads;
            ThreadPool.GetAvailableThreads(out workerThreads, out completionPortThreads);

            Console.WriteLine("Worker threads:         {0}", workerThreads);
            Console.WriteLine("OSVersion:              {0}", Environment.OSVersion);
            Console.WriteLine("ProcessorCount:         {0}", processorCount);
            Console.WriteLine("ClockSpeed:             {0} MHZ", CpuSpeed());
            Console.WriteLine("Actor Count:            {0}", processorCount * 2);
            Console.WriteLine("Messages sent/received: {0}  ({0:0e0})", GetTotalMessagesReceived(repeat));
            Console.WriteLine();

            //Warm up
            Console.Write("Local     first start time: ");
            await Benchmark(1, 1, 1, PrintStats.StartTimeOnly, -1, -1,
                (idx, wfs) => Tuple.Create((IActorServerProxy)null, ActorSystem.Default.CreateActor<IDestination, Destination>(new object[] { new Destination(wfs) })),
                (idx, d) => d,
                (idx, wfs, dest, r, latch) => ActorSystem.Default.CreateActor<IPingPongActor, PingPongActor>(new object[] { new PingPongActor(wfs, dest, r, latch) }));
            Console.WriteLine(" ms");
            Console.Write("Remote    first start time: ");
            await Benchmark(1, 1, 1, PrintStats.StartTimeOnly, -1, -1,
                (idx, wfs) =>
                {
                    var dest = ActorSystem.Default.CreateActor<IDestination, Destination>(new object[] { new Destination(wfs)});
                    return Tuple.Create(ActorServerProxy.Create("tcp://localhost:" + (54000 + idx), dest), dest);
                },
                (idx, d) => ActorClientProxy.CreateActor<IDestination>("tcp://localhost:" + (54000 + idx)).Result,
                (idx, wfs, dest, r, latch) => ActorSystem.Default.CreateActor<IPingPongActor, PingPongActor>(new object[] { new PingPongActor(wfs, dest, r, latch)}));
            Console.WriteLine(" ms");
            Console.WriteLine();

            Console.WriteLine("            Local actor                        Remote actor");
            Console.WriteLine("Throughput, Msgs/sec, Start [ms], Total [ms],  Msgs/sec, Start [ms], Total [ms]");
            for (var i = 0; i < timesToRun; i++)
            {
                var redCountLocalActor = 0;
                var redCountRemoteActor = 0;
                var bestThroughputLocalActor = 0L;
                var bestThroughputRemoteActor = 0L;
                foreach (var throughput in GetThroughputSettings())
                {
                    var result1 = await Benchmark(throughput, processorCount, repeat, PrintStats.LineStart | PrintStats.Stats, bestThroughputLocalActor, redCountLocalActor,
                       (idx, wfs) => Tuple.Create((IActorServerProxy)null, ActorSystem.Default.CreateActor<IDestination, Destination>(new object[] { new Destination(wfs)})),
                       (idx, d) => d,
                       (idx, wfs, dest, r, latch) => ActorSystem.Default.CreateActor<IPingPongActor, PingPongActor>(new object[] { new PingPongActor(wfs, dest, r, latch)}));
                    bestThroughputLocalActor = result1.Item2;
                    redCountLocalActor = result1.Item3;
                    Console.Write(",  ");

                    var result2 = await Benchmark(throughput, processorCount, repeat, PrintStats.Stats, bestThroughputRemoteActor, redCountRemoteActor,
                        (idx, wfs) =>
                        {
                            var dest =
                                ActorSystem.Default.CreateActor<IDestination, Destination>(new object[] { new Destination(wfs)});
                            return Tuple.Create(ActorServerProxy.Create("tcp://localhost:" + (54000 + idx), dest, serverProxyOptions), dest);
                        },
                        (idx, d) => ActorClientProxy.CreateActor<IDestination>("tcp://localhost:" + (54000 + idx)).Result,
                        (idx, wfs, dest, r, latch) => ActorSystem.Default.CreateActor<IPingPongActor, PingPongActor>(new object[] { new PingPongActor(wfs, dest, r, latch)}));
                    bestThroughputRemoteActor = result2.Item2;
                    redCountRemoteActor = result2.Item3;
                    Console.WriteLine();
                }
            }

            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine("Done..");
        }

        public static IEnumerable<int> GetThroughputSettings()
        {
            yield return 1;
            yield return 5;
            yield return 10;
            yield return 15;
            for (int i = 20; i < 100; i += 10)
            {
                yield return i;
            }
            for (int i = 100; i < 1000; i += 100)
            {
                yield return i;
            }
        }

        private static async Task<Tuple<bool, long, int>> Benchmark(int factor, int numberOfClients, long numberOfRepeats, PrintStats printStats, long bestThroughput, int redCount,
            Func<int, IWaitForStarts, Tuple<IActorServerProxy, IDestination>> createDestination,
            Func<int, IDestination, IDestination> createDestClient,
            Func<int, IWaitForStarts, IDestination, long, TaskCompletionSource<bool>, IPingPongActor> createPingPong)
        {
            var totalMessagesReceived = GetTotalMessagesReceived(numberOfRepeats);
            //times 2 since the client and the destination both send messages
            long repeatsPerClient = numberOfRepeats / numberOfClients;
            var totalWatch = Stopwatch.StartNew();

            var countdown = new CountdownEvent(numberOfClients * 2);
            var waitForStartsActor =
                ActorSystem.Default.CreateActor<IWaitForStarts, WaitForStarts>(new object[] { new WaitForStarts(countdown)});
            var clients = new List<IPingPongActor>();
            var dests = new List<Tuple<IActorServerProxy, IDestination>>();
            var tasks = new List<Task>();
            for (int i = 0; i < numberOfClients; i++)
            {
                var destination = createDestination(i, waitForStartsActor);
                dests.Add(destination);

                var ts = new TaskCompletionSource<bool>();
                tasks.Add(ts.Task);
                var client = createPingPong(i, waitForStartsActor, createDestClient(i, destination.Item2), repeatsPerClient, ts);
                clients.Add(client);

                client.Start();

                destination.Item2.Start();
            }
            if (!countdown.Wait(TimeSpan.FromSeconds(10)))
            {
                Console.WriteLine("The system did not start in 10 seconds. Aborting.");
                return Tuple.Create(false, bestThroughput, redCount);
            }
            var setupTime = totalWatch.Elapsed;
            var sw = Stopwatch.StartNew();
            clients.ForEach(c => c.Ping());

            await Task.WhenAll(tasks.ToArray());
            sw.Stop();

            totalWatch.Stop();

            dests.ForEach(d => { if (d.Item1 != null) d.Item1.Stop(); });
            dests.ForEach(d => { d.Item2.Stop_(); });
            clients.ForEach(c => c.Stop());

            var elapsedMilliseconds = sw.ElapsedMilliseconds;
            long throughput = elapsedMilliseconds == 0 ? -1 : totalMessagesReceived / elapsedMilliseconds * 1000;
            var foregroundColor = Console.ForegroundColor;
            if (throughput >= bestThroughput)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                bestThroughput = throughput;
                redCount = 0;
            }
            else
            {
                redCount++;
                Console.ForegroundColor = ConsoleColor.Red;
            }
            if (printStats.HasFlag(PrintStats.StartTimeOnly))
            {
                Console.Write("{0,5}", setupTime.TotalMilliseconds.ToString("F2", CultureInfo.InvariantCulture));
            }
            else
            {
                if (printStats.HasFlag(PrintStats.LineStart))
                    Console.Write("{0,10}, ", factor);
                if (printStats.HasFlag(PrintStats.Stats))
                    Console.Write("{0,8}, {1,10}, {2,10}", throughput, setupTime.TotalMilliseconds.ToString("F2", CultureInfo.InvariantCulture), totalWatch.Elapsed.TotalMilliseconds.ToString("F2", CultureInfo.InvariantCulture));
            }
            Console.ForegroundColor = foregroundColor;

            return Tuple.Create(redCount <= 3, bestThroughput, redCount);
        }

        private static long GetTotalMessagesReceived(long numberOfRepeats)
        {
            return numberOfRepeats * 2;
        }


    }


    public interface IDestination
    {
        Task Pong();
        Task Start();
        Task Stop_();
    }

    public class Destination : Actor, IDestination
    {
        private IWaitForStarts waitForStartsActor;

        public Destination(IWaitForStarts waitForStartsActor)
            : base(new ActorSettings { SupportSynchronizationContext = false })
        {
            this.waitForStartsActor = waitForStartsActor;
        }

        public Task Pong()
        {
            return Context.PostTask(null);
        }

        public Task Start()
        {
            waitForStartsActor.OnStart();
            return Task.FromResult(0);
        }

        public Task Stop_()
        {
            Context.Stop();
            return Task.FromResult(0);
        }
    }

    public interface IWaitForStarts
    {
        Task OnStart();
    }

    public class WaitForStarts : Actor, IWaitForStarts
    {
        private readonly CountdownEvent countdown;

        public WaitForStarts(CountdownEvent countdown)
        {
            this.countdown = countdown;
        }

        public new async Task OnStart()
        {
            await Context;
            countdown.Signal();
        }
    }

    [Flags]
    public enum PrintStats
    {
        No = 0,
        LineStart = 1,
        Stats = 2,
        StartTimeOnly = 32768,
    }


    public interface IPingPongActor
    {
        Task Ping();
        Task Recv();
        Task Start();
        Task Stop();
    }

    public class PingPongActor : Actor, IPingPongActor
    {
        private IDestination destination;
        private IWaitForStarts waitForStartsActor;
        private long repeat;
        private long sent;
        private long received;
        private TaskCompletionSource<bool> latch;
        private bool done;

        public PingPongActor(IWaitForStarts waitForStartsActor, IDestination destination, long repeat, TaskCompletionSource<bool> latch)
            : base(new ActorSettings { SupportSynchronizationContext = false })
        {
            this.waitForStartsActor = waitForStartsActor;
            this.destination = destination;
            this.repeat = repeat;
            this.latch = latch;
        }

        public Task Start()
        {
            waitForStartsActor.OnStart();
            return Task.FromResult(0);
        }

        public async Task Ping()
        {
            await Context;

            for (int i = 0; i < Math.Min(1000, repeat); ++i)
            {
                PingImpl();
            }

        }

        private async void PingImpl()
        {
            await destination.Pong();
            await Context;
            ++sent;
            Recv();
        }

        public Task Recv()
        {
            Context.Post(async () =>
            {
                ++received;
                if (sent < repeat)
                {
                    try
                    {
                        await destination.Pong();
                        await Context;
                        if (done) { return; }
                        ++sent;
                        Recv();
                    }
                    catch { }
                }
                else if (received == repeat)
                {
                    done = true;
                    latch.SetResult(true);
                }
            });
            return Task.FromResult(0);
        }

        public new Task Stop()
        {
            Context.Stop();
            // ReSharper disable once SuspiciousTypeConversion.Global
            var d = destination as IActorClientProxy<IDestination>;

            d?.Close();
            return Task.FromResult(0);
        }
    }
}

#pragma warning restore 4014