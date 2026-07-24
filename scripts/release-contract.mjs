export const releaseVersion = "0.1.2";
export const publishedVersion = "0.1.2";
export const releaseTag = `v${releaseVersion}`;
export const packageName = "sanjaya-mcp";
export const registryName = "io.github.Priyadarshiswain/sanjaya";

// npm publication is independently verified. Registry publication and the
// public VS Code install link remain separately approval-gated.
export const publicationState = "published";
export const registryPublicationState = "unpublished";
export const vsCodeInstallState = "registry_pending";

export const releaseArtifactDirectory = "dist/release";
export const releaseTarballName = `${packageName}-${releaseVersion}.tgz`;

export function assertReleasePackage(packageDocument) {
  if (packageDocument.name !== packageName || packageDocument.version !== releaseVersion) {
    throw new Error(`Release metadata must identify ${packageName}@${releaseVersion}.`);
  }
  if (Object.hasOwn(packageDocument, "private")) {
    throw new Error("The release candidate package must not contain npm's private publication lock.");
  }
  const expectedPublishConfig = { access: "public", provenance: true };
  if (JSON.stringify(packageDocument.publishConfig) !== JSON.stringify(expectedPublishConfig)) {
    throw new Error("The release candidate must require public access and npm provenance.");
  }
}

export function assertUnpublishedRelease() {
  if (
    publicationState !== "candidate"
    || publishedVersion === releaseVersion
  ) {
    throw new Error(
      `Version ${releaseVersion} is already published and immutable; `
      + "prepare a new version before building publication evidence.",
    );
  }
}
