using System;
using SqlToGraphite.Conf;
using SqlToGraphiteInterfaces;
using Topshelf;
using log4net;

namespace SqlToGraphite.host
{
    using Environment = SqlToGraphite.Conf.Environment;

    public class Program
    {
        private static ILog log;

        public static TaskManager CreateTaskManager(SqlToGraphiteSection configuration)
        {
            var cacheLength = new TimeSpan(0, configuration.ConfigCacheLengthMinutes, 0);
            var stop = new Stop();
            var directoryImpl = new DirectoryImpl();
            var assemblyResolver = new AssemblyResolver(directoryImpl, log);
            IEncryption encryption = new Encryption();
            IDataClientFactory dataClientFactory = new DataClientFactory(log, assemblyResolver, encryption);
            IGraphiteClientFactory graphiteClientFactory = new GraphiteClientFactory(log);

            var configReader = new ConfigHttpReader(configuration.ConfigUri, configuration.ConfigUsername, configuration.ConfigPassword);
            var cache = new Cache(cacheLength, log);
            var sleeper = new Sleeper();
            var genericSer = new GenericSerializer(Global.GetNameSpace());
            var cr = new ConfigRepository(configReader, cache, sleeper, log, configuration.MinutesBetweenRetryToGetConfigOnError, genericSer);
            var configMapper = new ConfigMapper(configuration.Hostname, stop, dataClientFactory, graphiteClientFactory, log, cr);
            var roleConfigFactory = new RoleConfigFactory();
            var environment = new Environment();
            var taskSetBuilder = new TaskSetBuilder();
            var configController = new ConfigController(configMapper, log, cr, roleConfigFactory, environment, taskSetBuilder);
            return new TaskManager(log, configController, configuration.ConfigUri, stop, sleeper, configuration.CheckConfigUpdatedEveryMinutes);
        }

        private static SqlToGraphiteSection ReadConfigFromAppConfig()
        {
            return (SqlToGraphiteSection)System.Configuration.ConfigurationManager.GetSection(SqlToGraphiteSection.SectionName);
        }
        public static void Main(string[] args)
        {
            try
            {
                log = LogManager.GetLogger("log");
                log4net.Config.XmlConfigurator.Configure();
                var configuration = ReadConfigFromAppConfig();
                RunApplication(CreateTaskManager(configuration));
            }
            catch (Exception ex)
            {
                log.Error(ex);
                Console.WriteLine(ex.Message);
            }
        }

        private static void RunApplication(TaskManager taskManager)
        {
            HostFactory.Run(
                x =>
                {
                    x.Service<Application>(
                        s =>
                        {
                            s.SetServiceName("tc");
                            s.ConstructUsing(name => new Application(taskManager, log));
                            s.WhenStarted(tc => tc.Start());
                            s.WhenStopped(tc => tc.Stop());
                        });
                    x.RunAsLocalSystem();
                    x.SetDescription("Runs sql to inject data into graphite");
                    x.SetDisplayName("SqlToGraphite");
                    x.SetServiceName("SqlToGraphite");
                });
        }
    }
}
