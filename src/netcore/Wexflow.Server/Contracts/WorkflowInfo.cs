﻿using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System;
using Wexflow.Core.Db;

namespace Wexflow.Server.Contracts
{
    public enum LaunchType
    {
        Startup,
        Trigger,
        Periodic,
        Cron
    }

    public class WorkflowInfo : IComparable<WorkflowInfo>
    {
        public string DbId { get; set; }

        public int Id { get; set; }

        public Guid InstanceId { get; set; }

        public string Name { get; set; }

        public string FilePath { get; set; }

        public LaunchType LaunchType { get; set; }

        public bool IsEnabled { get; set; }

        public bool IsApproval { get; private set; }

        public bool EnableParallelJobs { get; private set; }

        public bool IsWaitingForApproval { get; private set; }

        public string Description { get; set; }

        public bool IsRunning { get; set; }

        public bool IsPaused { get; set; }

        public string Period { get; set; }

        public string CronExpression { get; set; }

        public bool IsExecutionGraphEmpty { get; set; }

        public Variable[] LocalVariables { get; set; }

        public string StartedOn { get; set; }

        public int RetryCount { get; set; }

        public int RetryTimeout { get; set; }

        [JsonConverter(typeof(StringEnumConverter))]
        public Status Status { get; set; }

        public WorkflowInfo(string dbId,
            int id,
            Guid instanceId,
            string name,
            string filePath,
            LaunchType launchType,
            bool isEnabled,
            bool isApproval,
            bool enableParallelJobs,
            bool isWaitingForApproval,
            string desc,
            bool isRunning,
            bool isPaused,
            string period,
            string cronExpression,
            bool isExecutionGraphEmpty,
            Variable[] localVariables,
            string startedOn,
            int retryCount,
            int retryTimeout,
            Core.Db.Status jobStatus)
        {
            DbId = dbId;
            Id = id;
            InstanceId = instanceId;
            Name = name;
            FilePath = filePath;
            LaunchType = launchType;
            IsEnabled = isEnabled;
            IsApproval = isApproval;
            EnableParallelJobs = enableParallelJobs;
            IsWaitingForApproval = isWaitingForApproval;
            Description = desc;
            IsRunning = isRunning;
            IsPaused = isPaused;
            Period = period;
            CronExpression = cronExpression;
            IsExecutionGraphEmpty = isExecutionGraphEmpty;
            LocalVariables = localVariables;
            StartedOn = startedOn;
            RetryCount = retryCount;
            RetryTimeout = retryTimeout;
            Status = (Status)jobStatus;
        }

        public int CompareTo(WorkflowInfo other) => other.Id.CompareTo(Id);

        public override bool Equals(object obj)
        {
            var wfi = obj as WorkflowInfo;

            if (wfi == null)
            {
                return false;
            }

            if (ReferenceEquals(this, obj))
            {
                return true;
            }

            return wfi.Id.Equals(Id);
        }

        public override int GetHashCode()
        {
            return Id.GetHashCode();
        }

        public static bool operator ==(WorkflowInfo left, WorkflowInfo right)
        {
            if (left is null)
            {
                return right is null;
            }

            return left.Equals(right);
        }

        public static bool operator !=(WorkflowInfo left, WorkflowInfo right)
        {
            return !(left == right);
        }

        public static bool operator <(WorkflowInfo left, WorkflowInfo right)
        {
            return left is null ? right is not null : left.CompareTo(right) < 0;
        }

        public static bool operator <=(WorkflowInfo left, WorkflowInfo right)
        {
            return left is null || left.CompareTo(right) <= 0;
        }

        public static bool operator >(WorkflowInfo left, WorkflowInfo right)
        {
            return left is not null && left.CompareTo(right) > 0;
        }

        public static bool operator >=(WorkflowInfo left, WorkflowInfo right)
        {
            return left is null ? right is null : left.CompareTo(right) >= 0;
        }
    }
}
