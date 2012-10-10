﻿using System;
using System.Collections.Generic;

using log4net;
using SqlToGraphite.Clients;
using SqlToGraphite.Config;
using SqlToGraphite.UnitTests;
using SqlToGraphiteInterfaces;

namespace SqlToGraphite.Conf
{
    public class ConfigRepository : IConfigRepository
    {
        private readonly IConfigPersister configPersister;

        private readonly IConfigReader configReader;

        private readonly IKnownGraphiteClients knownGraphiteClients;

        private readonly ICache cache;

        private readonly ISleep sleep;

        private readonly ILog log;

        private readonly int errorReadingConfigSleepTime;

        private readonly IGenericSerializer genericSerializer;

        private List<string> errors;
        private GraphiteClients clientList;
        public const string FailedToLoadAnyConfiguration = "Failed to load any configuration";
        public const string FailedToFindClients = "Failed to find any clients defined";
        public const string FailedToFindHosts = "Failed to find any hosts defined";
        public const string FailedToFindTemplates = "Failed to find any templates defined";
        public const string UnknownClient = "unknown client defined";

        private SqlToGraphiteConfig masterConfig;

        public ConfigRepository(IConfigReader configReader, ICache cache, ISleep sleep, ILog log, int errorReadingConfigSleepTime, IGenericSerializer genericSerializer)
        {
            this.configReader = configReader;
            this.knownGraphiteClients = knownGraphiteClients;
            this.cache = cache;
            this.sleep = sleep;
            this.log = log;
            this.errorReadingConfigSleepTime = errorReadingConfigSleepTime;
            this.genericSerializer = genericSerializer;
            clientList = new GraphiteClients();
            this.masterConfig = new SqlToGraphiteConfig(new AssemblyResolver(new DirectoryImpl()));
        }

        public ConfigRepository(IConfigReader configReader, ICache cache, ISleep sleep, ILog log, int errorReadingConfigSleepTime, IConfigPersister configPersister, IGenericSerializer genericSerializer)
            : this(configReader, cache, sleep, log, errorReadingConfigSleepTime, genericSerializer)
        {
            this.configPersister = configPersister;
        }

        public void Load()
        {
            if (cache.HasExpired())
            {
                log.Debug("Cache has expired");
                SqlToGraphiteConfig graphiteConfig = null;
                while (graphiteConfig == null)
                {
                    try
                    {
                        graphiteConfig = GetConfig(configReader);
                        if (graphiteConfig == null)
                        {
                            log.Error(FailedToLoadAnyConfiguration);
                            this.errors.Add(FailedToLoadAnyConfiguration);
                            this.RestForAwhile();
                        }
                    }
                    catch (Exception ex)
                    {
                        log.Error(ex);
                        this.RestForAwhile();
                    }
                }

                this.masterConfig = graphiteConfig;
                Init();
                cache.ResetCache();
            }
        }

        private void RestForAwhile()
        {
            log.Debug(string.Format("sleeping for {0} mins", errorReadingConfigSleepTime));
            this.sleep.Sleep(errorReadingConfigSleepTime);
        }

        private SqlToGraphiteConfig GetConfig(IConfigReader configurationReader)
        {
            var xml = configurationReader.GetXml();
            return genericSerializer.Deserialize<SqlToGraphiteConfig>(xml);
        }

        private void Init()
        {
            this.errors = new List<string>();
            clientList = new GraphiteClients();
        }

        public List<string> Errors
        {
            get
            {
                return errors;
            }
        }

        public ListOfUniqueType<Client> GetClients()
        {
            return this.masterConfig.Clients;
        }

        public List<Template> GetTemplates()
        {
            return this.masterConfig.Templates;
        }

        public List<Host> GetHosts()
        {
            return this.masterConfig.Hosts;
        }

        public bool Validate()
        {
            this.masterConfig.Validate();
            var rtn = true;
            if (masterConfig.Templates.Count == 0)
            {
                errors.Add(FailedToFindTemplates);
                rtn = false;
            }

            if (masterConfig.Hosts.Count == 0)
            {
                errors.Add(FailedToFindHosts);
                rtn = false;
            }

            if (masterConfig.Clients.Count == 0)
            {
                errors.Add(FailedToFindClients);
                rtn = false;
            }

            return rtn;
        }

        public GraphiteClients GetClientList()
        {
            return this.clientList;
        }

        public void AddClient(Client client)
        {
            this.masterConfig.Clients.Add(client);
        }

        public void AddHost(string name, List<Role> roles)
        {
            var host = new Host { Name = name, Roles = roles };
            this.masterConfig.Hosts.Add(host);
        }

        public void AddWorkItem(WorkItems workItem)
        {
            this.masterConfig.Templates[0].WorkItems.Add(workItem);
        }

        public void AddJob(Job job)
        {
            this.masterConfig.Jobs.Add(job);
        }

        public void AddTask(TaskDetails taskProperties)
        {
            var added = false;
            foreach (var template in this.masterConfig.Templates)
            {
                foreach (var wi in template.WorkItems)
                {
                    added = this.AddIfRolesAreTheSame(taskProperties, added, wi);
                }
            }

            if (!added)
            {
                var taskSetItem = CreateTaskSetItem(taskProperties);
                if (masterConfig.Templates.Count == 0)
                {
                    masterConfig.Templates.Add(new Template());
                }

                var workItem = new WorkItems { RoleName = taskProperties.Role, TaskSet = new List<TaskSet> { taskSetItem } };
                this.masterConfig.Templates[0].WorkItems.Add(workItem);
            }
        }

        public void Save()
        {
            this.configPersister.Save(this.masterConfig);
        }

        private bool AddIfRolesAreTheSame(TaskDetails taskProperties, bool added, WorkItems template)
        {
            if (template.RoleName == taskProperties.Role)
            {
                foreach (var t in template.TaskSet)
                {
                    added = AddTaskIfFrequencyIsTheSame(taskProperties, t);
                }

                if (!added)
                {
                    added = this.AddTaskToNewFrequency(taskProperties, template);
                }
            }

            return added;
        }

        private bool AddTaskToNewFrequency(TaskDetails taskProperties, WorkItems template)
        {
            template.TaskSet.Add(this.CreateTaskSetItem(taskProperties));
            return true;
        }

        private static bool AddTaskIfFrequencyIsTheSame(TaskDetails taskProperties, TaskSet t)
        {
            bool added = false;
            if (t.Frequency == taskProperties.Frequency)
            {
                t.Tasks.Add(CreateTask(taskProperties));
                added = true;
            }

            return added;
        }

        private TaskSet CreateTaskSetItem(TaskDetails taskProperties)
        {
            var tasks = new List<Task> { CreateTask(taskProperties) };
            var taskSetItem = new TaskSet { Frequency = taskProperties.Frequency, Tasks = tasks };
            return taskSetItem;
        }

        private static Task CreateTask(TaskDetails taskProperties)
        {
            return new Task { JobName = taskProperties.JobName };
        }

        public List<Job> GetJobs()
        {
            return this.masterConfig.Jobs;
        }

        public Job GetJob(string jobName)
        {
            foreach (var job in this.masterConfig.Jobs)
            {
                if (job.Name == jobName)
                {
                    return job;
                }
            }

            throw new JobNotFoundException();
        }

        public Client GetClient(string clientName)
        {
            foreach (var client in this.masterConfig.Clients)
            {
                if (client.ClientName == clientName)
                {
                    return client;
                }
            }

            throw new ClientNotFoundException(string.Format("Client {0} is not found", clientName));
        }
    }
}