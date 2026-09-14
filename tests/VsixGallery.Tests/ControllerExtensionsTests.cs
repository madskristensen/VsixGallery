using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

using VsixGallery.Controllers;

namespace VsixGallery.Tests;

public class ControllerExtensionsTests
{
	private static readonly Package _package = new()
	{
		DatePublished = new DateTime(2026, 9, 13, 12, 34, 56, DateTimeKind.Utc),
	};

	[Fact]
	public void IsConditionalGet_ReturnsNotModifiedForMatchingEtagAlone()
	{
		Controller controller = CreateController();
		controller.Request.Headers.IfNoneMatch = $"\"{_package.DatePublished.Ticks}\"";

		bool notModified = controller.IsConditionalGet(_package);

		Assert.True(notModified);
		Assert.Equal(StatusCodes.Status304NotModified, controller.Response.StatusCode);
	}

	[Fact]
	public void IsConditionalGet_GivesEtagPrecedenceOverDate()
	{
		Controller controller = CreateController();
		controller.Request.Headers.IfNoneMatch = "\"different\"";
		controller.Request.Headers.IfModifiedSince = _package.DatePublished.AddDays(1).ToString("r");

		bool notModified = controller.IsConditionalGet(_package);

		Assert.False(notModified);
	}

	[Fact]
	public void IsConditionalGet_MatchesWeakEtagInAList()
	{
		Controller controller = CreateController();
		controller.Request.Headers.IfNoneMatch = $"\"old\", W/\"{_package.DatePublished.Ticks}\"";

		Assert.True(controller.IsConditionalGet(_package));
	}

	[Fact]
	public void IsConditionalGet_CollectionEtagChangesWhenAnyPackageChanges()
	{
		Package first = new() { ID = "first", Version = "1", DatePublished = _package.DatePublished };
		Package second = new() { ID = "second", Version = "1", DatePublished = _package.DatePublished.AddDays(-1) };
		Controller initial = CreateController();
		initial.IsConditionalGet([first, second]);
		string firstEtag = initial.Response.Headers.ETag.ToString();

		second.Version = "2";
		Controller changed = CreateController();
		changed.IsConditionalGet([first, second]);

		Assert.NotEqual(firstEtag, changed.Response.Headers.ETag.ToString());
	}

	[Fact]
	public void IsConditionalGet_ReturnsNotModifiedForDateAlone()
	{
		Controller controller = CreateController();
		controller.Request.Headers.IfModifiedSince = _package.DatePublished.ToString("r");

		Assert.True(controller.IsConditionalGet(_package));
	}

	private static Controller CreateController()
	{
		return new TestController
		{
			ControllerContext = new ControllerContext
			{
				HttpContext = new DefaultHttpContext(),
			},
		};
	}

	private sealed class TestController : Controller;
}
