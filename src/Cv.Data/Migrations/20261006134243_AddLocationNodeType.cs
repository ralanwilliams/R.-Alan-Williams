using Microsoft.EntityFrameworkCore.Migrations;

namespace Cv.Data.Migrations;

/// <summary>
/// Adds the <c>location</c> node type: where an entry took place ("Haugesund, Norway").
/// It is translatable ("Norway" is "Norge" in nb), unlike the organisation name in the entry's
/// <c>subtitle</c>, which is usually the same in every language. See ADR 0002 §11.
/// Grammar is seed data, so this is reviewed SQL rather than a model change.
/// </summary>
public partial class AddLocationNodeType : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(
            """
            INSERT INTO cv.node_types (code, description, has_text) VALUES
                ('location', 'Where an entry took place, e.g. a city and country.', true);

            INSERT INTO cv.node_type_children (parent_type, child_type) VALUES
                ('entry', 'location');

            UPDATE cv.node_types
               SET description = 'The organisation of an entry: employer, school or client.'
             WHERE code = 'subtitle';
            """);

    // Fails (by design) if any version uses a location node: history is never rewritten.
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(
            """
            UPDATE cv.node_types
               SET description = 'Organisation and place under an entry.'
             WHERE code = 'subtitle';

            DELETE FROM cv.node_type_children WHERE parent_type = 'entry' AND child_type = 'location';
            DELETE FROM cv.node_types WHERE code = 'location';
            """);
}
