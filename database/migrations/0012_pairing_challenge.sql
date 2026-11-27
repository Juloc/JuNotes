-- A pairing code made by the sign-in window of the app is bound to that app (ARCHITECTURE 11, PKCE as in RFC 7636): the app sends the SHA-256 of a secret only it
-- knows (base64url, 43 characters), and the code is only redeemed together with that secret. A code from "Gerät koppeln" (typed or scanned) has none.
alter table pairing_codes add column challenge text;
