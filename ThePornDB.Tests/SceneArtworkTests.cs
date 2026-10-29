using System.Linq;
using MediaBrowser.Model.Entities;
using Newtonsoft.Json;
using ThePornDB.Configuration;
using ThePornDB.Models;
using ThePornDB.Providers;
using Xunit;

namespace ThePornDB.Tests
{
    public class SceneArtworkTests
    {
        [Theory]
        [InlineData("full-cover", "large-cover", "poster", "full-cover")]
        [InlineData(null, "large-cover", "poster", "large-cover")]
        [InlineData(null, null, "poster", "poster")]
        [InlineData(" ", "", "poster", "poster")]
        [InlineData(null, null, null, "background")]
        public void PrefersEveryPosterSourceBeforeBackground(string full, string large, string poster, string expected)
        {
            var scene = new Scene
            {
                Posters = new Image { Full = full, Large = large },
                Poster = poster,
                Background = new Image { Large = "background" },
            };

            Assert.Equal(expected, MetadataAPI.GetScenePrimaryImage(scene, ScenesImageStyle.Poster));
            Assert.Equal(expected, MetadataAPI.GetSceneImages(scene, ScenesImageStyle.Poster)
                .Single(image => image.Type == ImageType.Primary).Url);
        }

        [Fact]
        public void DeserializesFullSizeBoxArtAndKeepsScreenshotsAsBackdrops()
        {
            var scene = JsonConvert.DeserializeObject<Scene>(@"{
                'poster': 'preview',
                'posters': { 'full': 'https://example.test/1000x1500/cover.jpg', 'large': 'large-cover' },
                'background': { 'full': 'full-background', 'large': 'large-background' },
                'background_back': { 'full': 'back-cover' },
                'site': { 'logo': 'studio-logo' }
            }");

            var images = MetadataAPI.GetSceneImages(scene, ScenesImageStyle.Poster);

            Assert.Equal(4, images.Count);
            Assert.Equal(ImageType.Primary, images[0].Type);
            Assert.Equal("https://example.test/1000x1500/cover.jpg", images[0].Url);
            Assert.Equal(1000, images[0].Width);
            Assert.Equal(1500, images[0].Height);
            Assert.Equal(new[] { "full-background", "back-cover" },
                images.Where(image => image.Type == ImageType.Backdrop).Select(image => image.Url));
            Assert.Equal("studio-logo", images.Single(image => image.Type == ImageType.Logo).Url);
        }

        [Fact]
        public void RespectsExplicitBackgroundPreferenceAndFallsBackToPoster()
        {
            var scene = new Scene
            {
                Poster = "cover",
                Background = new Image { Full = "full-background", Large = "large-background" },
            };

            Assert.Equal("full-background", MetadataAPI.GetScenePrimaryImage(scene, ScenesImageStyle.Background));
            scene.Background = default;
            Assert.Equal("cover", MetadataAPI.GetScenePrimaryImage(scene, ScenesImageStyle.Background));
        }

        [Fact]
        public void MissingArtworkDoesNotProduceEmptyImageEntries()
        {
            Assert.Null(MetadataAPI.GetScenePrimaryImage(default, ScenesImageStyle.Poster));
            Assert.Empty(MetadataAPI.GetSceneImages(default, ScenesImageStyle.Poster));
            Assert.Equal(ScenesImageStyle.Poster, new PluginConfiguration().ScenesImage);
        }
    }
}
