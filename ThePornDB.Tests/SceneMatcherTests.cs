using System;
using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Model.Providers;
using ThePornDB.Helpers;
using ThePornDB.Models;
using Xunit;

namespace ThePornDB.Tests
{
    public class SceneMatcherTests
    {
        private static Scene ExampleScene => new Scene
        {
            UUID = "12345678-1234-1234-1234-123456789012",
            Title = "A Day Outside",
            Date = new DateTime(2024, 1, 15),
            Site = new Site { Name = "Example Studio" },
            Performers = new List<PerformerSite>
            {
                new PerformerSite { Name = "Jane Doe" },
                new PerformerSite { Name = "Alex Smith" },
            },
        };

        [Theory]
        [InlineData("ExampleStudio.2024.01.15.A.Day.Outside.Jane.Doe.1080p.x264", 100)]
        [InlineData("ExampleStudio.24.01.15.A.Day.Outside.XXX.2160", 100)]
        [InlineData("ExampleStudio.2024.01.14.A.Day.Outside", 100)]
        [InlineData("ExampleStudio.2024.01.16.A.Day.Outside", 100)]
        [InlineData("ExampleStudio.2024.01.15.Jane.Doe", 100)]
        [InlineData("ExampleStudio.2024.01.15.Jane.Doe.Alex.Smith", 100)]
        [InlineData("A Day Outside", 100)]
        [InlineData("Outside A Day", 100)]
        [InlineData("OtherStudio.2024.01.15.A.Day.Outside", 0)]
        [InlineData("ExampleStudio.2024.01.19.A.Day.Outside", 40)]
        [InlineData("ExampleStudio.2024.13.15.A.Day.Outside", 40)]
        public void ScoresStudioDateTitleAndPerformers(string input, double expected)
        {
            Assert.Equal(expected, SceneMatcher.Score(input, null, ExampleScene));
        }

        [Theory]
        [InlineData("ExampleStudio.2024.01.15.Unrelated.Title")]
        [InlineData("ExampleStudio.2024.01.15.Jane")]
        [InlineData("")]
        public void RejectsUnrelatedOrPartialPerformerNames(string input)
        {
            Assert.True(SceneMatcher.Score(input, null, ExampleScene) < 85);
        }

        [Fact]
        public void RejectsConflictingYearAndMissingDate()
        {
            Assert.Equal(60, SceneMatcher.Score("A Day Outside", 2021, ExampleScene));
            var scene = ExampleScene;
            scene.Date = null;
            Assert.Equal(40, SceneMatcher.Score("ExampleStudio.2024.01.15.A.Day.Outside", null, scene));
        }

        [Theory]
        [InlineData(85, 75, true)]
        [InlineData(85, 76, false)]
        [InlineData(84, 0, false)]
        [InlineData(100, 100, false)]
        public void RequiresThresholdAndTenPointMargin(double first, double second, bool expected)
        {
            Assert.Equal(expected, SceneMatcher.IsConfident(new List<RemoteSearchResult>
            {
                new MatchedSearchResult { Score = first },
                new MatchedSearchResult { Score = second },
            }, 85));
        }

        [Fact]
        public void KeepsLowConfidenceResultsAvailableButDoesNotAutoSelect()
        {
            var results = new List<RemoteSearchResult> { new MatchedSearchResult { Score = 50 } };
            Assert.False(SceneMatcher.IsConfident(results, 85));
            Assert.Single(results);
            Assert.False(SceneMatcher.IsConfident(new List<RemoteSearchResult>(), 85));
        }

        [Theory]
        [InlineData("/library/ExampleStudio.24.01.15.A.Day.Outside/video.mp4", "ExampleStudio.24.01.15.A.Day.Outside")]
        [InlineData("/library/folder/A.Day.Outside.mp4", "A.Day.Outside")]
        [InlineData(@"C:\library\A.Day.Outside.mp4", "A.Day.Outside")]
        public void UsesFilenameOrGenericNamesParent(string path, string expected)
        {
            Assert.Equal(expected, SceneMatcher.MatchingTitle("Ignored", path));
        }

        [Fact]
        public void QueriesAreBoundedDistinctAndKeepRawDates()
        {
            var queries = SceneMatcher.Queries("A.Day.Outside", "/library/A.Day.Outside.mp4", false).ToList();
            Assert.Equal(new[] { "A.Day.Outside", "a day outside" }, queries);
            Assert.True(SceneMatcher.Queries("Name", "/library/ExampleStudio.24.01.15.Title.mp4", true).Count() <= 3);
            Assert.Contains("ExampleStudio.24.01.15.Title", SceneMatcher.Queries("Name", "/library/ExampleStudio.24.01.15.Title.mp4", false));
        }

        [Fact]
        public void DeduplicatesBeforeApplyingConfidenceMargin()
        {
            var unrelated = ExampleScene;
            unrelated.UUID = "other-id";
            unrelated.Title = "Unrelated Title";
            var results = SceneMatcher.Rank(new[]
            {
                new MatchedSearchResult { Scene = unrelated },
                new MatchedSearchResult { Scene = ExampleScene },
                new MatchedSearchResult { Scene = ExampleScene },
            }, "A Day Outside", null);
            Assert.Equal(2, results.Count);
            Assert.Equal(ExampleScene.UUID, ((MatchedSearchResult)results[0]).Scene.UUID);
            Assert.True(SceneMatcher.IsConfident(results, 85));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(101)]
        public void RejectsInvalidThreshold(int threshold)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => SceneMatcher.IsConfident(new List<RemoteSearchResult>(), threshold));
        }
    }
}
