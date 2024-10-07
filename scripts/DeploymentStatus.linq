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
  <Namespace>System.Collections.ObjectModel</Namespace>
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

	var releases = await githubService.GetReleasesWithManifests();
	var environments = await githubService.GetEnvironments();
	var deployments = await githubService.GetRecentDeployments();
	//deployments.Dump();
	
	var groupedDeployments = deployments.Where(d => d.Payload.Keys.Any()).GroupBy(d => new {Sha = d.Payload["full_sha"], BuildDate = d.Payload["build_date"]});
	//groupedDeployments.Dump();
	foreach (var group in groupedDeployments)
	{
		var release = group.First().Payload;
		//release.Dump();
		var envsWithStatus = group.Select(g => new {g.EnvironmentName, g.ActualState, g.CreatedAt}).ToList();
		var envsWithoutStatus = environments.Select(e => new {EnvironmentName= e.Name, ActualState = "notDeployed", CreatedAt = DateTimeOffset.MinValue});
		var composite = envsWithStatus.UnionBy(envsWithoutStatus, x => x.EnvironmentName);
		
		composite.Dump(release["full_sha"] + " " + release["build_date"]);
	}
	
	if (releases.Count == 0 || environments.Count == 0)
	{
		Console.WriteLine("No releases or environments found.");
		return;
	}

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
	private static readonly int MaxDeploymentsPerPage = 100;
	private readonly Config _config;
	
	public GitHubService(Config config)
	{
		var repoParts = config.Repository.Split('/');
		_owner = repoParts[0];
		_name = repoParts[1];
		_config = config;
		_client = CreateGitHubClient(config.ClientName, config.Pat);
	}

	public async Task<List<Depl>> GetRecentDeployments()
	{
		var allDeployments = await _client.Repository.Deployment.GetAll(_owner, _name,new ApiOptions{
			PageSize = MaxDeploymentsPerPage,
			PageCount = 1
		});
		var deployments = allDeployments.Select(async x =>
		{
			var statuses = await _client.Repository.Deployment.Status.GetAll(_owner, _name, x.Id);
			var latestStatus = statuses.OrderByDescending(x => x.CreatedAt).FirstOrDefault();
			var actualState = latestStatus?.State.StringValue ?? "notdeployed";
			var d = new Depl(x.Id, x.Sha, x.CreatedAt, x.Environment, x.Description, x.Payload, actualState);
			return d;
		}); 
		await Task.WhenAll(deployments);
		
		return deployments.Select(x => x.Result).ToList();
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

public record Rel(long Id, string Commit, DateTimeOffset CreatedAt, Manifest Manifest);
public record Env(long Id, string Name);
public record Depl(long Id, string Commit, DateTimeOffset CreatedAt, string EnvironmentName, string Name, IReadOnlyDictionary<string,string> Payload, string ActualState);
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
