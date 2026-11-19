using System.Security.Cryptography;
using System.Text;

namespace JuNotes.Backend.Service
{
    // Phase 1 guards the sync API with one token for the instance, set in the configuration. Devices send it as a bearer token.
    public sealed class SyncAccess(string? token)
    {
        public bool IsConfigured => !string.IsNullOrEmpty(token);

        public bool IsAuthorized(string? authorizationHeader)
        {
            if (!IsConfigured || authorizationHeader is null || !authorizationHeader.StartsWith("Bearer ", StringComparison.Ordinal))
            {
                return false;
            }

            // Hashes first, so the comparison takes the same time whatever was sent.
            var sent = SHA256.HashData(Encoding.UTF8.GetBytes(authorizationHeader["Bearer ".Length..]));
            var expected = SHA256.HashData(Encoding.UTF8.GetBytes(token!));
            return CryptographicOperations.FixedTimeEquals(sent, expected);
        }
    }
}
