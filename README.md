# Samples.Deployment2
Experiments in providing an Octopus-style deployment experience

`CI.yml` workflow produces a GitHub Release, which contains
- a manifest with metadata and pointing to out-of-file resources built, e.g. containers or nuget packages with versions
- holds any in-file resources, e.g. a zip file with built binaries
This is roughly equivalent to an Octopus Release, minus the config stuff (which this does not try to handle, as it should be externalized from release process anyway)
It runs on every commit, so each commit=a release.
The release is named with the short-sha, as release names cannot be as long as the complete sha.

`DeploymentCreator.linq` lists releases in the project, environments in the project, and offers to create a deployment of a release to an environment. It expects a PAT, and since it is not a GitHub triggered workflow, the deployment can trigger workflows of its own. Specifically it will trigger

`CD.yml` workflow does a fake, no-op deployment. It has full access to the manifest metadata, so it could load up binaries from releases, use specific container versions etc. The point here is that it is a regular GH actions workflow, so it can use all the good stuff here, e.g. deployment actions to Container Apps, App Service etc. based on the metadata.

Finally `DeploymentStatus.linq` looks at deployments, and for each deployment lists status as it is seen across environments.
This is a very rough draft of the data required to render an Octopus-style deployment matrix, with releases on the Y-axis and environments on the X-axis.
Currently it does not handle things like

- sorting releases by creation date
- sorting environments by their progression (e.g. dev before devtest before prod)
- handling undeployed releases (not shown)
- interpreting status to action (e.g. "success" becomes "redeploy", all other becomes "deploy")
- the slick logic Octopus uses to order and fade in/out releases not currently active but deployed earlier

In a finalized setup, DeploymentCreator and DeploymentStatus would merge together in an Octopus-release-matrix-like application.
