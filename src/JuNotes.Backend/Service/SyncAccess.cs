using System.Security.Cryptography;
using System.Text;

namespace JuNotes.Backend.Service
{
    // Phase 1 guards the sync API with one token for the instance, set in the configuration. Devices send it as a bearer token.
    public sealed class SyncAccess
    {
        public const int MinimumLength = 32;

        private readonly string? _token;

        // A token that is too short is refused at the start, not accepted quietly: it is the one key to every note of the first administrator.
        public SyncAccess(string? token)
        {
            if (!string.IsNullOrEmpty(token) && token.Length < MinimumLength)
            {
                throw new InvalidOperationException($"Sync__Token has {token.Length} characters; it needs at least {MinimumLength} (any long random value, for example `openssl rand -base64 32`).");
            }

            _token = token;
        }

        public bool IsConfigured => !string.IsNullOrEmpty(_token);

        public bool IsAuthorized(string? authorizationHeader)
        {
            if (!IsConfigured || authorizationHeader is null || !authorizationHeader.StartsWith("Bearer ", StringComparison.Ordinal))
            {
                return false;
            }

            // Hashes first, so the comparison takes the same time whatever was sent.
            var sent = SHA256.HashData(Encoding.UTF8.GetBytes(authorizationHeader["Bearer ".Length..]));
            var expected = SHA256.HashData(Encoding.UTF8.GetBytes(_token!));
            return CryptographicOperations.FixedTimeEquals(sent, expected);
        }
    }
}
