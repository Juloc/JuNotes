using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace JuNotes.Backend.Backup
{
    public sealed class BackupDecryptionException(string message) : Exception(message);

    // Files that leave the server are encrypted first: AES-256-GCM in chunks of one megabyte, the key from the passphrase with PBKDF2. Every chunk is
    // authenticated, and the last one is marked, so a flipped byte, a cut-off file or swapped chunks are noticed instead of read as data.
    //
    // Layout: "JNBK1" | salt (16) | nonce prefix (4) | then per chunk: length (4) | ciphertext | tag (16). The nonce of a chunk is the prefix and its number.
    public static class BackupCrypto
    {
        public const int ChunkSize = 1024 * 1024;
        private const int Iterations = 600_000;
        private const int SaltSize = 16;
        private const int PrefixSize = 4;
        private const int TagSize = 16;
        private static readonly byte[] Magic = "JNBK1"u8.ToArray();

        public static async Task EncryptAsync(string inputPath, string outputPath, string passphrase, CancellationToken ct)
        {
            var salt = RandomNumberGenerator.GetBytes(SaltSize);
            var prefix = RandomNumberGenerator.GetBytes(PrefixSize);
            var key = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(passphrase), salt, Iterations, HashAlgorithmName.SHA256, 32);
            using var aes = new AesGcm(key, TagSize);

            await using var input = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            await using var output = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
            await output.WriteAsync(Magic, ct);
            await output.WriteAsync(salt, ct);
            await output.WriteAsync(prefix, ct);

            var buffer = new byte[ChunkSize];
            var next = new byte[ChunkSize];
            var read = await ReadFullAsync(input, buffer, ct);
            uint counter = 0;
            do
            {
                // One chunk is read ahead to know whether this one is the last.
                var following = await ReadFullAsync(input, next, ct);
                var last = following == 0;
                var cipher = new byte[read];
                var tag = new byte[TagSize];
                aes.Encrypt(Nonce(prefix, counter), buffer.AsSpan(0, read), cipher, tag, Associated(counter, last));

                var length = new byte[4];
                BinaryPrimitives.WriteInt32BigEndian(length, read);
                await output.WriteAsync(length, ct);
                await output.WriteAsync(cipher, ct);
                await output.WriteAsync(tag, ct);

                (buffer, next) = (next, buffer);
                read = following;
                counter++;
                if (last)
                {
                    break;
                }
            }
            while (true);
        }

        public static async Task DecryptAsync(string inputPath, string outputPath, string passphrase, CancellationToken ct)
        {
            await using var input = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var header = new byte[Magic.Length + SaltSize + PrefixSize];
            if (await ReadFullAsync(input, header, ct) != header.Length || !header.AsSpan(0, Magic.Length).SequenceEqual(Magic))
            {
                throw new BackupDecryptionException("This is not an encrypted JuNotes backup.");
            }

            var salt = header.AsSpan(Magic.Length, SaltSize).ToArray();
            var prefix = header.AsSpan(Magic.Length + SaltSize, PrefixSize).ToArray();
            var key = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(passphrase), salt, Iterations, HashAlgorithmName.SHA256, 32);
            using var aes = new AesGcm(key, TagSize);

            // Written under another name and moved when it is complete and verified to the end.
            var partial = outputPath + ".partial";
            try
            {
                await using (var output = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    uint counter = 0;
                    var length = new byte[4];
                    var sawLast = false;
                    while (!sawLast)
                    {
                        if (await ReadFullAsync(input, length, ct) != 4)
                        {
                            throw new BackupDecryptionException("The file is cut off.");
                        }

                        var size = BinaryPrimitives.ReadInt32BigEndian(length);
                        if (size < 0 || size > ChunkSize)
                        {
                            throw new BackupDecryptionException("The file is damaged.");
                        }

                        var cipher = new byte[size];
                        var tag = new byte[TagSize];
                        if (await ReadFullAsync(input, cipher, ct) != size || await ReadFullAsync(input, tag, ct) != TagSize)
                        {
                            throw new BackupDecryptionException("The file is cut off.");
                        }

                        // Whether this was the last chunk is told by the end of the file; the tag only fits if the writer said the same.
                        sawLast = input.Position == input.Length;
                        var plain = new byte[size];
                        try
                        {
                            aes.Decrypt(Nonce(prefix, counter), cipher, tag, plain, Associated(counter, sawLast));
                        }
                        catch (CryptographicException)
                        {
                            throw new BackupDecryptionException("The passphrase is wrong or the file was changed.");
                        }

                        await output.WriteAsync(plain, ct);
                        counter++;
                    }
                }

                File.Move(partial, outputPath, overwrite: true);
            }
            finally
            {
                File.Delete(partial);
            }
        }

        private static byte[] Nonce(byte[] prefix, uint counter)
        {
            var nonce = new byte[12];
            prefix.CopyTo(nonce, 0);
            BinaryPrimitives.WriteUInt64BigEndian(nonce.AsSpan(4), counter);
            return nonce;
        }

        private static byte[] Associated(uint counter, bool last)
        {
            var data = new byte[5];
            BinaryPrimitives.WriteUInt32BigEndian(data, counter);
            data[4] = last ? (byte)1 : (byte)0;
            return data;
        }

        private static async Task<int> ReadFullAsync(Stream stream, byte[] buffer, CancellationToken ct)
        {
            var total = 0;
            while (total < buffer.Length)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(total), ct);
                if (read == 0)
                {
                    break;
                }

                total += read;
            }

            return total;
        }
    }
}
