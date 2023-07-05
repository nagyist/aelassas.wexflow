﻿namespace Wexflow.Core.Db.MariaDB
{
    public class Entry : Core.Db.Entry
    {
        public const string COLUMN_NAME_ID = "ID";
        public const string COLUMN_NAME_NAME = "NAME";
        public const string COLUMN_NAME_DESCRIPTION = "DESCRIPTION";
        public const string COLUMN_NAME_LAUNCH_TYPE = "LAUNCH_TYPE";
        public const string COLUMN_NAME_STATUS_DATE = "STATUS_DATE";
        public const string COLUMN_NAME_STATUS = "STATUS";
        public const string COLUMN_NAME_WORKFLOW_ID = "WORKFLOW_ID";
        public const string COLUMN_NAME_JOB_ID = "JOB_ID";
        public const string COLUMN_NAME_LOGS = "LOGS";

        public const string TableStruct = "(" + COLUMN_NAME_ID + " INT NOT NULL AUTO_INCREMENT, " + COLUMN_NAME_NAME + " VARCHAR(255), " + COLUMN_NAME_DESCRIPTION + " VARCHAR(255), " + COLUMN_NAME_LAUNCH_TYPE + " INT, " + COLUMN_NAME_STATUS_DATE + " TIMESTAMP, " + COLUMN_NAME_STATUS + " INT, " + COLUMN_NAME_WORKFLOW_ID + " INT, " + COLUMN_NAME_JOB_ID + " VARCHAR(255), " + COLUMN_NAME_LOGS + " LONGTEXT, CONSTRAINT " + DocumentName + "_pk PRIMARY KEY (" + COLUMN_NAME_ID + "))";

        public int Id { get; set; }

        public override string GetDbId()
        {
            return Id.ToString();
        }
    }
}
