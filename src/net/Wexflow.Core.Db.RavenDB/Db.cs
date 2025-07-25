﻿using Raven.Client.Documents;
using Raven.Client.Documents.Operations;
using Raven.Client.Documents.Queries;
using Raven.Client.Exceptions;
using Raven.Client.Exceptions.Database;
using Raven.Client.ServerWide;
using Raven.Client.ServerWide.Operations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Wexflow.Core.Db.RavenDB
{
    public sealed class Db : Core.Db.Db
    {
        private static readonly object _padlock = new object();
        private static DocumentStore _store;

        public Db(string connectionString) : base(connectionString)
        {
            var ravenUrl = string.Empty;
            var database = string.Empty;

            var connectionStringParts = ConnectionString.Split(';');
            foreach (var part in connectionStringParts)
            {
                if (!string.IsNullOrEmpty(part.Trim()))
                {
                    var connPart = part.TrimStart(' ').TrimEnd(' ');
                    if (connPart.StartsWith("Database="))
                    {
                        database = connPart.Replace("Database=", string.Empty);
                    }
                    else if (connPart.StartsWith("RavenUrl="))
                    {
                        ravenUrl = connPart.Replace("RavenUrl=", string.Empty);
                    }
                }
            }

            _store = new DocumentStore
            {
                Urls = new[] { ravenUrl },
                Database = database
            };

            _ = _store.Initialize();

            // Create database if it does not exist
            try
            {
                _ = _store.Maintenance.ForDatabase(_store.Database).Send(new GetStatisticsOperation());
            }
            catch (DatabaseDoesNotExistException)
            {
                try
                {
                    _ = _store.Maintenance.Server.Send(new CreateDatabaseOperation(new DatabaseRecord(database)));
                }
                catch (ConcurrencyException)
                {
                    // The database was already created before calling CreateDatabaseOperation
                }
            }
        }

        public override void Init()
        {
            using (var session = _store.OpenSession())
            {
                // StatusCount
                ClearStatusCount();

                var statusCount = new StatusCount
                {
                    PendingCount = 0,
                    RunningCount = 0,
                    DoneCount = 0,
                    FailedCount = 0,
                    WarningCount = 0,
                    DisabledCount = 0,
                    StoppedCount = 0
                };
                session.Store(statusCount);
                session.SaveChanges();

                // Entries
                ClearEntries();

                // Insert default user if necessary
                var usersCol = session.Query<User>();
                try
                {
                    if (!usersCol.Any())
                    {
                        InsertDefaultUser();
                    }
                }
                catch (Exception) // Create document if it does not exist
                {
                    InsertDefaultUser();
                }
            }
        }

        private void DeleteAll(string documentName)
        {
            lock (_padlock)
            {
                _ = _store.Operations
             .Send(new DeleteByQueryOperation(new IndexQuery
             {
                 Query = "from " + documentName
             }));
                Wait();
            }
        }

        private void Wait()
        {
            while (_store.Maintenance.ForDatabase(_store.Database).Send(new GetStatisticsOperation()).StaleIndexes.Length > 0)
            {
                Thread.Sleep(10);
            }
        }

        public override bool CheckUserWorkflow(string userId, string workflowId)
        {
            lock (_padlock)
            {
                using (var session = _store.OpenSession())
                {
                    try
                    {
                        var col = session.Query<UserWorkflow>();
                        var res = col.FirstOrDefault(uw => uw.UserId == userId && uw.WorkflowId == workflowId);
                        return res != null;
                    }
                    catch (Exception)
                    {
                        return false;
                    }
                }
            }
        }

        public override void ClearEntries()
        {
            DeleteAll("entries");
        }

        public override void ClearStatusCount()
        {
            DeleteAll("statusCounts");
        }

        public override void DecrementPendingCount()
        {
            lock (_padlock)
            {
                using (var session = _store.OpenSession())
                {
                    var col = session.Query<StatusCount>();
                    var statusCount = col.FirstOrDefault();
                    if (statusCount != null)
                    {
                        statusCount.PendingCount--;
                        session.SaveChanges();
                        Wait();
                    }
                }
            }
        }

        public override void DecrementRunningCount()
        {
            lock (_padlock)
            {
                using (var session = _store.OpenSession())
                {
                    var col = session.Query<StatusCount>();
                    var statusCount = col.FirstOrDefault();
                    if (statusCount != null)
                    {
                        statusCount.RunningCount--;
                        session.SaveChanges();
                        Wait();
                    }
                }
            }
        }

        public override void DeleteUser(string username, string password)
        {
            lock (_padlock)
            {
                using (var session = _store.OpenSession())
                {
                    var col = session.Query<User>();
                    var user = col.FirstOrDefault(u => u.Username == username);
                    if (user != null && user.Password == password)
                    {
                        session.Delete(user);
                        DeleteUserWorkflowRelationsByUserId(user.Id);
                        session.SaveChanges();
                        Wait();
                    }
                    else
                    {
                        throw new Exception("The password is incorrect.");
                    }
                }
            }
        }

        public override void DeleteUserWorkflowRelationsByUserId(string userId)
        {
            lock (_padlock)
            {
                using (var session = _store.OpenSession())
                {
                    var col = session.Query<UserWorkflow>();
                    var rels = col.Where(uw => uw.UserId == userId).ToArray();
                    foreach (var rel in rels)
                    {
                        session.Delete(rel);
                    }
                    session.SaveChanges();
                    Wait();
                }
            }
        }

        public override void DeleteUserWorkflowRelationsByWorkflowId(string workflowDbId)
        {
            lock (_padlock)
            {
                using (var session = _store.OpenSession())
                {
                    var col = session.Query<UserWorkflow>();
                    var rels = col.Where(uw => uw.WorkflowId == workflowDbId).ToArray();
                    foreach (var rel in rels)
                    {
                        session.Delete(rel);
                    }
                    session.SaveChanges();
                    Wait();
                }
            }
        }

        public override void DeleteWorkflow(string id)
        {
            lock (_padlock)
            {
                using (var session = _store.OpenSession())
                {
                    var col = session.Query<Workflow>();
                    var wf = col.FirstOrDefault(e => e.Id == id);
                    if (wf != null)
                    {
                        session.Delete(wf);
                    }
                    session.SaveChanges();
                    Wait();
                }
            }
        }

        public override void DeleteWorkflows(string[] ids)
        {
            lock (_padlock)
            {
                using (var session = _store.OpenSession())
                {
                    var col = session.Query<Workflow>();

                    foreach (var id in ids)
                    {
                        var wf = col.FirstOrDefault(w => w.Id == id);
                        if (wf != null)
                        {
                            session.Delete(wf);
                        }
                    }

                    session.SaveChanges();
                    Wait();
                }
            }
        }

        public override IEnumerable<Core.Db.User> GetAdministrators(string keyword, UserOrderBy uo)
        {
            lock (_padlock)
            {
                using (var session = _store.OpenSession())
                {
                    try
                    {
                        var col = session.Query<User>();
                        var keywordToLower = string.IsNullOrEmpty(keyword) ? "*" : "*" + keyword.ToLower() + "*";

                        switch (uo)
                        {
                            case UserOrderBy.UsernameAscending:
                                return col
                                    .Search(u => u.Username, keywordToLower)
                                    .Where(u => u.UserProfile == UserProfile.Administrator)
                                    .OrderBy(u => u.Username)
                                    .ToArray();
                            case UserOrderBy.UsernameDescending:
                                return col
                                    .Search(u => u.Username, keywordToLower)
                                    .Where(u => u.UserProfile == UserProfile.Administrator)
                                    .OrderByDescending(u => u.Username)
                                    .ToArray();

                            default:
                                break;
                        }

                        return Array.Empty<User>();
                    }
                    catch (Exception)
                    {
                        return Array.Empty<User>();
                    }
                }
            }
        }

        public override IEnumerable<Core.Db.Entry> GetEntries()
        {
            lock (_padlock)
            {
                using (var session = _store.OpenSession())
                {
                    try
                    {
                        var col = session.Query<Entry>().ToArray();
                        return col;
                    }
                    catch (Exception)
                    {
                        return Array.Empty<Entry>();
                    }
                }
            }
        }

        public override IEnumerable<Core.Db.Entry> GetEntries(string keyword, DateTime from, DateTime to, int page, int entriesCount, EntryOrderBy eo)
        {
            lock (_padlock)
            {
                using (var session = _store.OpenSession())
                {
                    try
                    {
                        var col = session.Query<Entry>();
                        var keywordToLower = string.IsNullOrEmpty(keyword) ? "*" : "*" + keyword.ToLower() + "*";
                        var skip = (page - 1) * entriesCount;

                        switch (eo)
                        {
                            case EntryOrderBy.StatusDateAscending:

                                return col
                                    .Search(e => e.Name, keywordToLower, options: SearchOptions.Or)
                                    .Search(e => e.Description, keywordToLower)
                                    .Where(e => e.StatusDate > from && e.StatusDate < to)
                                    .OrderBy(e => e.StatusDate).Skip(skip)
                                    .Take(entriesCount)
                                    .ToArray();

                            case EntryOrderBy.StatusDateDescending:

                                return col
                                    .Search(e => e.Name, keywordToLower, options: SearchOptions.Or)
                                    .Search(e => e.Description, keywordToLower)
                                    .Where(e => e.StatusDate > from && e.StatusDate < to)
                                    .OrderByDescending(e => e.StatusDate)
                                    .Skip(skip)
                                    .Take(entriesCount)
                                    .ToArray();

                            case EntryOrderBy.WorkflowIdAscending:

                                return col
                                    .Search(e => e.Name, keywordToLower, options: SearchOptions.Or)
                                    .Search(e => e.Description, keywordToLower)
                                    .Where(e => e.StatusDate > from && e.StatusDate < to)
                                    .OrderBy(e => e.WorkflowId)
                                    .Skip(skip)
                                    .Take(entriesCount)
                                    .ToArray();

                            case EntryOrderBy.WorkflowIdDescending:

                                return col
                                    .Search(e => e.Name, keywordToLower, options: SearchOptions.Or)
                                    .Search(e => e.Description, keywordToLower)
                                    .Where(e => e.StatusDate > from && e.StatusDate < to)
                                    .OrderByDescending(e => e.WorkflowId)
                                    .Skip(skip)
                                    .Take(entriesCount)
                                    .ToArray();

                            case EntryOrderBy.NameAscending:

                                return col
                                    .Search(e => e.Name, keywordToLower, options: SearchOptions.Or)
                                    .Search(e => e.Description, keywordToLower)
                                    .Where(e => e.StatusDate > from && e.StatusDate < to)
                                    .OrderBy(e => e.Name)
                                    .Skip(skip)
                                    .Take(entriesCount)
                                    .ToArray();

                            case EntryOrderBy.NameDescending:

                                return col
                                    .Search(e => e.Name, keywordToLower, options: SearchOptions.Or)
                                    .Search(e => e.Description, keywordToLower)
                                    .Where(e => e.StatusDate > from && e.StatusDate < to)
                                    .OrderByDescending(e => e.Name)
                                    .Skip(skip)
                                    .Take(entriesCount)
                                    .ToArray();

                            case EntryOrderBy.LaunchTypeAscending:

                                return col
                                    .Search(e => e.Name, keywordToLower, options: SearchOptions.Or)
                                    .Search(e => e.Description, keywordToLower)
                                    .Where(e => e.StatusDate > from && e.StatusDate < to)
                                    .OrderBy(e => e.LaunchType)
                                    .Skip(skip)
                                    .Take(entriesCount)
                                    .ToArray();

                            case EntryOrderBy.LaunchTypeDescending:

                                return col
                                    .Search(e => e.Name, keywordToLower, options: SearchOptions.Or)
                                    .Search(e => e.Description, keywordToLower)
                                    .Where(e => e.StatusDate > from && e.StatusDate < to)
                                    .OrderByDescending(e => e.LaunchType)
                                    .Skip(skip)
                                    .Take(entriesCount)
                                    .ToArray();

                            case EntryOrderBy.DescriptionAscending:

                                return col
                                    .Search(e => e.Name, keywordToLower, options: SearchOptions.Or)
                                    .Search(e => e.Description, keywordToLower)
                                    .Where(e => e.StatusDate > from && e.StatusDate < to)
                                    .OrderBy(e => e.Description)
                                    .Skip(skip)
                                    .Take(entriesCount)
                                    .ToArray();

                            case EntryOrderBy.DescriptionDescending:

                                return col
                                    .Search(e => e.Name, keywordToLower, options: SearchOptions.Or)
                                    .Search(e => e.Description, keywordToLower)
                                    .Where(e => e.StatusDate > from && e.StatusDate < to)
                                    .OrderByDescending(e => e.Description)
                                    .Skip(skip)
                                    .Take(entriesCount)
                                    .ToArray();

                            case EntryOrderBy.StatusAscending:

                                return col
                                    .Search(e => e.Name, keywordToLower, options: SearchOptions.Or)
                                    .Search(e => e.Description, keywordToLower)
                                    .Where(e => e.StatusDate > from && e.StatusDate < to)
                                    .OrderBy(e => e.Status)
                                    .Skip(skip)
                                    .Take(entriesCount)
                                    .ToArray();

                            case EntryOrderBy.StatusDescending:

                                return col
                                    .Search(e => e.Name, keywordToLower, options: SearchOptions.Or)
                                    .Search(e => e.Description, keywordToLower)
                                    .Where(e => e.StatusDate > from && e.StatusDate < to)
                                    .OrderByDescending(e => e.Status)
                                    .Skip(skip)
                                    .Take(entriesCount)
                                    .ToArray();

                            default:
                                break;
                        }

                        return Array.Empty<Entry>();
                    }
                    catch (Exception)
                    {
                        return Array.Empty<Entry>();
                    }
                }
            }
        }

        public override long GetEntriesCount(string keyword, DateTime from, DateTime to)
        {
            lock (_padlock)
            {
                using (var session = _store.OpenSession())
                {
                    try
                    {
                        var keywordToLower = string.IsNullOrEmpty(keyword) ? "*" : "*" + keyword.ToLower() + "*";
                        var col = session.Query<Entry>();

                        return col
                            .Search(e => e.Name, keywordToLower, options: SearchOptions.Or)
                            .Search(e => e.Description, keywordToLower)
                            .Count(e => e.StatusDate > from && e.StatusDate < to);
                    }
                    catch (Exception)
                    {
                        return 0;
                    }
                }
            }
        }

        public override Core.Db.Entry GetEntry(int workflowId)
        {
            lock (_padlock)
            {
                using (var session = _store.OpenSession())
                {
                    try
                    {
                        var col = session.Query<Entry>();
                        return col.Where(e => e.WorkflowId == workflowId).OrderByDescending(e => e.StatusDate).FirstOrDefault();
                    }
                    catch (Exception)
                    {
                        return null;
                    }
                }
            }
        }

        public override Core.Db.Entry GetEntry(int workflowId, Guid jobId)
        {
            lock (_padlock)
            {
                using (var session = _store.OpenSession())
                {
                    try
                    {
                        var col = session.Query<Entry>();
                        return col.FirstOrDefault(e => e.WorkflowId == workflowId && e.JobId == jobId.ToString());
                    }
                    catch (Exception)
                    {
                        return null;
                    }
                }
            }
        }

        public override DateTime GetEntryStatusDateMax()
        {
            lock (_padlock)
            {
                using (var session = _store.OpenSession())
                {
                    try
                    {
                        var col = session.Query<Entry>();
                        var q = col.OrderByDescending(e => e.StatusDate);
                        return q.Any() ? q.Select(e => e.StatusDate).First() : DateTime.Now;
                    }
                    catch (Exception)
                    {
                        return DateTime.Now;
                    }
                }
            }
        }

        public override DateTime GetEntryStatusDateMin()
        {
            lock (_padlock)
            {
                using (var session = _store.OpenSession())
                {
                    try
                    {
                        var col = session.Query<Entry>();
                        var q = col.OrderBy(e => e.StatusDate);
                        return q.Any() ? q.Select(e => e.StatusDate).First() : DateTime.Now;
                    }
                    catch (Exception)
                    {
                        return DateTime.Now;
                    }
                }
            }
        }

        public override IEnumerable<Core.Db.HistoryEntry> GetHistoryEntries()
        {
            lock (_padlock)
            {
                using (var session = _store.OpenSession())
                {
                    try
                    {
                        var col = session.Query<HistoryEntry>().ToArray();
                        return col;
                    }
                    catch (Exception)
                    {
                        return Array.Empty<HistoryEntry>();
                    }
                }
            }
        }

        public override IEnumerable<Core.Db.HistoryEntry> GetHistoryEntries(string keyword)
        {
            lock (_padlock)
            {
                using (var session = _store.OpenSession())
                {
                    try
                    {
                        var keywordToLower = string.IsNullOrEmpty(keyword) ? "*" : "*" + keyword.ToLower() + "*";
                        var col = session.Query<HistoryEntry>();
                        return col
                            .Search(e => e.Name, keywordToLower, options: SearchOptions.Or)
                            .Search(e => e.Description, keywordToLower)
                            .ToArray();
                    }
                    catch (Exception)
                    {
                        return Array.Empty<HistoryEntry>();
                    }
                }
            }
        }

        public override IEnumerable<Core.Db.HistoryEntry> GetHistoryEntries(string keyword, int page, int entriesCount)
        {
            lock (_padlock)
            {
                using (var session = _store.OpenSession())
                {
                    try
                    {
                        var keywordToLower = string.IsNullOrEmpty(keyword) ? "*" : "*" + keyword.ToLower() + "*";
                        var col = session.Query<HistoryEntry>();
                        return col
                            .Search(e => e.Name, keywordToLower, options: SearchOptions.Or)
                            .Search(e => e.Description, keywordToLower)
                            .Skip((page - 1) * entriesCount).Take(entriesCount).ToArray();
                    }
                    catch (Exception)
                    {
                        return Array.Empty<HistoryEntry>();
                    }
                }
            }
        }

        public override IEnumerable<Core.Db.HistoryEntry> GetHistoryEntries(string keyword, DateTime from, DateTime to, int page, int entriesCount, EntryOrderBy heo)
        {
            lock (_padlock)
            {
                using (var session = _store.OpenSession())
                {
                    try
                    {
                        var col = session.Query<HistoryEntry>();
                        var keywordToLower = string.IsNullOrEmpty(keyword) ? "*" : "*" + keyword.ToLower() + "*";
                        var skip = (page - 1) * entriesCount;

                        switch (heo)
                        {
                            case EntryOrderBy.StatusDateAscending:

                                return col
                                    .Search(e => e.Name, keywordToLower, options: SearchOptions.Or)
                                    .Search(e => e.Description, keywordToLower)
                                    .Where(e => e.StatusDate > from && e.StatusDate < to)
                                    .OrderBy(e => e.StatusDate).Skip(skip)
                                    .Take(entriesCount)
                                    .ToArray();

                            case EntryOrderBy.StatusDateDescending:

                                return col
                                    .Search(e => e.Name, keywordToLower, options: SearchOptions.Or)
                                    .Search(e => e.Description, keywordToLower)
                                    .Where(e => e.StatusDate > from && e.StatusDate < to)
                                    .OrderByDescending(e => e.StatusDate)
                                    .Skip(skip)
                                    .Take(entriesCount)
                                    .ToArray();

                            case EntryOrderBy.WorkflowIdAscending:

                                return col
                                    .Search(e => e.Name, keywordToLower, options: SearchOptions.Or)
                                    .Search(e => e.Description, keywordToLower)
                                    .Where(e => e.StatusDate > from && e.StatusDate < to)
                                    .OrderBy(e => e.WorkflowId)
                                    .Skip(skip)
                                    .Take(entriesCount)
                                    .ToArray();

                            case EntryOrderBy.WorkflowIdDescending:

                                return col
                                    .Search(e => e.Name, keywordToLower, options: SearchOptions.Or)
                                    .Search(e => e.Description, keywordToLower)
                                    .Where(e => e.StatusDate > from && e.StatusDate < to)
                                    .OrderByDescending(e => e.WorkflowId)
                                    .Skip(skip)
                                    .Take(entriesCount)
                                    .ToArray();

                            case EntryOrderBy.NameAscending:

                                return col
                                    .Search(e => e.Name, keywordToLower, options: SearchOptions.Or)
                                    .Search(e => e.Description, keywordToLower)
                                    .Where(e => e.StatusDate > from && e.StatusDate < to)
                                    .OrderBy(e => e.Name)
                                    .Skip(skip)
                                    .Take(entriesCount)
                                    .ToArray();

                            case EntryOrderBy.NameDescending:

                                return col
                                    .Search(e => e.Name, keywordToLower, options: SearchOptions.Or)
                                    .Search(e => e.Description, keywordToLower)
                                    .Where(e => e.StatusDate > from && e.StatusDate < to)
                                    .OrderByDescending(e => e.Name)
                                    .Skip(skip)
                                    .Take(entriesCount)
                                    .ToArray();

                            case EntryOrderBy.LaunchTypeAscending:

                                return col
                                    .Search(e => e.Name, keywordToLower, options: SearchOptions.Or)
                                    .Search(e => e.Description, keywordToLower)
                                    .Where(e => e.StatusDate > from && e.StatusDate < to)
                                    .OrderBy(e => e.LaunchType)
                                    .Skip(skip)
                                    .Take(entriesCount)
                                    .ToArray();

                            case EntryOrderBy.LaunchTypeDescending:

                                return col
                                    .Search(e => e.Name, keywordToLower, options: SearchOptions.Or)
                                    .Search(e => e.Description, keywordToLower)
                                    .Where(e => e.StatusDate > from && e.StatusDate < to)
                                    .OrderByDescending(e => e.LaunchType)
                                    .Skip(skip)
                                    .Take(entriesCount)
                                    .ToArray();

                            case EntryOrderBy.DescriptionAscending:

                                return col
                                    .Search(e => e.Name, keywordToLower, options: SearchOptions.Or)
                                    .Search(e => e.Description, keywordToLower)
                                    .Where(e => e.StatusDate > from && e.StatusDate < to)
                                    .OrderBy(e => e.Description)
                                    .Skip(skip)
                                    .Take(entriesCount)
                                    .ToArray();

                            case EntryOrderBy.DescriptionDescending:

                                return col
                                    .Search(e => e.Name, keywordToLower, options: SearchOptions.Or)
                                    .Search(e => e.Description, keywordToLower)
                                    .Where(e => e.StatusDate > from && e.StatusDate < to)
                                    .OrderByDescending(e => e.Description)
                                    .Skip(skip)
                                    .Take(entriesCount)
                                    .ToArray();

                            case EntryOrderBy.StatusAscending:

                                return col
                                    .Search(e => e.Name, keywordToLower, options: SearchOptions.Or)
                                    .Search(e => e.Description, keywordToLower)
                                    .Where(e => e.StatusDate > from && e.StatusDate < to)
                                    .OrderBy(e => e.Status)
                                    .Skip(skip)
                                    .Take(entriesCount)
                                    .ToArray();

                            case EntryOrderBy.StatusDescending:

                                return col
                                    .Search(e => e.Name, keywordToLower, options: SearchOptions.Or)
                                    .Search(e => e.Description, keywordToLower)
                                    .Where(e => e.StatusDate > from && e.StatusDate < to)
                                    .OrderByDescending(e => e.Status)
                                    .Skip(skip)
                                    .Take(entriesCount)
                                    .ToArray();

                            default:
                                break;
                        }

                        return Array.Empty<HistoryEntry>();
                    }
                    catch (Exception)
                    {
                        return Array.Empty<HistoryEntry>();
                    }
                }
            }
        }

        public override long GetHistoryEntriesCount(string keyword)
        {
            lock (_padlock)
            {
                using (var session = _store.OpenSession())
                {
                    try
                    {
                        var keywordToLower = string.IsNullOrEmpty(keyword) ? "*" : "*" + keyword.ToLower() + "*";
                        var col = session.Query<HistoryEntry>();
                        return col
                            .Search(e => e.Name, keywordToLower, options: SearchOptions.Or)
                            .Search(e => e.Description, keywordToLower)
                            .Count();
                    }
                    catch (Exception)
                    {
                        return 0;
                    }
                }
            }
        }

        public override long GetHistoryEntriesCount(string keyword, DateTime from, DateTime to)
        {
            lock (_padlock)
            {
                using (var session = _store.OpenSession())
                {
                    try
                    {
                        var keywordToLower = string.IsNullOrEmpty(keyword) ? "*" : "*" + keyword.ToLower() + "*";
                        var col = session.Query<HistoryEntry>();

                        return col
                            .Search(e => e.Name, keywordToLower, options: SearchOptions.Or)
                            .Search(e => e.Description, keywordToLower)
                            .Count(e => e.StatusDate > from && e.StatusDate < to);
                    }
                    catch (Exception)
                    {
                        return 0;
                    }
                }
            }
        }

        public override DateTime GetHistoryEntryStatusDateMax()
        {
            lock (_padlock)
            {
                using (var session = _store.OpenSession())
                {
                    try
                    {
                        var col = session.Query<HistoryEntry>();
                        var q = col.OrderByDescending(e => e.StatusDate);
                        return q.Any() ? q.Select(e => e.StatusDate).First() : DateTime.Now;
                    }
                    catch (Exception)
                    {
                        return DateTime.Now;
                    }
                }
            }
        }

        public override DateTime GetHistoryEntryStatusDateMin()
        {
            lock (_padlock)
            {
                using (var session = _store.OpenSession())
                {
                    try
                    {
                        var col = session.Query<HistoryEntry>();
                        var q = col.OrderBy(e => e.StatusDate);
                        return q.Any() ? q.Select(e => e.StatusDate).First() : DateTime.Now;
                    }
                    catch (Exception)
                    {
                        return DateTime.Now;
                    }
                }
            }
        }

        public override string GetPassword(string username)
        {
            lock (_padlock)
            {
                using (var session = _store.OpenSession())
                {
                    try
                    {
                        var col = session.Query<User>();
                        var user = col.First(u => u.Username == username);
                        return user.Password;
                    }
                    catch (Exception)
                    {
                        return null;
                    }
                }
            }
        }

        public override Core.Db.StatusCount GetStatusCount()
        {
            lock (_padlock)
            {
                using (var session = _store.OpenSession())
                {
                    try
                    {
                        var col = session.Query<StatusCount>();
                        var statusCount = col.FirstOrDefault();
                        return statusCount;
                    }
                    catch (Exception)
                    {
                        return null;
                    }
                }
            }
        }

        public override Core.Db.User GetUser(string username)
        {
            lock (_padlock)
            {
                using (var session = _store.OpenSession())
                {
                    try
                    {
                        var col = session.Query<User>();
                        var user = col.FirstOrDefault(u => u.Username == username);
                        return user;
                    }
                    catch (Exception)
                    {
                        return null;
                    }
                }
            }
        }

        public override Core.Db.User GetUserById(string userId)
        {
            lock (_padlock)
            {
                using (var session = _store.OpenSession())
                {
                    try
                    {
                        var col = session.Query<User>();
                        var user = col.FirstOrDefault(u => u.Id == userId);
                        return user;
                    }
                    catch (Exception)
                    {
                        return null;
                    }
                }
            }
        }

        public override IEnumerable<Core.Db.User> GetUsers()
        {
            lock (_padlock)
            {
                using (var session = _store.OpenSession())
                {
                    try
                    {
                        var col = session.Query<User>();
                        return col.ToArray();
                    }
                    catch (Exception)
                    {
                        return Array.Empty<User>();
                    }
                }
            }
        }

        public override IEnumerable<Core.Db.User> GetUsers(string keyword, UserOrderBy uo)
        {
            lock (_padlock)
            {
                using (var session = _store.OpenSession())
                {
                    try
                    {
                        var col = session.Query<User>();
                        var keywordToLower = string.IsNullOrEmpty(keyword) ? "*" : "*" + keyword.ToLower() + "*";

                        switch (uo)
                        {
                            case UserOrderBy.UsernameAscending:
                                return col.Search(u => u.Username, keywordToLower).OrderBy(u => u.Username).ToArray();
                            case UserOrderBy.UsernameDescending:
                                return col.Search(u => u.Username, keywordToLower).OrderByDescending(u => u.Username).ToArray();
                            default:
                                break;
                        }

                        return Array.Empty<User>();
                    }
                    catch (Exception)
                    {
                        return Array.Empty<User>();
                    }
                }
            }
        }

        public override IEnumerable<string> GetUserWorkflows(string userId)
        {
            lock (_padlock)
            {
                using (var session = _store.OpenSession())
                {
                    try
                    {
                        var col = session.Query<UserWorkflow>();
                        return col.Where(uw => uw.UserId == userId).Select(uw => uw.WorkflowId).ToArray();
                    }
                    catch (Exception)
                    {
                        return Array.Empty<string>();
                    }
                }
            }
        }

        public override Core.Db.Workflow GetWorkflow(string id)
        {
            lock (_padlock)
            {
                using (var session = _store.OpenSession())
                {
                    try
                    {
                        var col = session.Query<Workflow>();
                        return col.FirstOrDefault(w => w.Id == id);
                    }
                    catch (Exception)
                    {
                        return null;
                    }
                }
            }
        }

        public override IEnumerable<Core.Db.Workflow> GetWorkflows()
        {
            lock (_padlock)
            {
                using (var session = _store.OpenSession())
                {
                    try
                    {
                        var col = session.Query<Workflow>();
                        return col.ToArray();
                    }
                    catch (Exception)
                    {
                        return Array.Empty<Workflow>();
                    }
                }
            }
        }

        public override void IncrementDisabledCount()
        {
            lock (_padlock)
            {
                using (var session = _store.OpenSession())
                {
                    var col = session.Query<StatusCount>();
                    var statusCount = col.FirstOrDefault();
                    if (statusCount != null)
                    {
                        statusCount.DisabledCount++;
                        session.SaveChanges();
                        Wait();
                    }
                }
            }
        }

        public override void IncrementRejectedCount()
        {
            lock (_padlock)
            {
                using (var session = _store.OpenSession())
                {
                    var col = session.Query<StatusCount>();
                    var statusCount = col.FirstOrDefault();
                    if (statusCount != null)
                    {
                        statusCount.RejectedCount++;
                        session.SaveChanges();
                        Wait();
                    }
                }
            }
        }

        public override void IncrementDoneCount()
        {
            lock (_padlock)
            {
                using (var session = _store.OpenSession())
                {
                    var col = session.Query<StatusCount>();
                    var statusCount = col.FirstOrDefault();
                    if (statusCount != null)
                    {
                        statusCount.DoneCount++;
                        session.SaveChanges();
                        Wait();
                    }
                }
            }
        }

        public override void IncrementFailedCount()
        {
            lock (_padlock)
            {
                using (var session = _store.OpenSession())
                {
                    var col = session.Query<StatusCount>();
                    var statusCount = col.FirstOrDefault();
                    if (statusCount != null)
                    {
                        statusCount.FailedCount++;
                        session.SaveChanges();
                        Wait();
                    }
                }
            }
        }

        public override void IncrementPendingCount()
        {
            lock (_padlock)
            {
                using (var session = _store.OpenSession())
                {
                    var col = session.Query<StatusCount>();
                    var statusCount = col.FirstOrDefault();
                    if (statusCount != null)
                    {
                        statusCount.PendingCount++;
                        session.SaveChanges();
                        Wait();
                    }
                }
            }
        }

        public override void IncrementRunningCount()
        {
            lock (_padlock)
            {
                using (var session = _store.OpenSession())
                {
                    var col = session.Query<StatusCount>();
                    var statusCount = col.FirstOrDefault();
                    if (statusCount != null)
                    {
                        statusCount.RunningCount++;
                        session.SaveChanges();
                        Wait();
                    }
                }
            }
        }

        public override void IncrementStoppedCount()
        {
            lock (_padlock)
            {
                using (var session = _store.OpenSession())
                {
                    var col = session.Query<StatusCount>();
                    var statusCount = col.FirstOrDefault();
                    if (statusCount != null)
                    {
                        statusCount.StoppedCount++;
                        session.SaveChanges();
                        Wait();
                    }
                }
            }
        }

        public override void IncrementWarningCount()
        {
            lock (_padlock)
            {
                using (var session = _store.OpenSession())
                {
                    var col = session.Query<StatusCount>();
                    var statusCount = col.FirstOrDefault();
                    if (statusCount != null)
                    {
                        statusCount.WarningCount++;
                        session.SaveChanges();
                        Wait();
                    }
                }
            }
        }

        public override void InsertEntry(Core.Db.Entry entry)
        {
            lock (_padlock)
            {
                using (var session = _store.OpenSession())
                {
                    var ie = new Entry
                    {
                        Description = entry.Description,
                        LaunchType = entry.LaunchType,
                        Name = entry.Name,
                        Status = entry.Status,
                        StatusDate = entry.StatusDate,
                        WorkflowId = entry.WorkflowId,
                        JobId = entry.JobId,
                        Logs = entry.Logs
                    };
                    session.Store(ie);
                    session.SaveChanges();
                    Wait();
                }
            }
        }

        public override void InsertHistoryEntry(Core.Db.HistoryEntry entry)
        {
            lock (_padlock)
            {
                using (var session = _store.OpenSession())
                {
                    var he = new HistoryEntry
                    {
                        Description = entry.Description,
                        LaunchType = entry.LaunchType,
                        Name = entry.Name,
                        Status = entry.Status,
                        StatusDate = entry.StatusDate,
                        WorkflowId = entry.WorkflowId,
                        Logs = entry.Logs
                    };
                    session.Store(he);
                    session.SaveChanges();
                    Wait();
                }
            }
        }

        public override void InsertUser(Core.Db.User user)
        {
            lock (_padlock)
            {
                using (var session = _store.OpenSession())
                {
                    user.CreatedOn = DateTime.Now;
                    var nu = new User
                    {
                        CreatedOn = user.CreatedOn,
                        Email = user.Email,
                        ModifiedOn = user.ModifiedOn,
                        Password = user.Password,
                        Username = user.Username,
                        UserProfile = user.UserProfile
                    };
                    session.Store(nu);
                    session.SaveChanges();
                    Wait();
                }
            }
        }

        public override void InsertUserWorkflowRelation(Core.Db.UserWorkflow userWorkflow)
        {
            lock (_padlock)
            {
                using (var session = _store.OpenSession())
                {
                    var uw = new UserWorkflow
                    {
                        UserId = userWorkflow.UserId,
                        WorkflowId = userWorkflow.WorkflowId
                    };
                    session.Store(uw);
                    session.SaveChanges();
                    Wait();
                }
            }
        }

        public override string InsertWorkflow(Core.Db.Workflow workflow)
        {
            lock (_padlock)
            {
                using (var session = _store.OpenSession())
                {
                    var wf = new Workflow { Xml = workflow.Xml };
                    session.Store(wf);
                    session.SaveChanges();
                    Wait();
                    return wf.Id;
                }
            }
        }

        public override void UpdateEntry(string id, Core.Db.Entry entry)
        {
            lock (_padlock)
            {
                using (var session = _store.OpenSession())
                {
                    var col = session.Query<Entry>();
                    var ue = col.First(e => e.Id == id);
                    ue.Name = entry.Name;
                    ue.Description = entry.Description;
                    ue.LaunchType = entry.LaunchType;
                    ue.Status = entry.Status;
                    ue.StatusDate = entry.StatusDate;
                    ue.WorkflowId = entry.WorkflowId;
                    ue.JobId = entry.JobId;
                    ue.Logs = entry.Logs;

                    session.SaveChanges();
                    Wait();
                }
            }
        }

        public override void UpdatePassword(string username, string password)
        {
            lock (_padlock)
            {
                using (var session = _store.OpenSession())
                {
                    var col = session.Query<User>();
                    var dbUser = col.First(u => u.Username == username);
                    dbUser.Password = password;

                    session.SaveChanges();
                    Wait();
                }
            }
        }

        public override void UpdateUser(string id, Core.Db.User user)
        {
            lock (_padlock)
            {
                using (var session = _store.OpenSession())
                {
                    var col = session.Query<User>();
                    var uu = col.First(u => u.Id == id);
                    uu.ModifiedOn = DateTime.Now;
                    uu.Username = user.Username;
                    uu.Password = user.Password;
                    uu.UserProfile = user.UserProfile;
                    uu.Email = user.Email;

                    session.SaveChanges();
                    Wait();
                }
            }
        }

        public override void UpdateUsernameAndEmailAndUserProfile(string userId, string username, string email, UserProfile up)
        {
            lock (_padlock)
            {
                using (var session = _store.OpenSession())
                {
                    var col = session.Query<User>();
                    var uu = col.First(u => u.Id == userId);
                    uu.ModifiedOn = DateTime.Now;
                    uu.Username = username;
                    uu.UserProfile = up;
                    uu.Email = email;

                    session.SaveChanges();
                    Wait();
                }
            }
        }

        public override void UpdateWorkflow(string dbId, Core.Db.Workflow workflow)
        {
            lock (_padlock)
            {
                using (var session = _store.OpenSession())
                {
                    var col = session.Query<Workflow>();
                    var wf = col.First(w => w.Id == dbId);
                    wf.Xml = workflow.Xml;

                    session.SaveChanges();
                    Wait();
                }
            }
        }

        public override string GetEntryLogs(string entryId)
        {
            lock (_padlock)
            {
                using (var session = _store.OpenSession())
                {
                    var col = session.Query<Entry>();
                    var entry = col.First(e => e.Id == entryId);
                    return entry.Logs;
                }
            }
        }

        public override string GetHistoryEntryLogs(string entryId)
        {
            lock (_padlock)
            {
                using (var session = _store.OpenSession())
                {
                    var col = session.Query<HistoryEntry>();
                    var entry = col.First(e => e.Id == entryId);
                    return entry.Logs;
                }
            }
        }

        public override IEnumerable<Core.Db.User> GetNonRestricedUsers()
        {
            lock (_padlock)
            {
                using (var session = _store.OpenSession())
                {
                    var col = session.Query<User>();
                    var users = col.Where(u => u.UserProfile == UserProfile.SuperAdministrator || u.UserProfile == UserProfile.Administrator).OrderBy(u => u.Username).ToList();
                    return users;
                }
            }
        }

        public override string InsertRecord(Core.Db.Record record)
        {
            lock (_padlock)
            {
                using (var session = _store.OpenSession())
                {
                    var r = new Record
                    {
                        Approved = record.Approved,
                        AssignedOn = record.AssignedOn,
                        AssignedTo = record.AssignedTo,
                        Comments = record.Comments,
                        CreatedBy = record.CreatedBy,
                        CreatedOn = DateTime.Now,
                        Description = record.Description,
                        EndDate = record.EndDate,
                        ManagerComments = record.ManagerComments,
                        Name = record.Name,
                        StartDate = record.StartDate
                    };
                    session.Store(r);
                    session.SaveChanges();
                    Wait();
                    return r.Id;
                }
            }
        }

        public override void UpdateRecord(string recordId, Core.Db.Record record)
        {
            lock (_padlock)
            {
                using (var session = _store.OpenSession())
                {
                    var col = session.Query<Record>();
                    var recordFromDb = col.First(r => r.Id == recordId);

                    recordFromDb.Approved = record.Approved;
                    recordFromDb.AssignedOn = record.AssignedOn;
                    recordFromDb.AssignedTo = record.AssignedTo;
                    recordFromDb.Comments = record.Comments;
                    recordFromDb.CreatedBy = record.CreatedBy;
                    recordFromDb.CreatedOn = recordFromDb.CreatedOn;
                    recordFromDb.Description = record.Description;
                    recordFromDb.EndDate = record.EndDate;
                    recordFromDb.ManagerComments = record.ManagerComments;
                    recordFromDb.Name = record.Name;
                    recordFromDb.StartDate = record.StartDate;
                    recordFromDb.ModifiedBy = record.ModifiedBy;
                    recordFromDb.ModifiedOn = DateTime.Now;

                    session.SaveChanges();
                    Wait();
                }
            }
        }

        public override void DeleteRecords(string[] recordIds)
        {
            lock (_padlock)
            {
                using (var session = _store.OpenSession())
                {
                    var col = session.Query<Record>();

                    foreach (var id in recordIds)
                    {
                        var record = col.FirstOrDefault(r => r.Id == id);
                        if (record != null)
                        {
                            session.Delete(record);
                        }
                    }

                    session.SaveChanges();
                    Wait();
                }
            }
        }

        public override Core.Db.Record GetRecord(string id)
        {
            lock (_padlock)
            {
                using (var session = _store.OpenSession())
                {
                    var col = session.Query<Record>();
                    var record = col.FirstOrDefault(r => r.Id == id);
                    return record;
                }
            }
        }

        public override IEnumerable<Core.Db.Record> GetRecords(string keyword)
        {
            lock (_padlock)
            {
                using (var session = _store.OpenSession())
                {
                    var col = session.Query<Record>();
                    var keywordToLower = string.IsNullOrEmpty(keyword) ? "*" : "*" + keyword.ToLower() + "*";
                    var records = col
                        .Search(r => r.Name, keywordToLower, options: SearchOptions.Or)
                        .Search(r => r.Description, keywordToLower)
                        .OrderByDescending(r => r.CreatedOn)
                        .ToList();
                    return records;
                }
            }
        }

        public override IEnumerable<Core.Db.Record> GetRecordsCreatedBy(string createdBy)
        {
            lock (_padlock)
            {
                using (var session = _store.OpenSession())
                {
                    var col = session.Query<Record>();
                    var records = col.Where(r => r.CreatedBy == createdBy).OrderBy(r => r.Name).ToList();
                    return records;
                }
            }
        }

        public override IEnumerable<Core.Db.Record> GetRecordsCreatedByOrAssignedTo(string createdBy, string assingedTo, string keyword)
        {
            lock (_padlock)
            {
                using (var session = _store.OpenSession())
                {
                    var col = session.Query<Record>();
                    var keywordToLower = string.IsNullOrEmpty(keyword) ? "*" : "*" + keyword.ToLower() + "*";

                    var records = col
                        .Where(r => r.CreatedBy == createdBy || r.AssignedTo == assingedTo)
                        .Search(r => r.Name, keywordToLower, options: SearchOptions.Or)
                        .Search(r => r.Description, keywordToLower)
                        .OrderByDescending(r => r.CreatedOn)
                        .ToList();
                    return records;
                }
            }
        }

        public override string InsertVersion(Core.Db.Version version)
        {
            lock (_padlock)
            {
                using (var session = _store.OpenSession())
                {
                    var v = new Version
                    {
                        RecordId = version.RecordId,
                        CreatedOn = DateTime.Now,
                        FilePath = version.FilePath
                    };
                    session.Store(v);
                    session.SaveChanges();
                    Wait();
                    return v.Id;
                }
            }
        }

        public override void UpdateVersion(string versionId, Core.Db.Version version)
        {
            lock (_padlock)
            {
                using (var session = _store.OpenSession())
                {
                    var col = session.Query<Version>();
                    var versionFromDb = col.First(v => v.Id == versionId);

                    versionFromDb.RecordId = version.RecordId;
                    versionFromDb.CreatedOn = versionFromDb.CreatedOn;
                    versionFromDb.FilePath = version.FilePath;

                    session.SaveChanges();
                    Wait();
                }
            }
        }

        public override void DeleteVersions(string[] versionIds)
        {
            lock (_padlock)
            {
                using (var session = _store.OpenSession())
                {
                    var col = session.Query<Version>();

                    foreach (var id in versionIds)
                    {
                        var version = col.FirstOrDefault(v => v.Id == id);
                        if (version != null)
                        {
                            session.Delete(version);
                        }
                    }

                    session.SaveChanges();
                    Wait();
                }
            }
        }

        public override IEnumerable<Core.Db.Version> GetVersions(string recordId)
        {
            lock (_padlock)
            {
                using (var session = _store.OpenSession())
                {
                    var col = session.Query<Version>();

                    var versions = col.Where(v => v.RecordId == recordId).OrderBy(r => r.CreatedOn).ToList();
                    return versions;
                }
            }
        }

        public override Core.Db.Version GetLatestVersion(string recordId)
        {
            lock (_padlock)
            {
                using (var session = _store.OpenSession())
                {
                    var col = session.Query<Version>();

                    var version = col.Where(v => v.RecordId == recordId).OrderByDescending(r => r.CreatedOn).FirstOrDefault();
                    return version;
                }
            }
        }

        public override string InsertNotification(Core.Db.Notification notification)
        {
            lock (_padlock)
            {
                using (var session = _store.OpenSession())
                {
                    var n = new Notification
                    {
                        AssignedBy = notification.AssignedBy,
                        AssignedOn = notification.AssignedOn,
                        AssignedTo = notification.AssignedTo,
                        Message = notification.Message,
                        IsRead = notification.IsRead
                    };

                    session.Store(n);
                    session.SaveChanges();
                    Wait();
                    return n.Id;
                }
            }
        }

        public override void MarkNotificationsAsRead(string[] notificationIds)
        {
            lock (_padlock)
            {
                using (var session = _store.OpenSession())
                {
                    var col = session.Query<Notification>();

                    foreach (var id in notificationIds)
                    {
                        var notification = col.FirstOrDefault(n => n.Id == id);
                        if (notification != null)
                        {
                            notification.IsRead = true;
                        }
                    }

                    session.SaveChanges();
                    Wait();
                }
            }
        }

        public override void MarkNotificationsAsUnread(string[] notificationIds)
        {
            lock (_padlock)
            {
                using (var session = _store.OpenSession())
                {
                    var col = session.Query<Notification>();

                    foreach (var id in notificationIds)
                    {
                        var notification = col.FirstOrDefault(n => n.Id == id);
                        if (notification != null)
                        {
                            notification.IsRead = false;
                        }
                    }

                    session.SaveChanges();
                    Wait();
                }
            }
        }

        public override void DeleteNotifications(string[] notificationIds)
        {
            lock (_padlock)
            {
                using (var session = _store.OpenSession())
                {
                    var col = session.Query<Notification>();

                    foreach (var id in notificationIds)
                    {
                        var notification = col.FirstOrDefault(n => n.Id == id);
                        if (notification != null)
                        {
                            session.Delete(notification);
                        }
                    }

                    session.SaveChanges();
                    Wait();
                }
            }
        }

        public override IEnumerable<Core.Db.Notification> GetNotifications(string assignedTo, string keyword)
        {
            lock (_padlock)
            {
                using (var session = _store.OpenSession())
                {
                    var col = session.Query<Notification>();
                    var keywordToLower = string.IsNullOrEmpty(keyword) ? "*" : "*" + keyword.ToLower() + "*";
                    var notifications = col
                        .Where(n => n.AssignedTo == assignedTo)
                        .Search(n => n.Message, keywordToLower)
                        .OrderByDescending(n => n.AssignedOn)
                        .ToList();
                    return notifications;
                }
            }
        }

        public override bool HasNotifications(string assignedTo)
        {
            lock (_padlock)
            {
                using (var session = _store.OpenSession())
                {
                    var col = session.Query<Notification>();
                    var notifications = col.Where(n => n.AssignedTo == assignedTo && !n.IsRead);
                    var hasNotifications = notifications.Any();
                    return hasNotifications;
                }
            }
        }

        public override string InsertApprover(Core.Db.Approver approver)
        {
            lock (_padlock)
            {
                using (var session = _store.OpenSession())
                {
                    var a = new Approver
                    {
                        UserId = approver.UserId,
                        RecordId = approver.RecordId,
                        Approved = approver.Approved,
                        ApprovedOn = approver.ApprovedOn
                    };
                    session.Store(a);
                    session.SaveChanges();
                    Wait();
                    return a.Id;
                }
            }
        }

        public override void UpdateApprover(string approverId, Core.Db.Approver approver)
        {
            lock (_padlock)
            {
                using (var session = _store.OpenSession())
                {
                    var col = session.Query<Approver>();
                    var ua = col.First(a => a.Id == approverId);
                    ua.UserId = approver.UserId;
                    ua.RecordId = approver.RecordId;
                    ua.Approved = approver.Approved;
                    ua.ApprovedOn = approver.ApprovedOn;

                    session.SaveChanges();
                    Wait();
                }
            }
        }

        public override void DeleteApproversByRecordId(string recordId)
        {
            lock (_padlock)
            {
                using (var session = _store.OpenSession())
                {
                    var col = session.Query<Approver>();
                    var approvers = col.Where(a => a.RecordId == recordId).ToArray();
                    foreach (var approver in approvers)
                    {
                        session.Delete(approver);
                    }
                    session.SaveChanges();
                    Wait();
                }
            }
        }

        public override void DeleteApprovedApprovers(string recordId)
        {
            lock (_padlock)
            {
                using (var session = _store.OpenSession())
                {
                    var col = session.Query<Approver>();
                    var approvers = col.Where(a => a.Approved && a.RecordId == recordId).ToArray();
                    foreach (var approver in approvers)
                    {
                        session.Delete(approver);
                    }
                    session.SaveChanges();
                    Wait();
                }
            }
        }

        public override void DeleteApproversByUserId(string userId)
        {
            lock (_padlock)
            {
                using (var session = _store.OpenSession())
                {
                    var col = session.Query<Approver>();
                    var approvers = col.Where(a => a.UserId == userId).ToArray();
                    foreach (var approver in approvers)
                    {
                        session.Delete(approver);
                    }
                    session.SaveChanges();
                    Wait();
                }
            }
        }

        public override IEnumerable<Core.Db.Approver> GetApprovers(string recordId)
        {
            lock (_padlock)
            {
                using (var session = _store.OpenSession())
                {
                    var col = session.Query<Approver>();
                    return col.Where(a => a.RecordId == recordId).ToList();
                }
            }
        }

        public override void Dispose()
        {
        }
    }
}
