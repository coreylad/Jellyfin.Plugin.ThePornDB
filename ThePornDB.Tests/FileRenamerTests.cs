using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ThePornDB.Helpers;
using ThePornDB.Models;
using Xunit;

namespace ThePornDB.Tests
{
    public sealed class FileRenamerTests : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "tpdb-tests-" + Guid.NewGuid());

        public FileRenamerTests()
        {
            Directory.CreateDirectory(this.directory);
        }

        [Fact]
        public void BuildsPortableNameWithIdentityAndExtension()
        {
            var name = FileRenamer.BuildName(new Scene
            {
                UUID = "scene-id",
                Title = "A: Title/With?Characters",
                Site = new Site { Name = "Studio" },
                Date = new DateTime(2024, 1, 15),
            }, "{studio} - {release_date} - {title}", ".MKV");
            Assert.Equal("Studio - 2024-01-15 - A Title With Characters [TPDBID=scene-id].MKV", name);
        }

        [Theory]
        [InlineData("{unknown}")]
        [InlineData(" ")]
        [InlineData("/?:")]
        public void RejectsInvalidFormats(string format)
        {
            Assert.Throws<ArgumentException>(() => FileRenamer.BuildName(new Scene { UUID = "id", Title = "Title" }, format, ".mp4"));
        }

        [Fact]
        public void LimitsUtf8NameWithoutBreakingSurrogates()
        {
            var name = FileRenamer.BuildName(new Scene { UUID = "id", Title = string.Concat(Enumerable.Repeat("\U0001F600", 100)) }, "{title}", ".mp4");
            Assert.True(Encoding.UTF8.GetByteCount(name) <= 240);
            Assert.DoesNotContain("\uFFFD", Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(name)));
            Assert.EndsWith("[TPDBID=id].mp4", name);
        }

        [Fact]
        public async Task RenamesInPlaceAndPersistsLibraryPath()
        {
            var source = this.CreateFile("original.mp4", "video");
            string savedPath = null;
            await FileRenamer.Rename(source, "friendly.mp4", destination =>
            {
                savedPath = destination;
                return Task.CompletedTask;
            });
            Assert.False(File.Exists(source));
            Assert.Equal(Path.Combine(this.directory, "friendly.mp4"), savedPath);
            Assert.Equal("video", File.ReadAllText(savedPath));
        }

        [Fact]
        public async Task NeverOverwritesCollision()
        {
            var source = this.CreateFile("original.mp4", "original");
            var destination = this.CreateFile("friendly.mp4", "existing");
            await Assert.ThrowsAsync<IOException>(() => FileRenamer.Rename(source, "friendly.mp4", _ => throw new Exception("Must not persist")));
            Assert.Equal("original", File.ReadAllText(source));
            Assert.Equal("existing", File.ReadAllText(destination));
        }

        [Fact]
        public async Task RollsBackMoveWhenLibraryUpdateFails()
        {
            var source = this.CreateFile("original.mp4", "original");
            await Assert.ThrowsAsync<InvalidOperationException>(() => FileRenamer.Rename(source, "friendly.mp4", _ => throw new InvalidOperationException("Database failure")));
            Assert.True(File.Exists(source));
            Assert.False(File.Exists(Path.Combine(this.directory, "friendly.mp4")));
        }

        [Theory]
        [InlineData("original.nfo")]
        [InlineData("original.en.srt")]
        [InlineData("original-poster.jpg")]
        [InlineData("original.mkv")]
        public void RefusesToSeparateVideoFromSidecarsAndVersions(string sidecar)
        {
            var source = this.CreateFile("original.mp4");
            this.CreateFile(sidecar);
            Assert.Throws<IOException>(() => FileRenamer.ValidateSource(source));
            Assert.True(File.Exists(source));
        }

        [Fact]
        public async Task RejectsPathTraversal()
        {
            var source = this.CreateFile("original.mp4");
            await Assert.ThrowsAsync<ArgumentException>(() => FileRenamer.Rename(source, "../outside.mp4", _ => Task.CompletedTask));
        }

        [Fact]
        public void RejectsSymlink()
        {
            var source = this.CreateFile("original.mp4");
            var link = Path.Combine(this.directory, "link.mp4");
            File.CreateSymbolicLink(link, source);
            Assert.Throws<IOException>(() => FileRenamer.ValidateSource(link));
        }

        [Fact]
        public void DefersRecentlyModifiedFiles()
        {
            var source = this.CreateFile("original.mp4");
            File.SetLastWriteTimeUtc(source, DateTime.UtcNow);
            Assert.Throws<IOException>(() => FileRenamer.ValidateSource(source));
        }

        [Fact]
        public void DoesNotInterpretPlaceholdersInsideMetadata()
        {
            Assert.Equal("Title {actors} [TPDBID=id].mp4", FileRenamer.BuildName(new Scene { UUID = "id", Title = "Title {actors}" }, "{title}", ".mp4"));
        }

        [Fact]
        public void EscapesWindowsReservedBasenames()
        {
            Assert.StartsWith("_CON.", FileRenamer.BuildName(new Scene { UUID = "id", Title = "CON.title" }, "{title}", ".mp4"));
        }

        public void Dispose()
        {
            Directory.Delete(this.directory, true);
        }

        private string CreateFile(string name, string content = "")
        {
            var path = Path.Combine(this.directory, name);
            File.WriteAllText(path, content);
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(-5));
            return path;
        }
    }
}
