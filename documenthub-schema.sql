-- Document Hub — idempotent database setup script.
--
-- Applies every EF Core migration to a fresh (or partially migrated) Postgres
-- database and seeds the default administrator. Safe to run repeatedly: each
-- migration is guarded by a check against __ef_migrations_history, and the
-- admin insert is guarded by ON CONFLICT, so re-running this script only ever
-- applies what is actually missing.
--
-- This exists for the same reason the README's IIS deployment section calls
-- for it: an operator machine without the .NET SDK cannot run
-- `dotnet ef database update`, but can always paste SQL into `psql` or a
-- Portainer console.
--
-- Regenerate the migration portion whenever a new migration is added:
--
--   dotnet ef migrations script --idempotent --project server/src/DocHub.DataAccess --startup-project server/src/DocHub.Api --output documenthub-schema.sql
--
-- That command overwrites the whole file, so re-add the admin seed block
-- below (everything from "-- Default administrator" to the end) after
-- regenerating, or keep a copy of it to paste back in.


CREATE TABLE IF NOT EXISTS __ef_migrations_history (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___ef_migrations_history" PRIMARY KEY ("MigrationId")
);

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260728050307_InitialSchema') THEN
    CREATE TABLE users (
        "Id" uuid NOT NULL,
        "Name" character varying(200) NOT NULL,
        "Email" character varying(320) NOT NULL,
        "Role" character varying(32) NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_users" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260728050307_InitialSchema') THEN
    CREATE TABLE folders (
        "Id" uuid NOT NULL,
        "ParentId" uuid,
        "Name" character varying(200) NOT NULL,
        "Path" character varying(2000) NOT NULL,
        "OwnerId" uuid NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_folders" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_folders_folders_ParentId" FOREIGN KEY ("ParentId") REFERENCES folders ("Id") ON DELETE CASCADE,
        CONSTRAINT "FK_folders_users_OwnerId" FOREIGN KEY ("OwnerId") REFERENCES users ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260728050307_InitialSchema') THEN
    CREATE TABLE documents (
        "Id" uuid NOT NULL,
        "FolderId" uuid NOT NULL,
        "Title" character varying(500) NOT NULL,
        "Description" character varying(4000),
        "FileName" character varying(500) NOT NULL,
        "Extension" character varying(32) NOT NULL,
        "ContentType" character varying(200) NOT NULL,
        "SizeBytes" bigint NOT NULL,
        "StoragePath" character varying(1000) NOT NULL,
        "Version" integer NOT NULL,
        "Tags" text[] NOT NULL,
        "OwnerId" uuid NOT NULL,
        "Status" character varying(32) NOT NULL,
        "FailureReason" character varying(2000),
        "ChunkCount" integer,
        "IsStarred" boolean NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_documents" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_documents_folders_FolderId" FOREIGN KEY ("FolderId") REFERENCES folders ("Id") ON DELETE CASCADE,
        CONSTRAINT "FK_documents_users_OwnerId" FOREIGN KEY ("OwnerId") REFERENCES users ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260728050307_InitialSchema') THEN
    CREATE TABLE document_versions (
        "Id" uuid NOT NULL,
        "DocumentId" uuid NOT NULL,
        "VersionNumber" integer NOT NULL,
        "StoragePath" character varying(1000) NOT NULL,
        "SizeBytes" bigint NOT NULL,
        "Note" character varying(1000),
        "ChangedById" uuid NOT NULL,
        "ChangedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_document_versions" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_document_versions_documents_DocumentId" FOREIGN KEY ("DocumentId") REFERENCES documents ("Id") ON DELETE CASCADE,
        CONSTRAINT "FK_document_versions_users_ChangedById" FOREIGN KEY ("ChangedById") REFERENCES users ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260728050307_InitialSchema') THEN
    INSERT INTO users ("Id", "CreatedAt", "Email", "Name", "Role")
    VALUES ('00000000-0000-0000-0000-0000000000a1', TIMESTAMPTZ '2026-01-01T00:00:00+00:00', 'dev@dochub.local', 'Local Developer', 'Admin');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260728050307_InitialSchema') THEN
    CREATE INDEX "IX_document_versions_ChangedById" ON document_versions ("ChangedById");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260728050307_InitialSchema') THEN
    CREATE UNIQUE INDEX "IX_document_versions_DocumentId_VersionNumber" ON document_versions ("DocumentId", "VersionNumber");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260728050307_InitialSchema') THEN
    CREATE INDEX "IX_documents_FolderId" ON documents ("FolderId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260728050307_InitialSchema') THEN
    CREATE INDEX "IX_documents_OwnerId" ON documents ("OwnerId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260728050307_InitialSchema') THEN
    CREATE INDEX "IX_documents_Status" ON documents ("Status");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260728050307_InitialSchema') THEN
    CREATE INDEX "IX_documents_Tags" ON documents USING gin ("Tags");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260728050307_InitialSchema') THEN
    CREATE INDEX "IX_documents_UpdatedAt" ON documents ("UpdatedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260728050307_InitialSchema') THEN
    CREATE INDEX "IX_folders_OwnerId" ON folders ("OwnerId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260728050307_InitialSchema') THEN
    CREATE INDEX "IX_folders_ParentId" ON folders ("ParentId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260728050307_InitialSchema') THEN
    CREATE UNIQUE INDEX "IX_folders_ParentId_Name" ON folders ("ParentId", "Name");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260728050307_InitialSchema') THEN
    CREATE INDEX "IX_folders_Path" ON folders ("Path");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260728050307_InitialSchema') THEN
    CREATE UNIQUE INDEX "IX_users_Email" ON users ("Email");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260728050307_InitialSchema') THEN
    INSERT INTO __ef_migrations_history ("MigrationId", "ProductVersion")
    VALUES ('20260728050307_InitialSchema', '10.0.10');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260728142613_DocumentChunks') THEN
    CREATE EXTENSION IF NOT EXISTS vector;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260728142613_DocumentChunks') THEN
    CREATE TABLE document_chunks (
        "Id" uuid NOT NULL,
        "DocumentId" uuid NOT NULL,
        "Ordinal" integer NOT NULL,
        "Text" text NOT NULL,
        "SectionRef" character varying(300),
        "TokenCount" integer NOT NULL,
        "DocumentVersion" integer NOT NULL,
        "Embedding" vector(768) NOT NULL,
        "SearchVector" tsvector GENERATED ALWAYS AS (to_tsvector('english', "Text")) STORED,
        "CreatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_document_chunks" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_document_chunks_documents_DocumentId" FOREIGN KEY ("DocumentId") REFERENCES documents ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260728142613_DocumentChunks') THEN
    CREATE UNIQUE INDEX "IX_document_chunks_DocumentId_Ordinal" ON document_chunks ("DocumentId", "Ordinal");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260728142613_DocumentChunks') THEN
    CREATE INDEX "IX_document_chunks_Embedding" ON document_chunks USING hnsw ("Embedding" vector_cosine_ops);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260728142613_DocumentChunks') THEN
    CREATE INDEX "IX_document_chunks_SearchVector" ON document_chunks USING gin ("SearchVector");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260728142613_DocumentChunks') THEN
    INSERT INTO __ef_migrations_history ("MigrationId", "ProductVersion")
    VALUES ('20260728142613_DocumentChunks', '10.0.10');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260728191530_ChatSessions') THEN
    CREATE TABLE chat_sessions (
        "Id" uuid NOT NULL,
        "UserId" uuid NOT NULL,
        "Title" character varying(300) NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_chat_sessions" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_chat_sessions_users_UserId" FOREIGN KEY ("UserId") REFERENCES users ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260728191530_ChatSessions') THEN
    CREATE TABLE chat_messages (
        "Id" uuid NOT NULL,
        "SessionId" uuid NOT NULL,
        "Role" character varying(16) NOT NULL,
        "Content" text NOT NULL,
        "Citations" jsonb NOT NULL,
        "IsRefusal" boolean NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_chat_messages" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_chat_messages_chat_sessions_SessionId" FOREIGN KEY ("SessionId") REFERENCES chat_sessions ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260728191530_ChatSessions') THEN
    CREATE INDEX "IX_chat_messages_SessionId_CreatedAt" ON chat_messages ("SessionId", "CreatedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260728191530_ChatSessions') THEN
    CREATE INDEX "IX_chat_sessions_UserId_UpdatedAt" ON chat_sessions ("UserId", "UpdatedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260728191530_ChatSessions') THEN
    INSERT INTO __ef_migrations_history ("MigrationId", "ProductVersion")
    VALUES ('20260728191530_ChatSessions', '10.0.10');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260729045348_AddIdentityUserStore') THEN
    ALTER TABLE users ADD "AccessFailedCount" integer NOT NULL DEFAULT 0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260729045348_AddIdentityUserStore') THEN
    ALTER TABLE users ADD "ConcurrencyStamp" text;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260729045348_AddIdentityUserStore') THEN
    ALTER TABLE users ADD "EmailConfirmed" boolean NOT NULL DEFAULT FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260729045348_AddIdentityUserStore') THEN
    ALTER TABLE users ADD "LockoutEnabled" boolean NOT NULL DEFAULT FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260729045348_AddIdentityUserStore') THEN
    ALTER TABLE users ADD "LockoutEnd" timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260729045348_AddIdentityUserStore') THEN
    ALTER TABLE users ADD "NormalizedEmail" character varying(256);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260729045348_AddIdentityUserStore') THEN
    ALTER TABLE users ADD "NormalizedUserName" character varying(256);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260729045348_AddIdentityUserStore') THEN
    ALTER TABLE users ADD "PasswordHash" text;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260729045348_AddIdentityUserStore') THEN
    ALTER TABLE users ADD "PhoneNumber" text;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260729045348_AddIdentityUserStore') THEN
    ALTER TABLE users ADD "PhoneNumberConfirmed" boolean NOT NULL DEFAULT FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260729045348_AddIdentityUserStore') THEN
    ALTER TABLE users ADD "SecurityStamp" text;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260729045348_AddIdentityUserStore') THEN
    ALTER TABLE users ADD "TwoFactorEnabled" boolean NOT NULL DEFAULT FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260729045348_AddIdentityUserStore') THEN
    ALTER TABLE users ADD "UserName" character varying(256);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260729045348_AddIdentityUserStore') THEN
    CREATE TABLE user_claims (
        "Id" integer GENERATED BY DEFAULT AS IDENTITY,
        "UserId" uuid NOT NULL,
        "ClaimType" text,
        "ClaimValue" text,
        CONSTRAINT "PK_user_claims" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_user_claims_users_UserId" FOREIGN KEY ("UserId") REFERENCES users ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260729045348_AddIdentityUserStore') THEN
    CREATE TABLE user_logins (
        "LoginProvider" text NOT NULL,
        "ProviderKey" text NOT NULL,
        "ProviderDisplayName" text,
        "UserId" uuid NOT NULL,
        CONSTRAINT "PK_user_logins" PRIMARY KEY ("LoginProvider", "ProviderKey"),
        CONSTRAINT "FK_user_logins_users_UserId" FOREIGN KEY ("UserId") REFERENCES users ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260729045348_AddIdentityUserStore') THEN
    CREATE TABLE user_tokens (
        "UserId" uuid NOT NULL,
        "LoginProvider" text NOT NULL,
        "Name" text NOT NULL,
        "Value" text,
        CONSTRAINT "PK_user_tokens" PRIMARY KEY ("UserId", "LoginProvider", "Name"),
        CONSTRAINT "FK_user_tokens_users_UserId" FOREIGN KEY ("UserId") REFERENCES users ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260729045348_AddIdentityUserStore') THEN
    UPDATE users SET "AccessFailedCount" = 0, "ConcurrencyStamp" = '0f8c5b2d-1a44-4a1f-8c0e-3b6d7a9e2f45', "EmailConfirmed" = TRUE, "LockoutEnabled" = FALSE, "LockoutEnd" = NULL, "NormalizedEmail" = 'DEV@DOCHUB.LOCAL', "NormalizedUserName" = 'DEV@DOCHUB.LOCAL', "PasswordHash" = NULL, "PhoneNumber" = NULL, "PhoneNumberConfirmed" = FALSE, "SecurityStamp" = '5f1b0d5a-6c1e-4f27-9f4e-6d2c9d0a71b3', "TwoFactorEnabled" = FALSE, "UserName" = 'dev@dochub.local'
    WHERE "Id" = '00000000-0000-0000-0000-0000000000a1';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260729045348_AddIdentityUserStore') THEN
    CREATE INDEX "EmailIndex" ON users ("NormalizedEmail");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260729045348_AddIdentityUserStore') THEN
    CREATE UNIQUE INDEX "UserNameIndex" ON users ("NormalizedUserName");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260729045348_AddIdentityUserStore') THEN
    CREATE INDEX "IX_user_claims_UserId" ON user_claims ("UserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260729045348_AddIdentityUserStore') THEN
    CREATE INDEX "IX_user_logins_UserId" ON user_logins ("UserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260729045348_AddIdentityUserStore') THEN
    INSERT INTO __ef_migrations_history ("MigrationId", "ProductVersion")
    VALUES ('20260729045348_AddIdentityUserStore', '10.0.10');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260729101712_RepositorySourceSettings') THEN
    CREATE TABLE repository_source_settings (
        "Name" character varying(64) NOT NULL,
        "Endpoint" character varying(2000),
        "IsEnabled" boolean NOT NULL,
        "UpdatedAt" timestamp with time zone NOT NULL,
        "UpdatedById" uuid,
        CONSTRAINT "PK_repository_source_settings" PRIMARY KEY ("Name")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260729101712_RepositorySourceSettings') THEN
    INSERT INTO __ef_migrations_history ("MigrationId", "ProductVersion")
    VALUES ('20260729101712_RepositorySourceSettings', '10.0.10');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260729185636_ActivityTrail') THEN
    CREATE TABLE activity_events (
        "Id" uuid NOT NULL,
        "Type" character varying(32) NOT NULL,
        "ActorId" uuid NOT NULL,
        "Target" character varying(500) NOT NULL,
        "TargetId" uuid,
        "At" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_activity_events" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_activity_events_users_ActorId" FOREIGN KEY ("ActorId") REFERENCES users ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260729185636_ActivityTrail') THEN
    CREATE INDEX "IX_activity_events_ActorId" ON activity_events ("ActorId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260729185636_ActivityTrail') THEN
    CREATE INDEX "IX_activity_events_At" ON activity_events ("At" DESC);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260729185636_ActivityTrail') THEN
    INSERT INTO __ef_migrations_history ("MigrationId", "ProductVersion")
    VALUES ('20260729185636_ActivityTrail', '10.0.10');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260730143344_WidenCitations') THEN
    UPDATE chat_messages
    SET "Citations" = (
        SELECT COALESCE(
            jsonb_agg(
                jsonb_build_object(
                    'marker', element -> 'marker',
                    'kind', 'document',
                    'title', element -> 'documentTitle',
                    'heading', element -> 'heading',
                    'documentId', element -> 'documentId',
                    'chunkId', element -> 'chunkId',
                    'url', NULL,
                    'sourceName', 'documents'
                )
                ORDER BY (element ->> 'marker')::int
            ),
            '[]'::jsonb)
        FROM jsonb_array_elements("Citations") AS element
    )
    WHERE jsonb_typeof("Citations") = 'array'
      AND EXISTS (
          SELECT 1
          FROM jsonb_array_elements("Citations") AS probe
          WHERE probe ? 'documentTitle'
      );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260730143344_WidenCitations') THEN
    INSERT INTO __ef_migrations_history ("MigrationId", "ProductVersion")
    VALUES ('20260730143344_WidenCitations', '10.0.10');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260730193518_MessageDegradations') THEN
    ALTER TABLE chat_messages ADD "Degradations" jsonb NOT NULL DEFAULT ('[]'::jsonb);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260730193518_MessageDegradations') THEN
    INSERT INTO __ef_migrations_history ("MigrationId", "ProductVersion")
    VALUES ('20260730193518_MessageDegradations', '10.0.10');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260801102421_CitationsIndex') THEN
    CREATE INDEX "IX_chat_messages_Citations" ON chat_messages USING gin ("Citations" jsonb_path_ops);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260801102421_CitationsIndex') THEN
    INSERT INTO __ef_migrations_history ("MigrationId", "ProductVersion")
    VALUES ('20260801102421_CitationsIndex', '10.0.10');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260801115531_RepositoryServersAsData') THEN
    DELETE FROM repository_source_settings WHERE "Endpoint" IS NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260801115531_RepositoryServersAsData') THEN
    ALTER TABLE repository_source_settings ADD "CreatedAt" timestamp with time zone NOT NULL DEFAULT (now());
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260801115531_RepositoryServersAsData') THEN
    ALTER TABLE repository_source_settings ADD "Description" character varying(500) NOT NULL DEFAULT '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260801115531_RepositoryServersAsData') THEN
    ALTER TABLE repository_source_settings ADD "DisplayName" character varying(120) NOT NULL DEFAULT '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260801115531_RepositoryServersAsData') THEN
    ALTER TABLE repository_source_settings ADD "ToolName" character varying(120) NOT NULL DEFAULT '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260801115531_RepositoryServersAsData') THEN
    UPDATE repository_source_settings
    SET "DisplayName" = "Name", "CreatedAt" = "UpdatedAt"
    WHERE "DisplayName" = '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260801115531_RepositoryServersAsData') THEN
    ALTER TABLE repository_source_settings ALTER COLUMN "Endpoint" SET NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260801115531_RepositoryServersAsData') THEN
    INSERT INTO __ef_migrations_history ("MigrationId", "ProductVersion")
    VALUES ('20260801115531_RepositoryServersAsData', '10.0.10');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260801201049_DropRepositoryServerDescription') THEN
    ALTER TABLE repository_source_settings DROP COLUMN "Description";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260801201049_DropRepositoryServerDescription') THEN
    INSERT INTO __ef_migrations_history ("MigrationId", "ProductVersion")
    VALUES ('20260801201049_DropRepositoryServerDescription', '10.0.10');
    END IF;
END $EF$;
COMMIT;


-- Default administrator for a fresh deployment.
--
-- Sign in as admin@documenthub.local / documenthubadmin and change the
-- password immediately afterwards (People screen, or `seed-admin` against
-- this same email). This is a known credential committed to source control —
-- acceptable only because it is meant to be rotated on first use, the same
-- way a router ships with "admin/admin" and a nag to change it.
--
-- ON CONFLICT ("Email") DO NOTHING makes this safe to re-run: once the row
-- exists, re-running the script (e.g. after adding a migration) will not
-- clobber a password an operator has since changed.
INSERT INTO users (
    "Id", "Name", "Email", "Role", "CreatedAt",
    "AccessFailedCount", "ConcurrencyStamp", "EmailConfirmed", "LockoutEnabled", "LockoutEnd",
    "NormalizedEmail", "NormalizedUserName", "PasswordHash", "PhoneNumber", "PhoneNumberConfirmed",
    "SecurityStamp", "TwoFactorEnabled", "UserName"
)
VALUES (
    '00000000-0000-0000-0000-0000000000a2', 'Admin', 'admin@documenthub.local', 'Admin', TIMESTAMPTZ '2026-01-01T00:00:00+00:00',
    0, 'b6b6a6b2-2222-4b2f-9c1f-4c7d8b0a82c1', TRUE, FALSE, NULL,
    'ADMIN@DOCUMENTHUB.LOCAL', 'ADMIN@DOCUMENTHUB.LOCAL',
    'AQAAAAIAAYagAAAAEKf0W+JxdXjLRpQMIg2me6SCx77M4vfUOx6TQlobf4qqB8jhwilCnK8/qu9vx2Ntow==',
    NULL, FALSE, 'c7c7b7c3-3333-4c3f-9d2f-5d8e9c1b93d2', FALSE, 'admin@documenthub.local'
)
ON CONFLICT ("Email") DO NOTHING;
