﻿using System;
using System.IO;
using System.Threading;
using System.Xml.Linq;
using Wexflow.Core;

namespace Wexflow.Tasks.Touch
{
    public class Touch : Task
    {
        public string[] Tfiles { get; }

        public Touch(XElement xe, Workflow wf)
            : base(xe, wf)
        {
            Tfiles = GetSettings("file");
        }

        public override TaskStatus Run()
        {
            Workflow.CancellationTokenSource.Token.ThrowIfCancellationRequested();
            Info("Touching files...");

            var success = true;
            var atLeastOneSucceed = false;

            foreach (var file in Tfiles)
            {
                try
                {
                    Workflow.CancellationTokenSource.Token.ThrowIfCancellationRequested();
                    TouchFile(file);
                    InfoFormat("File {0} created.", file);
                    Files.Add(new FileInf(file, Id));

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
                    ErrorFormat("An error occured while creating the file {0}", e, file);
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

        private static void TouchFile(string file)
        {
            using (File.Create(file)) { }
        }
    }
}
