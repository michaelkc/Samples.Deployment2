<Query Kind="Program">
  <NuGetReference>Octokit</NuGetReference>
  <NuGetReference>System.Net.Http.Json</NuGetReference>
  <Namespace>LINQPad.Controls</Namespace>
  <Namespace>Octokit</Namespace>
  <Namespace>Octokit.Internal</Namespace>
  <Namespace>System.Net.Http</Namespace>
  <Namespace>System.Net.Http.Headers</Namespace>
  <Namespace>System.Net.Http.Json</Namespace>
  <Namespace>System.Text.Json</Namespace>
  <Namespace>System.Text.Json.Serialization</Namespace>
  <Namespace>System.Threading.Tasks</Namespace>
</Query>

async Task Main()
{
	var config = new Config
	{
		Pat = Util.GetPassword("GitHub PAT"),
		Repository = "michaelkc/Samples.Deployment2",
		ClientName = "michaelkc"
	};

	var githubService = new GitHubService(config);
	var uiService = new UIService();

	var releases = await githubService.GetReleasesWithManifests();
	var environments = await githubService.GetEnvironments();

	if (releases.Count == 0 || environments.Count == 0)
	{
		Console.WriteLine("No releases or environments found.");
		return;
	}

	var pickedRelease = await uiService.PickRelease(releases);
	var pickedEnvironment = await uiService.PickEnvironment(environments);

	var deployment = new NewDeployment(pickedRelease.Manifest.FullSha)
	{
		Payload = pickedRelease.Manifest.ToDictionary(),
		AutoMerge = false,
		Environment = pickedEnvironment.Name,
		Description = $"Deployment for release {pickedRelease.Commit}",
		RequiredContexts = null
	};

	var deploymentResult = await githubService.CreateDeployment(deployment);
	Console.WriteLine($"Deployment created successfully: {deploymentResult.Url}");
}

public class Config
{
	public string Pat { get; set; }
	public string Repository { get; set; }
	public string ClientName { get; set; }
}

public class GitHubService
{
	private readonly GitHubClient _client;
	private readonly string _owner;
	private readonly string _name;
	private static readonly int MaxReleasesPerPage = 100;
	private static readonly int MaxEnvironmentsPerPage = 100;
	private readonly Config _config;
	
	public GitHubService(Config config)
	{
		var repoParts = config.Repository.Split('/');
		_owner = repoParts[0];
		_name = repoParts[1];
		_config = config;
		_client = CreateGitHubClient(config.ClientName, config.Pat);
	}

	public async Task<List<Rel>> GetReleasesWithManifests()
	{
		var allReleases = await _client.Repository.Release.GetAll(_owner, _name, new ApiOptions
		{
			PageSize = MaxReleasesPerPage,
			PageCount = 1
		});

		var releasesWithManifests = await Task.WhenAll(allReleases.Select(async x => await LoadManifest(x)));
		return releasesWithManifests
			.Where(x => !x.Release.Draft && x.Manifest != null)
			.OrderByDescending(r => r.Release.CreatedAt)
			.Select(r => new Rel(r.Release.Id, r.Release.Name, r.Release.CreatedAt, r.Manifest))
			.ToList();
	}

	public async Task<List<Env>> GetEnvironments()
	{
		var allEnvironments = await _client.Repository.Environment.GetAll(_owner, _name, new ApiOptions { PageCount = 1, PageSize = MaxEnvironmentsPerPage });
		return allEnvironments.Environments.Select(x => new Env(x.Id, x.Name)).ToList();
	}

	public async Task<Deployment> CreateDeployment(NewDeployment deployment)
	{
		return await _client.Repository.Deployment.Create(_owner, _name, deployment);
	}

	private async Task<ReleaseManifestPair> LoadManifest(Release release)
	{
		var manifestAsset = release.Assets.FirstOrDefault(asset => asset.Name == "manifest.json");
		if (manifestAsset == null)
		{
			return new ReleaseManifestPair(release, null);
		}

		using var client = new HttpClient();
		client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("token", _client.Credentials.GetToken());
		client.DefaultRequestHeaders.UserAgent.ParseAdd(_config.ClientName);
		client.DefaultRequestHeaders.Accept.ParseAdd("application/octet-stream");

		using var response = await client.GetAsync(manifestAsset.Url, HttpCompletionOption.ResponseHeadersRead);
		if (!response.IsSuccessStatusCode)
		{
			throw new Exception($"Error downloading asset: {response.StatusCode}");
		}

		var manifestJson = await response.Content.ReadAsStringAsync();
		var manifest = JsonSerializer.Deserialize<Manifest>(manifestJson);
		if (manifest == null)
		{
			throw new Exception($"Error parsing manifest.json");
		}

		return new ReleaseManifestPair(release, manifest);
	}

	private GitHubClient CreateGitHubClient(string clientName, string pat)
	{
		return new GitHubClient(new Octokit.ProductHeaderValue(clientName))
		{
			Credentials = new Credentials(pat)
		};
	}
}

public class UIService
{
	public async Task<Rel> PickRelease(List<Rel> releases)
	{
		releases.Dump();
		var tcs = new TaskCompletionSource<Rel>();
		var selectBox = new SelectBox(new[] { "pick" }.Union(releases.Select(x => x.Commit)).Cast<object>().ToArray(), -1, sb =>
		{
			tcs.SetResult(releases.SingleOrDefault(x => x.Commit == (string)sb.SelectedOption));
			sb.Enabled = false;
		}).Dump("Choose a release");

		return await tcs.Task;
	}

	public async Task<Env> PickEnvironment(List<Env> environments)
	{
		var tcs = new TaskCompletionSource<Env>();
		var selectBox = new SelectBox(new[] { "pick" }.Union(environments.Select(x => x.Name)).Cast<object>().ToArray(), -1, sb =>
		{
			tcs.SetResult(environments.SingleOrDefault(x => x.Name == (string)sb.SelectedOption));
			sb.Enabled = false;
		}).Dump("Choose an env");

		return await tcs.Task;
	}
}

public record Rel(long Id, string Commit, DateTimeOffset CreatedAt, Manifest Manifest);
public record Env(long Id, string Name);
public record ReleaseManifestPair(Release Release, Manifest? Manifest);

public class Manifest
{
	[JsonPropertyName("release_version")]
	public string ReleaseVersion { get; set; }

	[JsonPropertyName("full_sha")]
	public string FullSha { get; set; }

	[JsonPropertyName("branch")]
	public string Branch { get; set; }

	[JsonPropertyName("build_date")]
	public DateTime BuildDate { get; set; }

	[JsonPropertyName("run_number")]
	public string RunNumber { get; set; }

	[JsonPropertyName("container_versions")]
	public string ContainerVersions { get; set; }

	public Dictionary<string, string> ToDictionary()
	{
		var json = JsonSerializer.Serialize(this);
		return JsonSerializer.Deserialize<Dictionary<string, string>>(json);
	}
}
