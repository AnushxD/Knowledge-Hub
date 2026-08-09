-- Document Hub V2 — idempotent database setup script.
--
-- Applies the V2 schema to a fresh (or partially migrated) Postgres database
-- and seeds the default administrator. Safe to run repeatedly: each migration
-- is guarded by a check against __ef_migrations_history, and the admin insert
-- is guarded by ON CONFLICT, so re-running this script only ever applies what
-- is actually missing.
--
-- This is for the V2 database — `documenthub_v2` by convention — and must not
-- be run against a V1 one. The two share no migration history: V2 restarted the
-- chain, so the guards here would find none of V1's migration ids recorded and
-- try to create tables that already exist under a different shape. V1's own
-- script is `documenthub-schema.sql`, frozen at release 1 and never regenerated.
--
-- Create the database first — the API never does, in keeping with this
-- project's rule that provisioning is explicit:
--
--   createdb -U documenthub documenthub_v2
--
-- This exists for the same reason the README's IIS deployment section calls
-- for it: an operator machine without the .NET SDK cannot run
-- `dotnet ef database update`, but can always paste SQL into `psql` or a
-- Portainer console.
--
-- Regenerate the migration portion whenever a new migration is added:
--
--   dotnet ef migrations script --idempotent --project server/src/DocHub.DataAccess --startup-project server/src/DocHub.Api --output documenthub-v2-schema.sql
--
-- That command overwrites the whole file, so re-add the header above and the
-- admin seed block below after regenerating — and strip the byte-order mark it
-- writes at the top. psql does not skip a BOM: it reads it as part of the first
-- statement and fails with `syntax error at or near "CREATE"`, which is a
-- baffling thing to hit on a server with no SDK and no way to regenerate the
-- file.
--
--   perl -i -pe 's/^\x{ef}\x{bb}\x{bf}//' documenthub-v2-schema.sql


CREATE TABLE IF NOT EXISTS __ef_migrations_history (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___ef_migrations_history" PRIMARY KEY ("MigrationId")
);

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260809104919_InitialSchemaV2') THEN
    CREATE EXTENSION IF NOT EXISTS vector;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260809104919_InitialSchemaV2') THEN
    CREATE TABLE folders (
        "Id" uuid NOT NULL,
        "ParentId" uuid,
        "Name" character varying(200) NOT NULL,
        "Path" character varying(2000) NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_folders" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_folders_folders_ParentId" FOREIGN KEY ("ParentId") REFERENCES folders ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260809104919_InitialSchemaV2') THEN
    CREATE TABLE repository_source_settings (
        "Name" character varying(64) NOT NULL,
        "DisplayName" character varying(120) NOT NULL,
        "Endpoint" character varying(2000) NOT NULL,
        "ToolName" character varying(120) NOT NULL,
        "IsEnabled" boolean NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone NOT NULL,
        "UpdatedById" uuid,
        CONSTRAINT "PK_repository_source_settings" PRIMARY KEY ("Name")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260809104919_InitialSchemaV2') THEN
    CREATE TABLE repository_sync_state (
        "ProjectPath" character varying(500) NOT NULL,
        "Branch" character varying(300) NOT NULL,
        "Outcome" character varying(16) NOT NULL,
        "CommitSha" character varying(64),
        "StartedAt" timestamp with time zone NOT NULL,
        "FinishedAt" timestamp with time zone,
        "Error" character varying(2000),
        "FilesAdded" integer NOT NULL,
        "FilesUpdated" integer NOT NULL,
        "FilesRemoved" integer NOT NULL,
        "FilesSkipped" integer NOT NULL,
        CONSTRAINT "PK_repository_sync_state" PRIMARY KEY ("ProjectPath", "Branch")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260809104919_InitialSchemaV2') THEN
    CREATE TABLE users (
        "Id" uuid NOT NULL,
        "Name" character varying(200) NOT NULL,
        "Role" character varying(32) NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UserName" character varying(256),
        "NormalizedUserName" character varying(256),
        "Email" character varying(320) NOT NULL,
        "NormalizedEmail" character varying(256),
        "EmailConfirmed" boolean NOT NULL,
        "PasswordHash" text,
        "SecurityStamp" text,
        "ConcurrencyStamp" text,
        "PhoneNumber" text,
        "PhoneNumberConfirmed" boolean NOT NULL,
        "TwoFactorEnabled" boolean NOT NULL,
        "LockoutEnd" timestamp with time zone,
        "LockoutEnabled" boolean NOT NULL,
        "AccessFailedCount" integer NOT NULL,
        CONSTRAINT "PK_users" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260809104919_InitialSchemaV2') THEN
    CREATE TABLE documents (
        "Id" uuid NOT NULL,
        "FolderId" uuid NOT NULL,
        "RepositoryPath" character varying(2000) NOT NULL,
        "BlobSha" character varying(64) NOT NULL,
        "CommitSha" character varying(64),
        "Title" character varying(500) NOT NULL,
        "Description" character varying(4000),
        "FileName" character varying(500) NOT NULL,
        "Extension" character varying(32) NOT NULL,
        "ContentType" character varying(200) NOT NULL,
        "SizeBytes" bigint NOT NULL,
        "Tags" text[] NOT NULL,
        "Status" character varying(32) NOT NULL,
        "FailureReason" character varying(2000),
        "ChunkCount" integer,
        "IsStarred" boolean NOT NULL,
        "LastSyncedAt" timestamp with time zone NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_documents" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_documents_folders_FolderId" FOREIGN KEY ("FolderId") REFERENCES folders ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260809104919_InitialSchemaV2') THEN
    CREATE TABLE activity_events (
        "Id" uuid NOT NULL,
        "Type" character varying(32) NOT NULL,
        "ActorId" uuid,
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
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260809104919_InitialSchemaV2') THEN
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
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260809104919_InitialSchemaV2') THEN
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
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260809104919_InitialSchemaV2') THEN
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
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260809104919_InitialSchemaV2') THEN
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
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260809104919_InitialSchemaV2') THEN
    CREATE TABLE document_chunks (
        "Id" uuid NOT NULL,
        "DocumentId" uuid NOT NULL,
        "Ordinal" integer NOT NULL,
        "Text" text NOT NULL,
        "SectionRef" character varying(300),
        "TokenCount" integer NOT NULL,
        "SourceBlobSha" character varying(64) NOT NULL,
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
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260809104919_InitialSchemaV2') THEN
    CREATE TABLE chat_messages (
        "Id" uuid NOT NULL,
        "SessionId" uuid NOT NULL,
        "Role" character varying(16) NOT NULL,
        "Content" text NOT NULL,
        "Citations" jsonb NOT NULL,
        "Degradations" jsonb NOT NULL DEFAULT ('[]'::jsonb),
        "SourcesWithoutMatches" jsonb NOT NULL DEFAULT ('[]'::jsonb),
        "IsRefusal" boolean NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_chat_messages" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_chat_messages_chat_sessions_SessionId" FOREIGN KEY ("SessionId") REFERENCES chat_sessions ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260809104919_InitialSchemaV2') THEN
    INSERT INTO users ("Id", "AccessFailedCount", "ConcurrencyStamp", "CreatedAt", "Email", "EmailConfirmed", "LockoutEnabled", "LockoutEnd", "Name", "NormalizedEmail", "NormalizedUserName", "PasswordHash", "PhoneNumber", "PhoneNumberConfirmed", "Role", "SecurityStamp", "TwoFactorEnabled", "UserName")
    VALUES ('00000000-0000-0000-0000-0000000000a1', 0, '0f8c5b2d-1a44-4a1f-8c0e-3b6d7a9e2f45', TIMESTAMPTZ '2026-01-01T00:00:00+00:00', 'dev@dochub.local', TRUE, FALSE, NULL, 'Local Developer', 'DEV@DOCHUB.LOCAL', 'DEV@DOCHUB.LOCAL', NULL, NULL, FALSE, 'Admin', '5f1b0d5a-6c1e-4f27-9f4e-6d2c9d0a71b3', FALSE, 'dev@dochub.local');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260809104919_InitialSchemaV2') THEN
    CREATE INDEX "IX_activity_events_ActorId" ON activity_events ("ActorId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260809104919_InitialSchemaV2') THEN
    CREATE INDEX "IX_activity_events_At" ON activity_events ("At" DESC);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260809104919_InitialSchemaV2') THEN
    CREATE INDEX "IX_chat_messages_Citations" ON chat_messages USING gin ("Citations" jsonb_path_ops);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260809104919_InitialSchemaV2') THEN
    CREATE INDEX "IX_chat_messages_SessionId_CreatedAt" ON chat_messages ("SessionId", "CreatedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260809104919_InitialSchemaV2') THEN
    CREATE INDEX "IX_chat_sessions_UserId_UpdatedAt" ON chat_sessions ("UserId", "UpdatedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260809104919_InitialSchemaV2') THEN
    CREATE UNIQUE INDEX "IX_document_chunks_DocumentId_Ordinal" ON document_chunks ("DocumentId", "Ordinal");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260809104919_InitialSchemaV2') THEN
    CREATE INDEX "IX_document_chunks_Embedding" ON document_chunks USING hnsw ("Embedding" vector_cosine_ops);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260809104919_InitialSchemaV2') THEN
    CREATE INDEX "IX_document_chunks_SearchVector" ON document_chunks USING gin ("SearchVector");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260809104919_InitialSchemaV2') THEN
    CREATE INDEX "IX_documents_FolderId" ON documents ("FolderId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260809104919_InitialSchemaV2') THEN
    CREATE UNIQUE INDEX "IX_documents_RepositoryPath" ON documents ("RepositoryPath");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260809104919_InitialSchemaV2') THEN
    CREATE INDEX "IX_documents_Status" ON documents ("Status");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260809104919_InitialSchemaV2') THEN
    CREATE INDEX "IX_documents_Tags" ON documents USING gin ("Tags");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260809104919_InitialSchemaV2') THEN
    CREATE INDEX "IX_documents_UpdatedAt" ON documents ("UpdatedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260809104919_InitialSchemaV2') THEN
    CREATE INDEX "IX_folders_ParentId" ON folders ("ParentId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260809104919_InitialSchemaV2') THEN
    CREATE UNIQUE INDEX "IX_folders_ParentId_Name" ON folders ("ParentId", "Name");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260809104919_InitialSchemaV2') THEN
    CREATE UNIQUE INDEX "IX_folders_Path" ON folders ("Path");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260809104919_InitialSchemaV2') THEN
    CREATE INDEX "IX_user_claims_UserId" ON user_claims ("UserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260809104919_InitialSchemaV2') THEN
    CREATE INDEX "IX_user_logins_UserId" ON user_logins ("UserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260809104919_InitialSchemaV2') THEN
    CREATE INDEX "EmailIndex" ON users ("NormalizedEmail");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260809104919_InitialSchemaV2') THEN
    CREATE UNIQUE INDEX "IX_users_Email" ON users ("Email");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260809104919_InitialSchemaV2') THEN
    CREATE UNIQUE INDEX "UserNameIndex" ON users ("NormalizedUserName");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260809104919_InitialSchemaV2') THEN
    INSERT INTO __ef_migrations_history ("MigrationId", "ProductVersion")
    VALUES ('20260809104919_InitialSchemaV2', '10.0.10');
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
