using JuNotes.Backend.Assets;

namespace JuNotes.Backend.Tests
{
    public sealed class AssetServiceTests : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "junotes-assets-" + Guid.NewGuid().ToString("N"));

        private AssetService Service() => new(new AssetOptions { Directory = _directory });

        public void Dispose()
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }

        private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0, 16, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0, 1, 1, 0, 0, 1, 0, 1];
        private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13];
        private static readonly byte[] Pdf = "%PDF-1.7\n1 0 obj\n"u8.ToArray();

        [Fact]
        public async Task A_saved_file_comes_back_with_its_real_type()
        {
            var service = Service();
            var id = Guid.NewGuid();

            await service.SaveAsync(id, new MemoryStream(Jpeg), CancellationToken.None);

            Assert.True(service.Exists(id));
            var (stream, type) = service.Open(id)!.Value;
            await using (stream)
            {
                Assert.Equal("image/jpeg", type);
                var copy = new MemoryStream();
                await stream.CopyToAsync(copy);
                Assert.Equal(Jpeg, copy.ToArray());
            }
        }

        [Theory]
        [InlineData("png")]
        [InlineData("pdf")]
        public async Task Png_and_pdf_are_accepted(string kind)
        {
            var service = Service();
            var id = Guid.NewGuid();

            await service.SaveAsync(id, new MemoryStream(kind == "png" ? Png : Pdf), CancellationToken.None);

            var (stream, type) = service.Open(id)!.Value;
            await stream.DisposeAsync();
            Assert.Equal(kind == "png" ? "image/png" : "application/pdf", type);
        }

        [Fact]
        public async Task Anything_else_is_refused_whatever_it_claims_to_be()
        {
            var error = await Assert.ThrowsAsync<AssetException>(() => Service().SaveAsync(Guid.NewGuid(), new MemoryStream("<html>script</html>"u8.ToArray()), CancellationToken.None));

            Assert.Equal(415, error.StatusCode);
            Assert.False(Directory.Exists(_directory) && Directory.GetFiles(_directory).Length > 0);
        }

        [Fact]
        public async Task An_empty_upload_is_refused()
        {
            var error = await Assert.ThrowsAsync<AssetException>(() => Service().SaveAsync(Guid.NewGuid(), new MemoryStream(), CancellationToken.None));

            Assert.Equal(400, error.StatusCode);
        }

        [Fact]
        public async Task A_file_over_twenty_megabytes_is_refused_and_leaves_nothing()
        {
            var big = new MemoryStream();
            big.Write(Jpeg);
            big.Write(new byte[AssetService.MaxBytes]);
            big.Position = 0;

            var error = await Assert.ThrowsAsync<AssetException>(() => Service().SaveAsync(Guid.NewGuid(), big, CancellationToken.None));

            Assert.Equal(413, error.StatusCode);
            Assert.Empty(Directory.GetFiles(_directory));
        }

        [Fact]
        public async Task The_same_id_uploaded_again_replaces_the_file()
        {
            var service = Service();
            var id = Guid.NewGuid();
            await service.SaveAsync(id, new MemoryStream(Jpeg), CancellationToken.None);
            await service.SaveAsync(id, new MemoryStream(Png), CancellationToken.None);

            var (stream, type) = service.Open(id)!.Value;
            await stream.DisposeAsync();
            Assert.Equal("image/png", type);
            Assert.Single(Directory.GetFiles(_directory));
        }

        [Fact]
        public async Task A_missing_asset_is_not_found_and_the_empty_id_is_refused()
        {
            var service = Service();

            Assert.Null(service.Open(Guid.NewGuid()));
            Assert.False(service.Exists(Guid.NewGuid()));
            await Assert.ThrowsAsync<AssetException>(() => service.SaveAsync(Guid.Empty, new MemoryStream(Jpeg), CancellationToken.None));
        }
    }
}
