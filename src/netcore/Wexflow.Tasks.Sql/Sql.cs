﻿using Microsoft.Data.SqlClient;
using MySqlConnector;
using Npgsql;
using Oracle.ManagedDataAccess.Client;
using System;
using System.Data.Common;
using System.Data.Odbc;
using System.Data.OleDb;
using System.Data.SQLite;
using System.IO;
using System.Xml.Linq;
using Teradata.Client.Provider;
using Wexflow.Core;

namespace Wexflow.Tasks.Sql
{
    public enum Type
    {
        SqlServer,
        Access,
        Oracle,
        MySql,
        Sqlite,
        PostGreSql,
        Teradata,
        Odbc
    }

    public class Sql : Task
    {
        public Type DbType { get; set; }
        public string ConnectionString { get; set; }
        public string SqlScript { get; set; }

        public Sql(XElement xe, Workflow wf)
            : base(xe, wf)
        {
            DbType = Enum.Parse<Type>(GetSetting("type"), true);
            ConnectionString = GetSetting("connectionString");
            SqlScript = GetSetting("sql", string.Empty);
        }

        public override TaskStatus Run()
        {
            Workflow.CancellationTokenSource.Token.ThrowIfCancellationRequested();
            Info("Executing SQL scripts...");

            var success = true;
            var atLeastOneSucceed = false;

            // Execute SqlScript if necessary
            try
            {
                if (!string.IsNullOrEmpty(SqlScript))
                {
                    ExecuteSql(SqlScript);
                    Info("The script has been executed through the sql option of the task.");
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                ErrorFormat("An error occured while executing sql script. Error: {0}", e.Message);
                success = false;
            }
            finally
            {
                if (!Workflow.CancellationTokenSource.Token.IsCancellationRequested)
                {
                    WaitOne();
                }
            }

            // Execute SQL files scripts
            foreach (var file in SelectFiles())
            {
                try
                {
                    Workflow.CancellationTokenSource.Token.ThrowIfCancellationRequested();
                    var sql = File.ReadAllText(file.Path);
                    ExecuteSql(sql);
                    InfoFormat("The script {0} has been executed.", file.Path);

                    if (!atLeastOneSucceed)
                    {
                        atLeastOneSucceed = true;
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception e)
                {
                    ErrorFormat("An error occured while executing sql script {0}. Error: {1}", file.Path, e.Message);
                    success = false;
                }
                finally
                {
                    if (!Workflow.CancellationTokenSource.Token.IsCancellationRequested)
                    {
                        WaitOne();
                    }
                }
            }

            var status = Status.Success;

            if (!success && atLeastOneSucceed)
            {
                status = Status.Warning;
            }
            else if (!success)
            {
                status = Status.Error;
            }

            Info("Task finished.");
            return new TaskStatus(status, false);
        }

        private void ExecuteSql(string sql)
        {
            switch (DbType)
            {
                case Type.SqlServer:
                    using (SqlConnection conn = new(ConnectionString))
                    {
                        SqlCommand comm = new(sql, conn);
                        ExecSql(conn, comm);
                    }
                    break;
                case Type.Access:
#pragma warning disable CA1416
                    using (OleDbConnection conn = new(ConnectionString))
                    {
                        OleDbCommand comm = new(sql, conn);
                        ExecSql(conn, comm);
                    }
#pragma warning restore CA1416
                    break;
                case Type.Oracle:
                    using (OracleConnection conn = new(ConnectionString))
                    {
                        OracleCommand comm = new(sql, conn);
                        ExecSql(conn, comm);
                    }
                    break;
                case Type.MySql:
                    using (MySqlConnection conn = new(ConnectionString))
                    {
                        MySqlCommand comm = new(sql, conn);
                        ExecSql(conn, comm);
                    }
                    break;
                case Type.Sqlite:
                    using (SQLiteConnection conn = new(ConnectionString))
                    {
                        SQLiteCommand comm = new(sql, conn);
                        ExecSql(conn, comm);
                    }
                    break;
                case Type.PostGreSql:
                    using (NpgsqlConnection conn = new(ConnectionString))
                    {
                        NpgsqlCommand comm = new(sql, conn);
                        ExecSql(conn, comm);
                    }
                    break;
                case Type.Teradata:
                    using (TdConnection conn = new(ConnectionString))
                    {
                        TdCommand comm = new(sql, conn);
                        ExecSql(conn, comm);
                    }
                    break;
                case Type.Odbc:
                    using (OdbcConnection conn = new(ConnectionString))
                    {
                        OdbcCommand comm = new(sql, conn);
                        ExecSql(conn, comm);
                    }
                    break;
                default:
                    break;
            }
        }

        private static void ExecSql(DbConnection conn, DbCommand comm)
        {
            conn.Open();
            _ = comm.ExecuteNonQuery();
        }
    }
}
