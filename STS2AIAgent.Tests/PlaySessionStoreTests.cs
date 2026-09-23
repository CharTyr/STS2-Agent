using STS2AIAgent.Agent;

namespace STS2AIAgent.Tests;

/// <summary>
/// Offline tests for <see cref="PlaySessionStore"/>: the per-run session files that let a continued
/// run get its conversation, decision memory, and dual-layer strategy back.
/// </summary>
/// <remarks>
/// Each test runs in a fresh temp directory so the files are real but isolated, and the directory is
/// deleted afterwards. No game, no network -- the store is pure file IO over a DTO.
/// </remarks>
internal static partial class PlaySessionStoreTests
{
    public static void SaveThenLoadRoundTripsEveryField()
    {
        WithStore(store =>
        {
            var record = new PlaySessionRecord
            {
                RunId = "seed-abc",
                Character = "Ironclad",
                Chat = new List<ChatTurn>
                {
                    new() { Role = "user", Text = "先打哪张？" },
                    new() { Role = "assistant", Text = "先出打击。" }
                },
                Decisions = new List<DecisionLogEntry>
                {
                    new(1, "2026-10-05T10:00:00Z", "play", "play_card", "打最左边", "fp1", 1, 100, "seed-abc", 0.9)
                },
                Strategy = new PlayStrategy { Posture = "aggressive", Source = "llm" }
            };

            store.Save(record);
            var loaded = store.Load("seed-abc");

            Assert.NotNull(loaded);
            Assert.Equal("seed-abc", loaded!.RunId);
            Assert.Equal("Ironclad", loaded.Character);
            Assert.Equal(2, loaded.Chat!.Count);
            Assert.Equal("先打哪张？", loaded.Chat[0].Text);
            Assert.Single(loaded.Decisions!);
            Assert.Equal("play_card", loaded.Decisions![0].action);
            Assert.Equal(0.9, loaded.Decisions[0].confidence);
            Assert.NotNull(loaded.Strategy);
            Assert.Equal("aggressive", loaded.Strategy!.Posture);
            // The store stamps the write time on the way out.
            Assert.NotNull(loaded.UpdatedAt);
        });
    }

    public static void LoadReturnsNullWhenNoSessionExists()
    {
        WithStore(store =>
        {
            Assert.Null(store.Load("never-saved"));
        });
    }

    public static void RunUnknownIsNeverPersisted()
    {
        WithStore(store =>
        {
            store.Save(new PlaySessionRecord { RunId = "run_unknown", Chat = new List<ChatTurn>() });
            Assert.Null(store.Load("run_unknown"));
            // No file may be written for the placeholder; the directory may not even exist yet.
            var wroteFile = System.IO.Directory.Exists(store.Directory)
                && Directory.GetFiles(store.Directory).Any();
            Assert.False(wroteFile, "run_unknown must not write a file");
        });
    }

    public static void SessionsAreIsolatedByRunId()
    {
        WithStore(store =>
        {
            store.Save(new PlaySessionRecord
            {
                RunId = "run-one",
                Chat = new List<ChatTurn> { new() { Role = "user", Text = "第一局" } }
            });
            store.Save(new PlaySessionRecord
            {
                RunId = "run-two",
                Chat = new List<ChatTurn> { new() { Role = "user", Text = "第二局" } }
            });

            Assert.Equal("第一局", store.Load("run-one")!.Chat![0].Text);
            Assert.Equal("第二局", store.Load("run-two")!.Chat![0].Text);
        });
    }

    public static void CorruptFileIsMovedAsideAndBackupRestored()
    {
        WithStore(store =>
        {
            // Establish a good session, which also leaves a .bak once it is overwritten.
            store.Save(new PlaySessionRecord
            {
                RunId = "seed-x",
                Chat = new List<ChatTurn> { new() { Role = "user", Text = "good" } }
            });
            store.Save(new PlaySessionRecord
            {
                RunId = "seed-x",
                Chat = new List<ChatTurn> { new() { Role = "user", Text = "good-v2" } }
            });

            var path = Directory.GetFiles(store.Directory, "seed-x.json").Single();
            File.WriteAllText(path, "{ this is not json");

            var loaded = store.Load("seed-x");
            Assert.NotNull(loaded);
            Assert.NotEmpty(loaded!.Chat!);
            Assert.True(Directory.GetFiles(store.Directory, "seed-x.json.corrupt-*").Any(),
                "the corrupt file should be moved aside");
        });
    }

    public static void CorruptFileWithoutBackupYieldsNullNotThrow()
    {
        WithStore(store =>
        {
            var path = Path.Combine(store.Directory, "seed-y.json");
            System.IO.Directory.CreateDirectory(store.Directory);
            File.WriteAllText(path, "not json at all");

            Assert.Null(store.Load("seed-y"));
        });
    }

    public static void ChatIsTrimmedToTheCapKeepingNewest()
    {
        WithStore(store =>
        {
            var turns = Enumerable.Range(0, PlaySessionStore.MaxChatTurns + 20)
                .Select(i => new ChatTurn { Role = "user", Text = "turn-" + i })
                .ToList();
            store.Save(new PlaySessionRecord { RunId = "seed-cap", Chat = turns });

            var loaded = store.Load("seed-cap")!;
            Assert.Equal(PlaySessionStore.MaxChatTurns, loaded.Chat!.Count);
            // The newest survive; the oldest were dropped.
            Assert.Equal("turn-" + (PlaySessionStore.MaxChatTurns + 19), loaded.Chat[^1].Text);
        });
    }

    public static void SecretsInChatTextAreRedactedBeforeDisk()
    {
        WithStore(store =>
        {
            store.Save(new PlaySessionRecord
            {
                RunId = "seed-secret",
                Chat = new List<ChatTurn>
                {
                    new() { Role = "user", Text = "my key is sk-abcdefghijklmnopqrstuvwxyz0123456789" }
                }
            });

            var path = Directory.GetFiles(store.Directory, "seed-secret.json").Single();
            var raw = File.ReadAllText(path);
            Assert.False(raw.Contains("sk-abcdefghijklmnopqrstuvwxyz0123456789", StringComparison.Ordinal),
                "the raw file must not contain the API key");
        });
    }

    public static void DeleteRemovesTheSession()
    {
        WithStore(store =>
        {
            store.Save(new PlaySessionRecord { RunId = "seed-del", Chat = new List<ChatTurn>() });
            Assert.NotNull(store.Load("seed-del"));
            store.Delete("seed-del");
            Assert.Null(store.Load("seed-del"));
            // Deleting again is not an error.
            store.Delete("seed-del");
        });
    }

    public static void RunIdWithPathSeparatorsCannotEscapeTheDirectory()
    {
        WithStore(store =>
        {
            store.Save(new PlaySessionRecord
            {
                RunId = "../evil/..\\seed",
                Chat = new List<ChatTurn> { new() { Role = "user", Text = "x" } }
            });

            // The file landed inside the session directory, not outside it.
            var files = Directory.GetFiles(store.Directory);
            Assert.Single(files);
            Assert.True(Path.GetFullPath(files[0]).StartsWith(Path.GetFullPath(store.Directory), StringComparison.Ordinal),
                "the session file must stay inside the sessions directory");
        });
    }

    /// <summary>Runs <paramref name="body"/> against a store in a fresh temp dir, then cleans up.</summary>
    private static void WithStore(Action<PlaySessionStore> body)
    {
        var directory = Path.Combine(Path.GetTempPath(), "sts2-session-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            body(new PlaySessionStore(directory));
        }
        finally
        {
            try
            {
                if (System.IO.Directory.Exists(directory))
                {
                    System.IO.Directory.Delete(directory, recursive: true);
                }
            }
            catch (IOException)
            {
            }
        }
    }
}
