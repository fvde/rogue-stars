using Google.Api.Gax;
using Google.Cloud.Firestore;
using RogueStars.Server.Infrastructure;

namespace RogueStars.Server.FirestoreTests;

/// <summary>
/// A test that needs the Firestore emulator. Skipped (not failed) when <c>FIRESTORE_EMULATOR_HOST</c> is not set, so
/// <c>dotnet test RogueStars.slnx</c> never needs the emulator. Run: <c>scripts/firestore-tests.sh</c>.
/// </summary>
public sealed class FirestoreFactAttribute : FactAttribute
{
    public FirestoreFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FIRESTORE_EMULATOR_HOST")))
            Skip = "Firestore emulator not running (FIRESTORE_EMULATOR_HOST unset) — run scripts/firestore-tests.sh";
    }
}

public static class Emulator
{
    public static string ProjectId => Environment.GetEnvironmentVariable("GCLOUD_PROJECT") ?? "demo-roguestars";

    private static readonly Lazy<FirestoreDb> LazyDb = new(() => new FirestoreDbBuilder
    {
        ProjectId = ProjectId,
        EmulatorDetection = EmulatorDetection.EmulatorOnly,
    }.Build());

    public static FirestoreDb Db => LazyDb.Value;

    /// <summary>Store options with a unique collection prefix, so tests never see each other's documents.</summary>
    public static StoreOptions Options() => new() { Mode = "firestore", FirestoreProjectId = ProjectId, CollectionPrefix = "it" + Guid.NewGuid().ToString("N")[..10] + "_" };
}
