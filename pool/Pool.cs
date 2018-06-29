
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using XPool.core;
using Newtonsoft.Json;
namespace XPool 
{
    public class Pool {
        public static void Main(string[] args) {
            try
            {
                Console.CancelKeyPress += Engine.OnCancelKeyPress;
                AppDomain.CurrentDomain.UnhandledException += Engine.OnAppDomainUnhandledException;
                AppDomain.CurrentDomain.ProcessExit += Engine.OnProcessExit;

                if(!Engine.HandleCommandLineOptions(args,out var confFile)){
                    return;
                }
                Engine.loadConfig(confFile);
                Engine.Bootstrap();
            }
            catch (PoolStartupAbortException ex)
            {
                if (!string.IsNullOrEmpty(ex.Message))
                    Console.WriteLine(ex.Message);

                Console.WriteLine("\nXPool start failed!");
            }

            catch (JsonException ex)
            {
                Console.WriteLine(ex);
            }

            catch (IOException ex)
            {
                Console.WriteLine(ex);
            }

            catch (AggregateException ex)
            {
                if (!(ex.InnerExceptions.First() is PoolStartupAbortException))
                    Console.WriteLine(ex);

                Console.WriteLine("XPool start failed!");
            }

            catch (OperationCanceledException)
            {
                            }

            catch (Exception ex)
            {
                Console.WriteLine(ex);

                Console.WriteLine("XPool start failed!");
            }

            Engine.Shutdown();
            Process.GetCurrentProcess().CloseMainWindow();
            Process.GetCurrentProcess().Close();
        }

        }
}
