using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DocHub.DataAccess.Migrations
{
    /// <summary>
    /// Rewrites stored citations into the shape that can also describe a passage
    /// from outside the hub.
    ///
    /// No column changes: <c>Citations</c> was already jsonb, and only the
    /// object inside it moved. That is exactly why this migration has to be
    /// written by hand — EF compares the model, sees jsonb on both sides and
    /// generates nothing, while every historical answer would quietly
    /// deserialize with a null title.
    ///
    /// Keeping those rows readable is not housekeeping. A citation records what
    /// an answer claimed at the time, and an answer whose sources have gone
    /// blank is worse than no history at all.
    /// </summary>
    public partial class WidenCitations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Every citation written before this point came from the document
            // source — the repository source was a stub that returned nothing —
            // so "document" and "documents" are the honest values, not guesses.
            //
            // The EXISTS guard makes this re-runnable: the idempotent setup
            // script may reach a database that is already migrated, and a second
            // pass must not turn a converted row, which has no "documentTitle"
            // left, into one with a null title.
            migrationBuilder.Sql("""
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
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // External citations cannot be represented in the old shape, so they
            // are dropped rather than written back as documents pointing at a
            // null id — a dead link in a historical answer is a worse outcome
            // than a missing one.
            migrationBuilder.Sql("""
                UPDATE chat_messages
                SET "Citations" = (
                    SELECT COALESCE(
                        jsonb_agg(
                            jsonb_build_object(
                                'marker', element -> 'marker',
                                'documentId', element -> 'documentId',
                                'documentTitle', element -> 'title',
                                'chunkId', element -> 'chunkId',
                                'heading', element -> 'heading'
                            )
                            ORDER BY (element ->> 'marker')::int
                        ),
                        '[]'::jsonb)
                    FROM jsonb_array_elements("Citations") AS element
                    WHERE element ->> 'kind' = 'document'
                )
                WHERE jsonb_typeof("Citations") = 'array'
                  AND EXISTS (
                      SELECT 1
                      FROM jsonb_array_elements("Citations") AS probe
                      WHERE probe ? 'kind'
                  );
                """);
        }
    }
}
