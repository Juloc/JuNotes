using JuNotes.Backend.Database;

namespace JuNotes.Backend.Tests
{
    public class MigrationSetTests
    {
        private static MigrationSet Of(params string[] files) => MigrationSet.FromNames(files.Select(file => (file, file)), name => "-- " + name);

        [Fact]
        public void Orders_by_number_and_reports_the_latest()
        {
            var set = Of("0002_notebooks.sql", "0001_pages.sql");

            Assert.Equal([1, 2], set.Migrations.Select(item => item.Number));
            Assert.Equal(2, set.Latest);
            Assert.Equal("pages", set.Migrations[0].Name);
            Assert.Equal("-- 0001_pages.sql", set.Migrations[0].Sql());
        }

        [Fact]
        public void No_migrations_is_version_zero() => Assert.Equal(0, Of().Latest);

        [Fact]
        public void A_gap_is_rejected() => Assert.Throws<InvalidOperationException>(() => Of("0001_a.sql", "0003_b.sql"));

        [Fact]
        public void A_duplicate_is_rejected() => Assert.Throws<InvalidOperationException>(() => Of("0001_a.sql", "0001_b.sql"));

        [Fact]
        public void Numbering_must_start_at_one() => Assert.Throws<InvalidOperationException>(() => Of("0002_a.sql"));

        [Theory]
        [InlineData("1_a.sql")]
        [InlineData("0001-a.sql")]
        [InlineData("0001_A.sql")]
        [InlineData("0001_a.txt")]
        public void A_badly_named_file_is_rejected(string file) => Assert.Throws<InvalidOperationException>(() => Of(file));

        [Fact]
        public void The_shipped_migrations_are_consistent() => Assert.True(MigrationSet.FromAssembly(typeof(MigrationSet).Assembly).Latest >= 0);
    }
}
