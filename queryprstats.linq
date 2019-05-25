<Query Kind="Program">
  <Reference>&lt;ProgramFilesX64&gt;\Microsoft SDKs\Azure\.NET SDK\v2.9\bin\plugins\Diagnostics\Newtonsoft.Json.dll</Reference>
  <Reference>&lt;RuntimeDirectory&gt;\System.Net.Http.dll</Reference>
  <Reference>&lt;RuntimeDirectory&gt;\System.Windows.Forms.DataVisualization.dll</Reference>
  <Reference>&lt;RuntimeDirectory&gt;\System.Windows.Forms.dll</Reference>
  <Namespace>System.Net.Http</Namespace>
  <Namespace>System.Net.Http.Headers</Namespace>
  <Namespace>System.Threading.Tasks</Namespace>
  <Namespace>Newtonsoft.Json.Linq</Namespace>
  <Namespace>System.Globalization</Namespace>
  <Namespace>System.Windows.Forms.DataVisualization.Charting</Namespace>
  <Namespace>System.Drawing</Namespace>
</Query>

private const string Organization = "...";
private const string Repository = "...";

private const string Token = "...";

private const string UserId = "f02efe5a-1041-6151-b007-15eeae3eadf1";
private static readonly DateTimeOffset FromTime = new DateTimeOffset(new DateTime(2019, 4, 1));

async Task Main()
{
	var stats = new Dictionary<DateTimeOffset, Statistics>();

	using (HttpClient client = new HttpClient())
	{
		client.DefaultRequestHeaders.Accept.Add(
			new MediaTypeWithQualityHeaderValue("application/json"));

		client.DefaultRequestHeaders.Authorization = 
			new AuthenticationHeaderValue(
				"Basic",
				Convert.ToBase64String(ASCIIEncoding.ASCII.GetBytes(string.Format("{0}:{1}", "", Token))));

		var prs = await FetchPullRequests(client, UserId, FromTime);
		foreach (var pr in prs)
		{
			// Console.WriteLine(pr.Id + ": " + pr.Title + " (" + pr.CreationDate + ")");
			
			var comments = await FetchComments(client, pr.Id, UserId);
			comments = comments.Where(c => !c.IsScr && !c.IsVote && !c.IsStatusUpdate);
			foreach (var comment in comments)
			{
				// Console.WriteLine("  - " + comment.Content);	
			}
			
			var day = pr.CreationDate.Date;
			var monday = this.GetMonday(day);
			Add(stats, monday, comments, UserId);
		}
	}
	
	// stats.Dump();
	new ColumnChart(stats).Dump();
}

private DateTimeOffset GetMonday(DateTimeOffset day)
{
	return day.AddDays((int) DayOfWeek.Monday - (int) day.DayOfWeek);
}

class Statistics
{
	public void Add(Comment comment, string userId)
	{
		TotalComments++;
		if (comment.Likes.Contains(userId))
		{
			LikedComments++;
		}
	}
	
	public int TotalComments { private set; get; }
	public int LikedComments { private set; get; }
}

private static void Add(Dictionary<DateTimeOffset, Statistics> dict, DateTimeOffset key, IEnumerable<Comment> comments, string userId)
{
	foreach (var comment in comments)
	{
		dict.TryGetValue(key, out var stats);
		if (stats == null)
		{
			stats = new Statistics();
		}

		stats.Add(comment, userId);
		dict[key] = stats;
	}
}

static async Task<IEnumerable<Comment>> FetchComments(HttpClient client, string prId, string authorId)
{
	var url = "https://msazure.visualstudio.com/" + Organization + "/_apis/git/repositories/" + Repository + "/pullrequests/" +
		prId +
		"/threads?api-version=5.0";

	var result = new List<Comment>();
	using (HttpResponseMessage response = await client.GetAsync(url))
	{
		response.EnsureSuccessStatusCode();
		string body = await response.Content.ReadAsStringAsync();
		JObject json = JObject.Parse(body);
		
		foreach (var thread in json["value"])
		{
			foreach (var token in thread["comments"])
			{
				var comment = Comment.Parse(token);
				if (comment.AuthorId == authorId)
				{
					result.Add(comment);
				}
			}
		}
	}
	
	return result;
}

class Comment
{
	public Comment(string authorId, string content, IEnumerable<string> likes)
	{
		this.AuthorId = authorId;
		this.Content = content;
		this.Likes = likes;
	}
	
	public static Comment Parse(JToken json)
	{
		return new Comment(
			(string) json["author"]["id"],
			(string) json["content"],
			ParseLikes(json));
	}
	
	public static IEnumerable<string> ParseLikes(JToken json)
	{
		return json["usersLiked"].Select(l => (string) l["id"]);
	}

	public string AuthorId { private set; get; }
	public string Content { private set; get; }
	public IEnumerable<string> Likes { private set; get; }	
	public bool IsScr => Content.Contains("No SCR issues found");
	public bool IsVote => Content.Contains("voted");
	public bool IsStatusUpdate => Content.Contains("updated the pull request status");
}

private static async Task<List<PullRequest>> FetchPullRequests(
	HttpClient client, string reviewerId, DateTimeOffset minCreationDate)
{
	var result = new List<PullRequest>();
	
	int skip = 0, top = 1000;
	int count;
	do
	{
		var url = "https://msazure.visualstudio.com/" + Organization + "/_apis/git/repositories/" + Repository + "/pullrequests?" +
			"searchCriteria.status=all" +
			"&searchCriteria.reviewerId=" + reviewerId +
			"&$skip=" + skip +
			"&$top=" + top +
			"&api-version=5.0";
		
		using (HttpResponseMessage response = await client.GetAsync(url))
		{
			response.EnsureSuccessStatusCode();
			string body = await response.Content.ReadAsStringAsync();
			JObject json = JObject.Parse(body);

			count = 0;
			foreach (var token in json["value"])
			{
				count++;
				var pr = PullRequest.Parse(token);
				if (pr.CreationDate >= minCreationDate)
				{
					result.Add(pr);
				}
			}

			skip += top;
		}
	}
	while (count > 0);
	
	return result;
}

class PullRequest
{
	public PullRequest(string id, string title, DateTimeOffset creationDate)
	{
		this.Id = id;
		this.Title = title;
		this.CreationDate = creationDate;
	}
	
	public static PullRequest Parse(JToken json)
	{
		return new PullRequest(
			(string) json["pullRequestId"],
			(string) json["title"],
			(DateTimeOffset) json["creationDate"]);
	}

	public string Id { private set; get; }
	public string Title { private set; get; }
	public DateTimeOffset CreationDate { private set; get; }
}

class ColumnChart
{
	private Bitmap bitmap;
	
	public ColumnChart(Dictionary<DateTimeOffset, Statistics> data)
	{
		var chart = new Chart();
		chart.Width = 1200;
		chart.Height = 600;
		var chartArea = new ChartArea();
		chart.ChartAreas.Add(chartArea);
		
		var sortedData = data.OrderBy(kv => kv.Key).ToList();
		var keys = sortedData.Select(kv => kv.Key.ToString()).ToArray();
		
		var totalComments = sortedData.Select(kv => kv.Value.TotalComments).ToArray();
		this.AddSeries(chart, keys, totalComments, SeriesChartType.Column);
		
		var likedComments = sortedData.Select(kv => kv.Value.LikedComments).ToArray();
		this.AddSeries(chart, keys, likedComments, SeriesChartType.Line);

		this.bitmap = new Bitmap(width: chart.Width, height: chart.Height);
		chart.DrawToBitmap(this.bitmap, chart.Bounds);
	}

	private void AddSeries(Chart chart, string[] keys, int[] values, SeriesChartType type)
	{
		var series = new Series();
		series.ChartType = type;
		series.BorderWidth = 5;
		series.Points.DataBindXY(keys, values);
		chart.Series.Add(series);
	}

	public void Dump()
	{
		this.bitmap.Dump();
	}
}
