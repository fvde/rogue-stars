using UnityEngine;

namespace RogueStars.Client.Services
{
    /// <summary>Picks the auth provider for an online session (composition root helper).</summary>
    public static class AuthFactory
    {
        private static FirebaseAuthProvider _firebase;

        /// <summary>
        /// "firebase" on a device → the app-wide <see cref="FirebaseAuthProvider"/> (Firebase SDK + Play Games when configured).
        /// Anything else, and always in the editor (the Firebase desktop libraries are not used here) → null = dev auth.
        /// </summary>
        public static IAuthProvider For(string authMode)
        {
            if (authMode != AuthModes.Firebase || Application.isEditor) return null;
            return _firebase ?? (_firebase = new FirebaseAuthProvider(new FirebaseSdkBackend(), GooglePlayGamesSignIn.Create()));
        }
    }
}
